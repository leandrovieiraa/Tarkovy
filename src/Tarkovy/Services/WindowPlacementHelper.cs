using System.Windows;
using System.Windows.Threading;
using Tarkovy.Models;

namespace Tarkovy.Services;

public static class WindowPlacementHelper
{
    private const double MinVisible = 80;

    public static void Restore(
        Window window,
        WindowPlacement? placement,
        double defaultWidth,
        double defaultHeight,
        double? defaultLeft = null,
        double? defaultTop = null)
    {
        try
        {
            if (placement is not { IsValid: true })
            {
                ApplyDefaults(window, defaultWidth, defaultHeight, defaultLeft, defaultTop);
                return;
            }

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var w = Math.Max(placement.Width!.Value, window.MinWidth);
            var h = Math.Max(placement.Height!.Value, window.MinHeight);
            window.Width = w;
            window.Height = h;
            window.WindowState = WindowState.Normal;
            ClampToScreen(window, w, h, placement.Left!.Value, placement.Top!.Value);

            if (placement.IsMaximized && window.ResizeMode != ResizeMode.NoResize)
                window.WindowState = WindowState.Maximized;
        }
        catch
        {
            ApplyDefaults(window, defaultWidth, defaultHeight, defaultLeft, defaultTop);
        }
    }

    public static void Capture(Window window, WindowPlacement placement)
    {
        if (window.WindowState == WindowState.Minimized) return;

        if (window.WindowState == WindowState.Maximized)
        {
            placement.IsMaximized = true;
            var rb = window.RestoreBounds;
            if (!IsFinite(rb.Width) || rb.Width <= 0 || !IsFinite(rb.Height) || rb.Height <= 0)
                return;
            placement.Left = rb.Left;
            placement.Top = rb.Top;
            placement.Width = rb.Width;
            placement.Height = rb.Height;
            return;
        }

        placement.IsMaximized = false;
        if (!IsFinite(window.Width) || window.Width <= 0 || !IsFinite(window.Height) || window.Height <= 0)
            return;
        placement.Left = window.Left;
        placement.Top = window.Top;
        placement.Width = window.Width;
        placement.Height = window.Height;
    }

    public static void Wire(Window window, WindowPlacement placement, Action persist)
    {
        var wired = false;
        window.Loaded += (_, _) =>
        {
            if (wired) return;
            wired = true;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (window.WindowState == WindowState.Minimized) return;
                Capture(window, placement);
                persist();
            };

            void Schedule()
            {
                if (window.WindowState == WindowState.Minimized) return;
                timer.Stop();
                timer.Start();
            }

            window.Closing += (_, _) =>
            {
                timer.Stop();
                Capture(window, placement);
                persist();
            };
            window.LocationChanged += (_, _) => Schedule();
            window.SizeChanged += (_, _) => Schedule();
            window.StateChanged += (_, _) =>
            {
                if (window.WindowState == WindowState.Minimized) return;
                Capture(window, placement);
                persist();
            };
        };
    }

    public static void EnsureVisible(Window window)
    {
        if (window.WindowState == WindowState.Maximized) return;
        var w = Math.Max(window.ActualWidth > 0 ? window.ActualWidth : window.Width, window.MinWidth);
        var h = Math.Max(window.ActualHeight > 0 ? window.ActualHeight : window.Height, window.MinHeight);
        ClampToScreen(window, w, h, window.Left, window.Top);
    }

    public static bool IsOnScreen(Window window)
    {
        if (window.WindowState == WindowState.Maximized) return true;
        var w = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var h = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        if (w <= 0 || h <= 0) return false;

        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;
        var right = window.Left + w;
        var bottom = window.Top + h;
        return right > vsLeft + MinVisible && bottom > vsTop + MinVisible &&
               window.Left < vsRight - MinVisible && window.Top < vsBottom - MinVisible;
    }

    private static void ApplyDefaults(
        Window window,
        double defaultWidth,
        double defaultHeight,
        double? defaultLeft,
        double? defaultTop)
    {
        window.Width = Math.Max(defaultWidth, window.MinWidth);
        window.Height = Math.Max(defaultHeight, window.MinHeight);
        window.WindowState = WindowState.Normal;
        if (defaultLeft.HasValue && defaultTop.HasValue)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            ClampToScreen(window, window.Width, window.Height, defaultLeft.Value, defaultTop.Value);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private static void ClampToScreen(Window window, double width, double height, double left, double top)
    {
        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
        var vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

        var maxLeft = vsRight - MinVisible;
        var maxTop = vsBottom - MinVisible;
        var minLeft = vsLeft - width + MinVisible;
        var minTop = vsTop - height + MinVisible;

        window.Left = Math.Clamp(left, minLeft, maxLeft);
        window.Top = Math.Clamp(top, minTop, maxTop);
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
