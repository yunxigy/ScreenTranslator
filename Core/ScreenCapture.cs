using ScreenTranslator.Native;
using System.Drawing;
using System.Windows;

namespace ScreenTranslator.Core;

/// <summary>
/// 屏幕/窗口截图工具 — 优先使用 PrintWindow（支持 GPU 加速窗口）
/// </summary>
internal sealed class ScreenCapture : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// 捕获指定窗口的客户区内容
    /// </summary>
    internal Bitmap? CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;

        var rect = WindowHelper.GetClientRect(hwnd);
        if (rect.Width <= 0 || rect.Height <= 0) return null;

        // 方法1: BitBlt（速度快，兼容性好）
        var bmp = CaptureWithBitBlt(hwnd, rect.Width, rect.Height);
        if (bmp != null && !IsBlackImage(bmp)) return bmp;

        bmp?.Dispose();

        // 方法2: PrintWindow（支持硬件加速窗口）
        bmp = CaptureWithPrintWindow(hwnd, rect.Width, rect.Height);
        if (bmp != null && !IsBlackImage(bmp)) return bmp;

        bmp?.Dispose();
        return CaptureWithBitBlt(hwnd, rect.Width, rect.Height);
    }

    /// <summary>
    /// 快速检查图像是否全黑（采样 4 个角 + 中心）
    /// </summary>
    private static bool IsBlackImage(Bitmap bmp)
    {
        try
        {
            var w = bmp.Width;
            var h = bmp.Height;
            if (w < 10 || h < 10) return true;

            int[][] points = [
                [2, 2], [w - 3, 2], [2, h - 3], [w - 3, h - 3], [w / 2, h / 2]
            ];
            int nonBlack = 0;
            foreach (var p in points)
            {
                var c = bmp.GetPixel(p[0], p[1]);
                if (c.R > 5 || c.G > 5 || c.B > 5) nonBlack++;
            }
            return nonBlack < 2;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 使用 PrintWindow 截图（支持硬件加速窗口）
    /// </summary>
    private static Bitmap? CaptureWithPrintWindow(IntPtr hwnd, int width, int height)
    {
        try
        {
            var hdcWindow = NativeMethods.GetWindowDC(hwnd);
            if (hdcWindow == IntPtr.Zero) return null;

            var hdcMem = NativeMethods.CreateCompatibleDC(hdcWindow);
            var hBitmap = NativeMethods.CreateCompatibleBitmap(hdcWindow, width, height);
            var oldBitmap = NativeMethods.SelectObject(hdcMem, hBitmap);

            try
            {
                // PW_RENDERFULLCONTENT = 0x00000002，支持 DWM 合成
                bool success = NativeMethods.PrintWindow(hwnd, hdcMem, 0x00000002);
                if (!success)
                    success = NativeMethods.PrintWindow(hwnd, hdcMem, 0);

                if (success)
                    return Bitmap.FromHbitmap(hBitmap);

                return null;
            }
            finally
            {
                NativeMethods.SelectObject(hdcMem, oldBitmap);
                NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(hdcMem);
                NativeMethods.ReleaseDC(hwnd, hdcWindow);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 使用 BitBlt 截图（回退方案）
    /// </summary>
    private static Bitmap? CaptureWithBitBlt(IntPtr hwnd, int width, int height)
    {
        try
        {
            var hdcWindow = NativeMethods.GetDC(hwnd);
            var hdcMem = NativeMethods.CreateCompatibleDC(hdcWindow);
            var hBitmap = NativeMethods.CreateCompatibleBitmap(hdcWindow, width, height);

            try
            {
                NativeMethods.SelectObject(hdcMem, hBitmap);
                NativeMethods.BitBlt(hdcMem, 0, 0, width, height,
                    hdcWindow, 0, 0, NativeMethods.SRCCOPY);

                return Bitmap.FromHbitmap(hBitmap);
            }
            finally
            {
                NativeMethods.DeleteObject(hBitmap);
                NativeMethods.DeleteDC(hdcMem);
                NativeMethods.ReleaseDC(hwnd, hdcWindow);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 捕获全屏
    /// </summary>
    internal Bitmap CaptureFullScreen()
    {
        var screenWidth = (int)SystemParameters.VirtualScreenWidth;
        var screenHeight = (int)SystemParameters.VirtualScreenHeight;

        return CaptureRegion(
            (int)SystemParameters.VirtualScreenLeft,
            (int)SystemParameters.VirtualScreenTop,
            screenWidth,
            screenHeight);
    }

    /// <summary>
    /// 捕获屏幕指定区域
    /// </summary>
    internal Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and height must be positive.");

        var screenDC = NativeMethods.GetDC(NativeMethods.GetDesktopWindow());
        var memDC = NativeMethods.CreateCompatibleDC(screenDC);
        var hBitmap = NativeMethods.CreateCompatibleBitmap(screenDC, width, height);

        try
        {
            NativeMethods.SelectObject(memDC, hBitmap);
            NativeMethods.BitBlt(memDC, 0, 0, width, height,
                screenDC, x, y, NativeMethods.SRCCOPY);

            return Bitmap.FromHbitmap(hBitmap);
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(memDC);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDC);
        }
    }

    /// <summary>
    /// 获取目标窗口客户区相对于屏幕的位置偏移
    /// </summary>
    internal static (int offsetX, int offsetY) GetWindowScreenOffset(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return (0, 0);

        var clientPoint = new NativeMethods.POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(hwnd, ref clientPoint);

        return (clientPoint.X, clientPoint.Y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
