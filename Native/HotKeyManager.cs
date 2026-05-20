using System.Windows.Interop;

namespace ScreenTranslator.Native;

/// <summary>
/// 全局热键管理器 — 基于 HwndSource 的 WndProc Hook
/// </summary>
internal sealed class HotKeyManager : IDisposable
{
    private const int BaseHotKeyId = 9000;

    private HwndSource? _hwndSource;
    private readonly Dictionary<int, HotKeyRegistration> _registrations = new();
    private bool _disposed;

    /// <summary>
    /// 挂载到 HwndSource
    /// </summary>
    internal void Attach(HwndSource hwndSource)
    {
        Detach();
        _hwndSource = hwndSource;
        _hwndSource.AddHook(WndProc);
    }

    /// <summary>
    /// 卸载
    /// </summary>
    internal void Detach()
    {
        UnregisterAll();
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    /// <summary>
    /// 注册全局热键（回调模式）
    /// </summary>
    internal bool Register(ModifierKeys modifiers, System.Windows.Forms.Keys key, Action? callback = null)
    {
        if (_disposed || _hwndSource == null) return false;

        var hwnd = _hwndSource.Handle;
        if (hwnd == IntPtr.Zero) return false;

        var id = BaseHotKeyId + _registrations.Count;
        var fsModifiers = ConvertModifiers(modifiers);

        if (NativeMethods.RegisterHotKey(hwnd, id, fsModifiers, (uint)key))
        {
            var reg = new HotKeyRegistration
            {
                Id = id,
                Modifiers = modifiers,
                Key = key,
                Callback = callback,
            };
            _registrations[id] = reg;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 注销指定热键
    /// </summary>
    internal void Unregister(int id)
    {
        if (_disposed || _hwndSource == null) return;

        var hwnd = _hwndSource.Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(hwnd, id);
        }
        _registrations.Remove(id);
    }

    /// <summary>
    /// 注销所有热键
    /// </summary>
    internal void UnregisterAll()
    {
        if (_hwndSource == null) return;

        var hwnd = _hwndSource.Handle;
        if (hwnd != IntPtr.Zero)
        {
            foreach (var id in _registrations.Keys)
            {
                NativeMethods.UnregisterHotKey(hwnd, id);
            }
        }
        _registrations.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_registrations.TryGetValue(id, out var reg))
            {
                reg.Callback?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static uint ConvertModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            result |= NativeMethods.MOD_ALT;
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            result |= NativeMethods.MOD_CONTROL;
        if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            result |= 0x0004;
        if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
            result |= 0x0008;

        result |= NativeMethods.MOD_NOREPEAT;
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Detach();
    }

    /// <summary>
    /// 热键注册信息
    /// </summary>
    private sealed class HotKeyRegistration
    {
        internal int Id { get; init; }
        internal ModifierKeys Modifiers { get; init; }
        internal System.Windows.Forms.Keys Key { get; init; }
        internal Action? Callback { get; init; }
    }
}

/// <summary>
/// 修饰键枚举
/// </summary>
[Flags]
internal enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}
