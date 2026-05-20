using Hardcodet.Wpf.TaskbarNotification;
using ScreenTranslator.Core;
using System.Windows;
using System.Windows.Threading;

namespace ScreenTranslator;

/// <summary>
/// 应用入口 — 管理 OverlayWindow 和系统托盘图标
/// </summary>
public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlayWindow;
    private TaskbarIcon? _trayIcon;
    private bool _isShuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Logger.Clear();
        Logger.Info("=== ScreenTranslator 启动 ===");

        // 全局异常处理
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            // 创建覆盖窗口
            _overlayWindow = new OverlayWindow();
            _overlayWindow.Create();
            _overlayWindow.TranslationStateChanged += OnTranslationStateChanged;
            Logger.Info("覆盖窗口已创建");
        }
        catch (Exception ex)
        {
            Logger.Error("创建覆盖窗口失败", ex);
        }

        // 创建托盘图标
        CreateTrayIcon();

        // 检查 API 配置
        if (string.IsNullOrWhiteSpace(_overlayWindow?.Settings.BaiduAppId))
        {
            Logger.Warn("百度翻译 API 未配置，请在设置中填写 App ID 和 Secret Key");
            _trayIcon?.ShowBalloonTip("ScreenTranslator", "请先在设置中配置百度翻译 API", BalloonIcon.Info);
        }

        // 是否自动开始翻译
        if (_overlayWindow?.Settings.AutoStartTranslation == true)
        {
            StartTranslationWithFeedback();
        }

        Logger.Info($"应用启动完成，日志文件: {Logger.GetLogFilePath()}");
    }

    /// <summary>
    /// 创建系统托盘图标
    /// </summary>
    private void CreateTrayIcon()
    {
        // 尝试加载自定义图标
        System.Drawing.Icon trayIcon;
        try
        {
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icons", "app.ico");
            if (System.IO.File.Exists(iconPath))
                trayIcon = new System.Drawing.Icon(iconPath);
            else
                trayIcon = System.Drawing.SystemIcons.Application;
        }
        catch
        {
            trayIcon = System.Drawing.SystemIcons.Application;
        }

        _trayIcon = new TaskbarIcon
        {
            Icon = trayIcon,
            ToolTipText = "ScreenTranslator - 悬浮翻译 (Ctrl+Alt+T)",
            Visibility = Visibility.Visible,
        };

        // 右键菜单
        var contextMenu = new System.Windows.Controls.ContextMenu();

        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = "开启/暂停翻译 (Ctrl+Alt+T)",
        };
        toggleItem.Click += (_, _) => StartTranslationWithFeedback();
        contextMenu.Items.Add(toggleItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var openLogItem = new System.Windows.Controls.MenuItem
        {
            Header = "查看日志",
        };
        openLogItem.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("notepad.exe", Logger.GetLogFilePath()); }
            catch { }
        };
        contextMenu.Items.Add(openLogItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var settingsItem = new System.Windows.Controls.MenuItem
        {
            Header = "设置",
        };
        settingsItem.Click += (_, _) => OpenSettings();
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem
        {
            Header = "退出",
        };
        exitItem.Click += (_, _) => ShutdownApplication();
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;

        // 双击切换
        _trayIcon.TrayMouseDoubleClick += (_, _) => StartTranslationWithFeedback();
    }

    /// <summary>
    /// 带反馈的翻译切换
    /// </summary>
    private void StartTranslationWithFeedback()
    {
        if (_overlayWindow == null) return;

        try
        {
            _overlayWindow.ToggleTranslation();

            if (_overlayWindow.IsTranslating)
            {
                Logger.Info("翻译已启动");
                _trayIcon?.ShowBalloonTip("ScreenTranslator", "翻译已开启", BalloonIcon.Info);
            }
            else
            {
                Logger.Info("翻译已暂停");
                _trayIcon?.ShowBalloonTip("ScreenTranslator", "翻译已暂停", BalloonIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("切换翻译失败", ex);
            _trayIcon?.ShowBalloonTip("ScreenTranslator", $"启动失败: {ex.Message}", BalloonIcon.Error);
        }
    }

    /// <summary>
    /// 翻译状态变化时更新托盘图标提示
    /// </summary>
    private void OnTranslationStateChanged(bool isTranslating)
    {
        if (_trayIcon == null) return;

        _trayIcon.ToolTipText = isTranslating
            ? "ScreenTranslator - 翻译中"
            : "ScreenTranslator - 已暂停 (Ctrl+Alt+T 开启)";
    }

    private void OpenSettings()
    {
        if (_overlayWindow == null) return;

        var window = new SettingsWindow(_overlayWindow.Settings);
        window.ShowDialog();

        if (window.DialogResult == true)
        {
            _overlayWindow.RecreateTranslator();
            Logger.Info("设置已保存，翻译器已重建");

            if (string.IsNullOrWhiteSpace(_overlayWindow.Settings.BaiduAppId))
            {
                _trayIcon?.ShowBalloonTip("ScreenTranslator", "请填写百度翻译 API 密钥", BalloonIcon.Warning);
            }
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("UI 线程异常", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Logger.Error("严重异常", ex);
    }

    private void ShutdownApplication()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        try
        {
            Logger.Info("应用退出中...");
            _overlayWindow?.Settings.Save();
            _overlayWindow?.StopTranslation();
            _overlayWindow?.Close();
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
        catch (Exception ex)
        {
            Logger.Error("退出异常", ex);
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_isShuttingDown)
        {
            _overlayWindow?.Settings.Save();
            _overlayWindow?.Close();
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
        base.OnExit(e);
    }
}
