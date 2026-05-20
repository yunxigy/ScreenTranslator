using ScreenTranslator.Models;
using ScreenTranslator.Native;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;

namespace ScreenTranslator.Core;

/// <summary>
/// HwndSource 透明覆盖窗口 — 替代传统 WPF Window
/// 通过 Win32 层叠窗口实现完全透明 + 鼠标穿透
/// </summary>
internal sealed class OverlayWindow : IDisposable
{
    // ===== Win32 窗口样式 =====
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    // ===== 组件 =====
    private HwndSource? _hwndSource;
    private TranslationOverlay? _overlay;
    private DispatcherTimer? _translationTimer;
    private readonly ScreenCapture _screenCapture;
    private readonly OcrEngine _ocrEngine;
    private BaiduTranslator _translator;
    private readonly HotKeyManager _hotKeyManager;
    private readonly Settings _settings;

    // ===== 状态 =====
    private bool _isTranslating;
    private bool _disposed;
    private bool _isTickRunning;
    private IntPtr _targetWindowHandle;
    private int _screenOffsetX;
    private int _screenOffsetY;
    private int _tickCount;

    // ===== 事件 =====
    internal event Action? WindowCreated;
    internal event Action? WindowDestroyed;
    internal event Action<bool>? TranslationStateChanged;

    /// <summary>窗口句柄</summary>
    internal IntPtr Handle => _hwndSource?.Handle ?? IntPtr.Zero;

    /// <summary>是否正在翻译</summary>
    internal bool IsTranslating => _isTranslating;

    /// <summary>当前配置</summary>
    internal Settings Settings => _settings;

    internal OverlayWindow()
    {
        _settings = Settings.Load();
        _screenCapture = new ScreenCapture();
        _ocrEngine = new OcrEngine();
        _translator = new BaiduTranslator(_settings.BaiduAppId, _settings.BaiduSecretKey);
        _hotKeyManager = new HotKeyManager();
    }

    /// <summary>
    /// 创建 HwndSource 透明覆盖窗口
    /// </summary>
    internal void Create()
    {
        if (_hwndSource != null) return;

        var virtualLeft = (int)SystemParameters.VirtualScreenLeft;
        var virtualTop = (int)SystemParameters.VirtualScreenTop;
        var virtualWidth = (int)SystemParameters.VirtualScreenWidth;
        var virtualHeight = (int)SystemParameters.VirtualScreenHeight;

        var parameters = new HwndSourceParameters("ScreenTranslatorOverlay")
        {
            Width = virtualWidth,
            Height = virtualHeight,
            WindowStyle = WS_POPUP | WS_VISIBLE,
            ExtendedWindowStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT
                                | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            UsesPerPixelOpacity = true,
            HwndSourceHook = WndProc,
            PositionX = virtualLeft,
            PositionY = virtualTop,
        };

        _hwndSource = new HwndSource(parameters);

        // 创建渲染层
        _overlay = new TranslationOverlay
        {
            FontSize = _settings.FontSize,
            FontColor = ParseColor(_settings.FontColor, Colors.White),
            StrokeThickness = _settings.StrokeWidth,
            BackgroundAlpha = _settings.BackgroundAlpha,
        };
        _hwndSource.RootVisual = _overlay;

        // 强制置顶
        WindowHelper.ForceTopmost(_hwndSource.Handle);

        // 热键
        _hotKeyManager.Attach(_hwndSource);
        RegisterHotkeys();

        WindowCreated?.Invoke();

        Debug.WriteLine($"[OverlayWindow] 窗口已创建: {virtualWidth}x{virtualHeight} @ ({virtualLeft},{virtualTop})");
    }

    /// <summary>
    /// 显示窗口
    /// </summary>
    internal void Show()
    {
        if (_hwndSource == null) return;
        WindowHelper.ShowWindow(_hwndSource.Handle);
        WindowHelper.ForceTopmost(_hwndSource.Handle);
    }

    /// <summary>
    /// 隐藏窗口
    /// </summary>
    internal void Hide()
    {
        if (_hwndSource == null) return;
        WindowHelper.HideWindow(_hwndSource.Handle);
    }

    /// <summary>
    /// 开始翻译循环
    /// </summary>
    internal void StartTranslation()
    {
        if (_isTranslating) return;

        Logger.Info("正在初始化 OCR 引擎...");

        // 初始化 OCR
        if (!string.IsNullOrEmpty(_settings.ModelPath))
        {
            _ocrEngine.Initialize(_settings.ModelPath);
            Logger.Info("OCR 引擎初始化完成");
        }
        else
        {
            Logger.Warn("模型路径为空，跳过 OCR 初始化");
        }

        // 查找目标窗口
        RefreshTargetWindow();

        if (_targetWindowHandle == IntPtr.Zero)
        {
            Logger.Warn($"未找到目标进程 \"{_settings.TargetProcessName}\" 的窗口，请确认程序已打开");
        }

        // 检查翻译 API
        if (string.IsNullOrWhiteSpace(_settings.BaiduAppId) || string.IsNullOrWhiteSpace(_settings.BaiduSecretKey))
        {
            Logger.Warn("百度翻译 API 未配置，翻译功能将不可用");
        }

        // 启动定时器
        _translationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(_settings.RefreshIntervalMs),
            DispatcherPriority.Background,
            OnTranslationTick,
            Dispatcher.CurrentDispatcher);

        _isTranslating = true;
        _translationTimer.Start();

        TranslationStateChanged?.Invoke(true);
        Logger.Info($"翻译已启动，刷新间隔 {_settings.RefreshIntervalMs}ms，目标进程: {_settings.TargetProcessName ?? "(全屏)"}");
    }

    /// <summary>
    /// 停止翻译循环
    /// </summary>
    internal void StopTranslation()
    {
        if (!_isTranslating) return;

        _translationTimer?.Stop();
        _translationTimer = null;

        _isTranslating = false;
        ClearOverlay();

        TranslationStateChanged?.Invoke(false);
        Debug.WriteLine("[OverlayWindow] 翻译已停止");
    }

    /// <summary>
    /// 开关翻译
    /// </summary>
    internal void ToggleTranslation()
    {
        if (_isTranslating)
            StopTranslation();
        else
            StartTranslation();
    }

    /// <summary>
    /// 定时器触发: 截图 → OCR → 翻译 → 渲染
    /// </summary>
    private async void OnTranslationTick(object? sender, EventArgs e)
    {
        if (_isTickRunning) return;
        _isTickRunning = true;
        _tickCount++;

        try
        {
            // 1. 刷新目标窗口
            if (_targetWindowHandle == IntPtr.Zero)
            {
                RefreshTargetWindow();
                if (_targetWindowHandle == IntPtr.Zero) return;
            }

            // 定期同步覆盖窗口位置（每 30 帧）
            if (_tickCount % 30 == 1 && _targetWindowHandle != NativeMethods.GetDesktopWindow())
            {
                PositionOverlayToTarget();
            }

            // 2. 截图
            var screenshot = _screenCapture.CaptureWindow(_targetWindowHandle);
            if (screenshot == null)
            {
                if (_tickCount == 1) Logger.Warn("截图失败，返回 null");
                return;
            }

            try
            {
                // 3. OCR 识别（后台线程）
                var ocrResults = await Task.Run(() => _ocrEngine.Recognize(screenshot));

                if (_tickCount == 1)
                    Logger.Info($"OCR 识别到 {ocrResults.Length} 条文本");

                if (ocrResults.Length == 0) return;

                // 4. 过滤
                var filtered = _ocrEngine.FilterResults(ocrResults);
                if (filtered.Length == 0) return;

                // 5. 批量翻译（缓存 + 合并请求）
                var translator = _translator; // 捕获当前实例
                var texts = filtered.Select(f => f.Text).ToArray();
                var translations = await translator.TranslateBatchAsync(texts);

                var items = new List<TranslationItem>(filtered.Length);
                for (int i = 0; i < filtered.Length; i++)
                {
                    items.Add(new TranslationItem
                    {
                        Text = translations[i],
                        SourceText = filtered[i].Text,
                        ScreenX = filtered[i].X + _screenOffsetX,
                        ScreenY = filtered[i].Y + _screenOffsetY,
                        SourceWidth = filtered[i].Width,
                        SourceHeight = filtered[i].Height,
                        Confidence = filtered[i].Confidence,
                    });
                }

                // 6. 渲染（UI 线程）
                UpdateOverlay(items.ToArray());

                if (_tickCount <= 3)
                {
                    Logger.Info($"翻译完成: {items.Count} 条, 偏移=({_screenOffsetX},{_screenOffsetY})");
                    if (items.Count > 0)
                    {
                        var first = items[0];
                        Logger.Info($"  示例: \"{first.SourceText}\" -> \"{first.Text}\" @ ({first.ScreenX},{first.ScreenY})");
                    }
                }
            }
            finally
            {
                screenshot.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (_tickCount <= 3)
                Logger.Error("翻译循环异常", ex);
        }
        finally
        {
            _isTickRunning = false;
        }
    }

    /// <summary>
    /// 更新译文渲染
    /// </summary>
    internal void UpdateOverlay(TranslationItem[] items)
    {
        _overlay?.UpdateItems(items);
    }

    /// <summary>
    /// 清除所有译文
    /// </summary>
    internal void ClearOverlay()
    {
        _overlay?.UpdateItems(Array.Empty<TranslationItem>());
    }

    /// <summary>
    /// 刷新目标窗口句柄，并调整覆盖窗口位置
    /// </summary>
    private void RefreshTargetWindow()
    {
        if (!string.IsNullOrEmpty(_settings.TargetProcessName))
        {
            _targetWindowHandle = WindowHelper.FindWindowByProcessName(_settings.TargetProcessName);

            if (_targetWindowHandle == IntPtr.Zero)
            {
                var processes = System.Diagnostics.Process.GetProcessesByName(_settings.TargetProcessName);
                if (processes.Length == 0)
                {
                    Logger.Warn($"未找到进程 \"{_settings.TargetProcessName}\"，请确认程序已启动（进程名不含 .exe）");
                }
                else
                {
                    Logger.Warn($"找到进程 \"{_settings.TargetProcessName}\" 但 MainWindowHandle 为空，尝试使用桌面窗口");
                    _targetWindowHandle = NativeMethods.GetDesktopWindow();
                }
                foreach (var p in processes) p.Dispose();
            }
        }
        else
        {
            _targetWindowHandle = NativeMethods.GetDesktopWindow();
            Logger.Info("目标进程为空，使用全屏模式");
        }

        if (_targetWindowHandle != IntPtr.Zero)
        {
            (_screenOffsetX, _screenOffsetY) = ScreenCapture.GetWindowScreenOffset(_targetWindowHandle);

            // 调整覆盖窗口到目标窗口的位置和大小
            PositionOverlayToTarget();
        }
    }

    /// <summary>
    /// 将覆盖窗口定位到目标窗口上方
    /// </summary>
    private void PositionOverlayToTarget()
    {
        if (_hwndSource == null || _targetWindowHandle == IntPtr.Zero) return;

        if (_targetWindowHandle == NativeMethods.GetDesktopWindow())
            return;

        try
        {
            // 使用客户区坐标（排除边框）
            var clientRect = WindowHelper.GetClientRect(_targetWindowHandle);
            var clientWidth = clientRect.Width;
            var clientHeight = clientRect.Height;
            if (clientWidth <= 100 || clientHeight <= 100) return;

            // 客户区屏幕坐标
            var (clientX, clientY) = ScreenCapture.GetWindowScreenOffset(_targetWindowHandle);

            NativeMethods.SetWindowPos(_hwndSource.Handle, (IntPtr)NativeMethods.HWND_TOPMOST,
                clientX, clientY, clientWidth, clientHeight,
                0x0010); // SWP_NOACTIVATE

            // 覆盖窗口客户区和目标窗口客户区完全对齐，偏移为 0
            // OCR 坐标直接作为渲染坐标，译文覆盖在原文位置上
            _screenOffsetX = 0;
            _screenOffsetY = 0;
        }
        catch (Exception ex)
        {
            Logger.Error("定位覆盖窗口失败", ex);
        }
    }

    /// <summary>
    /// 注册全局热键
    /// </summary>
    private void RegisterHotkeys()
    {
        // Ctrl+Alt+T 开关翻译
        _hotKeyManager.Register(
            ModifierKeys.Control | ModifierKeys.Alt,
            System.Windows.Forms.Keys.T,
            () =>
            {
                ToggleTranslation();
            });
    }

    /// <summary>
    /// WndProc 消息处理
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DESTROY = 0x0002;
        const int WM_DISPLAYCHANGE = 0x007E;

        switch (msg)
        {
            case WM_DESTROY:
                handled = true;
                break;

            case WM_DISPLAYCHANGE:
                // 显示器分辨率变化时重新调整
                WindowHelper.SetFullScreen(hwnd);
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 解析颜色字符串 (#FFFFFF → Colors.White)
    /// </summary>
    private static Color ParseColor(string? hex, Color defaultColor)
    {
        if (string.IsNullOrEmpty(hex)) return defaultColor;
        try
        {
            return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return defaultColor;
        }
    }

    /// <summary>
    /// 销毁窗口
    /// </summary>
    internal void Close()
    {
        if (_disposed) return;
        _disposed = true;

        StopTranslation();
        _hotKeyManager.Dispose();
        _screenCapture.Dispose();
        _ocrEngine.Dispose();
        _translator.Dispose();

        _hwndSource?.Dispose();
        _hwndSource = null;

        WindowDestroyed?.Invoke();
    }

    /// <summary>
    /// 重新创建翻译器（设置变更后调用）
    /// </summary>
    internal void RecreateTranslator()
    {
        var old = _translator;
        _translator = new BaiduTranslator(_settings.BaiduAppId, _settings.BaiduSecretKey);
        // 延迟 dispose 旧实例，避免翻译循环中使用已释放的对象
        _ = Task.Delay(2000).ContinueWith(_ => old.Dispose());
    }

    public void Dispose() => Close();
}
