using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Tarkovy.Services;

/// <summary>
/// Detects left-clicks outside this process without a low-level mouse hook
/// (WH_MOUSE_LL stalls every mouse move system-wide).
/// </summary>
public sealed class ItemScanClickWatcher : IDisposable
{
    private const int VkLButton = 0x01;
    private const int VkShift = 0x10;

    private readonly DispatcherTimer _timer;
    private readonly int _ownProcessId = Process.GetCurrentProcess().Id;
    private bool _leftWasDown;
    private bool _running;

    public event Action<int, int, bool>? ClickDetected;

    public ItemScanClickWatcher(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _timer.Tick += (_, _) => OnTick();
    }

    public void Start()
    {
        if (_running) return;
        _leftWasDown = false;
        _running = true;
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
        _leftWasDown = false;
    }

    private void OnTick()
    {
        if (!_running) return;

        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            GetWindowThreadProcessId(fg, out var fgPid);
            if ((int)fgPid == _ownProcessId)
            {
                _leftWasDown = (GetAsyncKeyState(VkLButton) & 0x8000) != 0;
                return;
            }
        }

        var down = (GetAsyncKeyState(VkLButton) & 0x8000) != 0;
        if (down && !_leftWasDown && GetCursorPos(out var pt))
        {
            var shift = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
            ClickDetected?.Invoke(pt.X, pt.Y, shift);
        }

        _leftWasDown = down;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public void Dispose()
    {
        Stop();
    }
}
