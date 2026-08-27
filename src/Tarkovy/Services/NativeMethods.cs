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
    public const int ModNorepeat = 0x4000;
    public const int VkF8 = 0x77;
    public const int VkF9 = 0x78;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
