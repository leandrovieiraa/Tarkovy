using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tarkovy.Models;

namespace Tarkovy.Services;

public static class PoiListUi
{
    public static UIElement BuildCatalog(string mapId, string filter, Action onChanged)
    {
        var root = new StackPanel();
        var pois = App.Maps.PoisFor(mapId);
        var counts = pois.GroupBy(p => p.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var present = new HashSet<string>(counts.Keys, StringComparer.OrdinalIgnoreCase);

        var sections = new[]
        {
            (PoiCatalog.CatLoot, Loc.T("Main.Poi.Cat.Loot")),
            (PoiCatalog.CatEnemies, Loc.T("Main.Poi.Cat.Enemies")),
            (PoiCatalog.CatLocations, Loc.T("Main.Poi.Cat.Locations"))
        };

        var any = false;
        foreach (var (cat, title) in sections)
        {
            var types = PoiCatalog.TypesIn(cat)
                .Where(t => present.Contains(t.Id))
                .Where(t => Matches(t, filter))
                .ToArray();
            if (types.Length == 0) continue;
            any = true;

            var header = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
            var allBtn = new Button
            {
                Content = Loc.T("Main.Poi.AllNone"),
                Style = (Style)Application.Current.FindResource("PoiPresetChip"),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Tag = cat
            };
            allBtn.Click += (_, _) =>
            {
                var presentIds = types.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                PoiCatalog.ToggleCategory(cat, presentIds);
                SettingsStore.Save(App.Settings);
                onChanged();
            };
            DockPanel.SetDock(allBtn, Dock.Right);
            header.Children.Add(allBtn);
            header.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = (Brush)Application.Current.FindResource("BrushAmber"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            root.Children.Add(header);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < (types.Length + 1) / 2; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var i = 0; i < types.Length; i++)
            {
                var def = types[i];
                counts.TryGetValue(def.Id, out var n);
                var box = new CheckBox
                {
                    Style = (Style)Application.Current.FindResource("MarkerGridCheckBox"),
                    Content = $"{PoiCatalog.TypeLabel(def).ToUpperInvariant()}  {n}",
                    IsChecked = PoiCatalog.IsEnabled(def.Id),
                    Tag = def.Id,
                    Margin = new Thickness(0, 0, 4, 2),
                    FontSize = 10,
                    Opacity = PoiCatalog.IsEnabled(def.Id) ? 1 : 0.55
                };
                var captured = def.Id;
                box.Checked += (_, _) => Toggle(captured, true, onChanged);
                box.Unchecked += (_, _) => Toggle(captured, false, onChanged);
                Grid.SetRow(box, i / 2);
                Grid.SetColumn(box, i % 2);
                grid.Children.Add(box);
            }
            root.Children.Add(grid);
        }

        if (!any)
        {
            root.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(filter)
                    ? Loc.T("Main.Poi.Empty")
                    : Loc.T("Main.Poi.Search.Empty"),
                FontSize = 11,
                Foreground = (Brush)Application.Current.FindResource("BrushTextDim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        return root;
    }

    private static void Toggle(string type, bool enabled, Action onChanged)
    {
        PoiCatalog.SetType(type, enabled);
        SettingsStore.Save(App.Settings);
        onChanged();
    }

    private static bool Matches(PoiTypeDef def, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var f = filter.Trim();
        return def.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
               || def.NamePt.Contains(f, StringComparison.OrdinalIgnoreCase)
               || def.Id.Contains(f, StringComparison.OrdinalIgnoreCase);
    }
}
