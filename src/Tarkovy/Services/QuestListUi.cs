using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Tarkovy.Models;

namespace Tarkovy.Services;

public static class QuestListUi
{
    public static UIElement BuildRow(QuestDefinition quest, Action onChanged, string? mapTooltip = null)
    {
        var slug = quest.Slug;
        var completed = QuestState.IsCompleted(slug);
        var label = string.IsNullOrWhiteSpace(Loc.QuestTrader(quest))
            ? Loc.QuestName(quest)
            : $"{Loc.QuestName(quest)}  ·  {Loc.QuestTrader(quest)}";

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var completeBtn = new ToggleButton
        {
            Style = (Style)Application.Current.FindResource("QuestCompleteToggle"),
            IsChecked = completed,
            Tag = slug,
            ToolTip = completed ? Loc.T("Quest.Complete.Undo") : Loc.T("Quest.Complete.Mark"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        completeBtn.Checked += (_, _) => OnCompleteToggled(slug, true, onChanged);
        completeBtn.Unchecked += (_, _) => OnCompleteToggled(slug, false, onChanged);
        Grid.SetColumn(completeBtn, 0);
        row.Children.Add(completeBtn);

        var separator = new Rectangle
        {
            Width = 1,
            Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 2),
            IsHitTestVisible = false
        };
        Grid.SetColumn(separator, 1);
        row.Children.Add(separator);

        var trackBox = new CheckBox
        {
            Content = label,
            IsChecked = QuestState.IsTracking(slug),
            IsEnabled = !completed,
            Tag = slug,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Opacity = completed ? 0.45 : 1,
            ToolTip = mapTooltip
        };
        if (completed)
            trackBox.FontStyle = FontStyles.Italic;

        trackBox.Checked += (_, _) => OnTrackToggled(slug, true, onChanged);
        trackBox.Unchecked += (_, _) => OnTrackToggled(slug, false, onChanged);
        Grid.SetColumn(trackBox, 2);
        row.Children.Add(trackBox);

        return row;
    }

    private static void OnCompleteToggled(string slug, bool completed, Action onChanged)
    {
        QuestState.SetCompleted(slug, completed);
        onChanged();
    }

    private static void OnTrackToggled(string slug, bool tracking, Action onChanged)
    {
        QuestState.SetTracking(slug, tracking);
        onChanged();
    }
}
