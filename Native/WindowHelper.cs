using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ScreenTranslator.Native;

/// <summary>
/// 窗口管理工具类 — 置顶/层叠窗口样式/鼠标穿透/窗口查找
/// </summary>
internal static class WindowHelper
{
    private static readonly ConcurrentDictionary<string, IntPtr> s_windowCache = new();

    // ===== 窗口样式常量 =====
    private const int GWL_EXSTYLE = -20;
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    /// <summary>
    /// 设置窗口为工具窗口样式（不在任务栏显示、Alt+Tab 不可见）
    /// </summary>
    internal static void SetToolWindow(IntPtr hwnd)
    {
        var exStyle = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    /// <summary>
    /// 启用/禁用鼠标穿透
    /// </summary>
    internal static void SetMousePassthrough(IntPtr hwnd, bool enabled)
    {
        var exStyle = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
        if (enabled)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED;
        }
        else
        {
            exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
        }
        NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
    }

    /// <summary>
    /// 强制窗口置顶
    /// </summary>
    internal static void ForceTopmost(IntPtr hwnd)
    {
        NativeMethods.SetWindowPos(hwnd, (IntPtr)NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// 设置窗口覆盖全屏（所有显示器）
    /// </summary>
    internal static void SetFullScreen(IntPtr hwnd)
    {
        var screenWidth = (int)SystemParameters.VirtualScreenWidth;
        var screenHeight = (int)SystemParameters.VirtualScreenHeight;

        NativeMethods.SetWindowPos(hwnd, (IntPtr)NativeMethods.HWND_TOPMOST,
            (int)SystemParameters.VirtualScreenLeft,
            (int)SystemParameters.VirtualScreenTop,
            screenWidth,
            screenHeight,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// 显示窗口
    /// </summary>
    internal static void ShowWindow(IntPtr hwnd)
    {
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        ForceTopmost(hwnd);
    }

    /// <summary>
    /// 隐藏窗口
    /// </summary>
    internal static void HideWindow(IntPtr hwnd)
    {
        // 使用 ShowWindow 隐藏
        NativeMethods.ShowWindow(hwnd, SW_HIDE);
    }

    /// <summary>
    /// 根据进程名查找主窗口句柄
    /// </summary>
    internal static IntPtr FindWindowByProcessName(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return IntPtr.Zero;

        // 检查缓存
        if (s_windowCache.TryGetValue(processName, out var cachedHwnd))
        {
            if (IsWindowStillValid(cachedHwnd, processName))
                return cachedHwnd;
            s_windowCache.TryRemove(processName, out _);
        }

        var foundHwnd = IntPtr.Zero;
        var processes = Process.GetProcessesByName(processName);

        foreach (var process in processes)
        {
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                foundHwnd = process.MainWindowHandle;
                s_windowCache[processName] = foundHwnd;
                process.Dispose();
                break;
            }
            process.Dispose();
        }

        return foundHwnd;
    }

    /// <summary>
    /// 查找 Unity 编辑器窗口（支持多种窗口类名）
    /// </summary>
    internal static IntPtr FindUnityEditorWindow()
    {
        var hwnd = NativeMethods.FindWindow("UnityContainerWndClass", null);
        if (hwnd != IntPtr.Zero) return hwnd;

        hwnd = NativeMethods.FindWindow("UnityWindowClass", null);
        if (hwnd != IntPtr.Zero) return hwnd;

        hwnd = FindWindowByProcessName("Unity");
        if (hwnd != IntPtr.Zero) return hwnd;

        return FindWindowByProcessName("Unity Hub");
    }

    /// <summary>
    /// 获取窗口在屏幕上的位置和尺寸
    /// </summary>
    internal static NativeMethods.RECT GetWindowRect(IntPtr hwnd)
    {
        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    /// <summary>
    /// 获取窗口客户区尺寸
    /// </summary>
    internal static NativeMethods.RECT GetClientRect(IntPtr hwnd)
    {
        NativeMethods.GetClientRect(hwnd, out var rect);
        return rect;
    }

    /// <summary>
    /// 检查窗口句柄是否仍有效且属于指定进程
    /// </summary>
    private static bool IsWindowStillValid(IntPtr hwnd, string processName)
    {
        if (hwnd == IntPtr.Zero) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return false;

        try
        {
            var process = Process.GetProcessById((int)pid);
            var valid = process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)
                        && !process.HasExited;
            process.Dispose();
            return valid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 清除窗口缓存
    /// </summary>
    internal static void ClearCache()
    {
        s_windowCache.Clear();
    }

    /// <summary>
    /// 获取窗口 DPI 缩放比例
    /// </summary>
    internal static double GetWindowDpi(IntPtr hwnd)
    {
        return NativeMethods.GetDpiForWindow(hwnd) / 96.0;
    }
}
