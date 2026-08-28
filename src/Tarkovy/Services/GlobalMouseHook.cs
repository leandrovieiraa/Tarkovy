using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tarkovy.Services;

public sealed class GlobalMouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonDown = 0x0201;
    private const int VkShift = 0x10;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private LowLevelMouseProc? _proc;
    private IntPtr _hook;
    private bool _enabled;
    private int _ownProcessId;

    public event Action<int, int, bool>? MouseScanClick;

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT pt);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _ownProcessId = Process.GetCurrentProcess().Id;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhMouseLl, _proc, GetModuleHandle(null), 0);
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _enabled && wParam == (IntPtr)WmLButtonDown)
        {
            var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if (!IsOwnWindow(data.X, data.Y))
            {
                var shift = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
                MouseScanClick?.Invoke(data.X, data.Y, shift);
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool IsOwnWindow(int x, int y)
    {
        var pt = new POINT { X = x, Y = y };
        var hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid == _ownProcessId;
    }

    public void Dispose()
    {
        Stop();
    }
}
