using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tarkovy.Services;

internal static class NativeMethods
{
    public const int GwlExstyle = -20;
    public const int WsExTransparent = 0x00000020;
    public const int WsExToolwindow = 0x00000080;
    public const int WsExNoactivate = 0x08000000;
    public const int WsExLayered = 0x00080000;

    public const int WmHotkey = 0x0312;
    public const int WmGetMinMaxInfo = 0x0024;
    public const int ModNorepeat = 0x4000;
    public const int VkF8 = 0x77;
    public const int VkF9 = 0x78;

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointNative ptReserved;
        public PointNative ptMaxSize;
        public PointNative ptMaxPosition;
        public PointNative ptMinTrackSize;
        public PointNative ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public RectNative rcMonitor;
        public RectNative rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    /// <summary>Borderless windows must respect monitor work area when maximized (taskbar).</summary>
    public static bool TryHandleGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return false;

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return false;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        mmi.ptMaxPosition.X = Math.Abs(info.rcWork.Left - info.rcMonitor.Left);
        mmi.ptMaxPosition.Y = Math.Abs(info.rcWork.Top - info.rcMonitor.Top);
        mmi.ptMaxSize.X = Math.Abs(info.rcWork.Right - info.rcWork.Left);
        mmi.ptMaxSize.Y = Math.Abs(info.rcWork.Bottom - info.rcWork.Top);
        Marshal.StructureToPtr(mmi, lParam, true);
        return true;
    }

    public static void EnableWorkAreaMaximize(Window window)
    {
        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            if (PresentationSource.FromVisual(window) is HwndSource source)
                source.AddHook(WorkAreaHook);
        }

        if (window.IsLoaded)
            OnSourceInitialized(window, EventArgs.Empty);
        else
            window.SourceInitialized += OnSourceInitialized;
    }

    private static IntPtr WorkAreaHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo && TryHandleGetMinMaxInfo(hwnd, lParam))
            handled = true;
        return IntPtr.Zero;
    }

    public static void SetClickThrough(Window window, bool clickThrough)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.EnsureHandle();
        var ex = GetWindowLong(hwnd, GwlExstyle);
        ex |= WsExToolwindow | WsExLayered | WsExNoactivate;
        if (clickThrough)
            ex |= WsExTransparent;
        else
            ex &= ~WsExTransparent;
        _ = SetWindowLong(hwnd, GwlExstyle, ex);
    }
}
