using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class OverlayWindow : Window
{
    public bool IsExpanded { get; private set; }

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyMiniLayout();
            OverlayMap.ResetView();
        };
        SizeChanged += (_, e) =>
        {
            if (e.PreviousSize.Width > 0 &&
                (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 8 ||
                 Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 8))
                OverlayMap.ResetView();
        };
    }

    public void SetGlassOpacity(double opacity)
    {
        Opacity = Math.Clamp(opacity, 0.45, 1.0);
        GlassBrush.Color = Color.FromArgb(255, 26, 26, 26);
    }

    public void LoadMap(MapDefinition map, IReadOnlyList<ExtractMarker> extracts, IReadOnlyList<HazardMarker>? mines = null, bool? showLabels = null)
    {
        OverlayTitle.Text = map.Name.ToUpperInvariant();
        OverlayMap.LoadMap(map, extracts, mines, showLabels);
        OverlayMap.SetFollow(App.Settings.FollowPlayer);
        OverlayMap.ResetView();
        ExtractList.Children.Clear();
        foreach (var ex in extracts)
        {
            ExtractList.Children.Add(new TextBlock
            {
                Text = Loc.T("Overlay.ExtractPrefix", ex.Name.ToUpperInvariant()),
                Foreground = (Brush)FindResource("BrushText"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }
        foreach (var mine in mines ?? [])
        {
            ExtractList.Children.Add(new TextBlock
            {
                Text = Loc.T("Overlay.MinePrefix", mine.Name.ToUpperInvariant()),
                Foreground = (Brush)FindResource("BrushTextDim"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }
    }

    public void ApplyLanguage()
    {
        OverlayMap.ApplyLanguage();
    }

    public void SetPlayer(PlayerFix? fix) => OverlayMap.SetPlayer(fix);

    public void SetFollow(bool follow) => OverlayMap.SetFollow(follow);

    public void SetMarkers(IReadOnlyList<ExtractMarker> extracts, IReadOnlyList<HazardMarker>? mines = null, bool? showLabels = null) =>
        OverlayMap.SetMarkers(extracts, mines, showLabels);

    public void ToggleExpanded()
    {
        if (IsExpanded) ApplyMiniLayout();
        else ApplyExpandedLayout();
    }

    private bool _placedOnce;

    public void ApplyMiniLayout()
    {
        IsExpanded = false;
        Width = 320;
        Height = 348;
        ExtractCol.Width = new GridLength(0);
        ExtractPanel.Visibility = Visibility.Collapsed;
        if (!_placedOnce)
        {
            PlaceBottomRight(16);
            _placedOnce = true;
        }
        NativeMethods.SetClickThrough(this, false);
    }

    public void ApplyExpandedLayout()
    {
        IsExpanded = true;
        Width = 820;
        Height = 560;
        ExtractCol.Width = new GridLength(200);
        ExtractPanel.Visibility = Visibility.Visible;
        NativeMethods.SetClickThrough(this, false);
    }

    private void Header_Drag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left ||
            e.ChangedButton == System.Windows.Input.MouseButton.Right)
        {
            try { DragMove(); }
            catch { /* ignore if button already released */ }
        }
    }

    private void PlaceBottomRight(double margin)
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - margin;
        Top = wa.Bottom - Height - margin;
    }
}
