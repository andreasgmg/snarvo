// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Runtime.InteropServices;

namespace Snarvo.Native;

/// <summary>Copies part of the screen and finds the window under a point. Used by step recording.</summary>
internal static class ScreenCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    const uint GA_ROOT = 2, GA_ROOTOWNER = 3;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const int SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Win32.POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, char[] name, int max);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Win32.POINT point, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    public static IntPtr RootWindowAt(Win32.POINT point)
    {
        var hWnd = WindowFromPoint(point);
        return hWnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hWnd, GA_ROOT);
    }

    public static uint ProcessOf(IntPtr hWnd)
    {
        Win32.GetWindowThreadProcessId(hWnd, out var pid);
        return pid;
    }

    static string ClassOf(IntPtr hWnd)
    {
        var buffer = new char[256];
        int n = GetClassName(hWnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, n));
    }

    /// <summary>The visible frame of a window, without the invisible resize borders and shadow.</summary>
    static RECT Bounds(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0 && r.Width > 0)
            return r;
        GetWindowRect(hWnd, out r);
        return r;
    }

    /// <summary>
    /// What a step should show for a click at <paramref name="point"/>: the window that was clicked,
    /// together with its owner (so a drop-down menu is shown with the program it belongs to).
    /// Clicks on the desktop or the taskbar show the whole screen. Always within one monitor.
    /// </summary>
    public static RECT AreaFor(Win32.POINT point, IntPtr root)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), ref info);
        var monitor = info.rcMonitor;
        if (root == IntPtr.Zero || ClassOf(root) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return monitor;

        var r = Bounds(root);
        var owner = GetAncestor(root, GA_ROOTOWNER);
        if (owner != IntPtr.Zero && owner != root)
        {
            var o = Bounds(owner);
            r = new RECT { Left = Math.Min(r.Left, o.Left), Top = Math.Min(r.Top, o.Top), Right = Math.Max(r.Right, o.Right), Bottom = Math.Max(r.Bottom, o.Bottom) };
        }
        r = new RECT
        {
            Left = Math.Max(r.Left, monitor.Left), Top = Math.Max(r.Top, monitor.Top),
            Right = Math.Min(r.Right, monitor.Right), Bottom = Math.Min(r.Bottom, monitor.Bottom),
        };
        // Tiny or odd results (a tooltip, a window mostly off screen): show the screen instead.
        return r.Width < 160 || r.Height < 120 ? monitor : r;
    }

    /// <summary>Copies an area of the screen as top-down BGRA pixels with full alpha.</summary>
    public static byte[] Copy(RECT area)
    {
        int w = area.Width, h = area.Height;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, w, h);
        var old = SelectObject(memory, bitmap);
        try
        {
            // CAPTUREBLT includes layered windows such as menus and tooltips.
            BitBlt(memory, 0, 0, w, h, screen, area.Left, area.Top, SRCCOPY | CAPTUREBLT);
            SelectObject(memory, old);
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
            };
            var pixels = new byte[w * h * 4];
            GetDIBits(memory, bitmap, 0, (uint)h, pixels, ref header, 0);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return pixels;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
