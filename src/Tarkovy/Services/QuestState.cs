using Tarkovy.Models;

namespace Tarkovy.Services;

public static class QuestState
{
    public static bool IsCompleted(string slug) =>
        App.Settings.CompletedQuestSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase);

    public static bool IsTracking(string slug) =>
        !IsCompleted(slug) &&
        App.Settings.EnabledQuestSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<string> TrackingSlugs() =>
        App.Settings.EnabledQuestSlugs.Where(s => !IsCompleted(s));

    public static void SetCompleted(string slug, bool completed)
    {
        var completedList = App.Settings.CompletedQuestSlugs;
        var enabledList = App.Settings.EnabledQuestSlugs;

        if (completed)
        {
            if (!completedList.Contains(slug, StringComparer.OrdinalIgnoreCase))
                completedList.Add(slug);
            enabledList.RemoveAll(s => string.Equals(s, slug, StringComparison.OrdinalIgnoreCase));

            var wp = App.Settings.ActiveWaypoint;
            if (wp?.Kind == "quest" &&
                (string.Equals(wp.Id, slug, StringComparison.OrdinalIgnoreCase) ||
                 wp.Id.StartsWith(slug + "-", StringComparison.OrdinalIgnoreCase)))
                App.Settings.ActiveWaypoint = null;
        }
        else
        {
            completedList.RemoveAll(s => string.Equals(s, slug, StringComparison.OrdinalIgnoreCase));
        }

        SettingsStore.Save(App.Settings);
    }

    public static void SetTracking(string slug, bool tracking)
    {
        if (IsCompleted(slug)) return;

        var list = App.Settings.EnabledQuestSlugs;
        if (tracking)
        {
            if (!list.Contains(slug, StringComparer.OrdinalIgnoreCase))
                list.Add(slug);
        }
        else
        {
            list.RemoveAll(s => string.Equals(s, slug, StringComparison.OrdinalIgnoreCase));
        }

        SettingsStore.Save(App.Settings);
    }

    /// <summary>Removes completed slugs accidentally left in the tracking list.</summary>
    public static void SanitizeTrackingList()
    {
        var completed = new HashSet<string>(App.Settings.CompletedQuestSlugs, StringComparer.OrdinalIgnoreCase);
        if (completed.Count == 0) return;

        var before = App.Settings.EnabledQuestSlugs.Count;
        App.Settings.EnabledQuestSlugs.RemoveAll(s => completed.Contains(s));
        if (App.Settings.EnabledQuestSlugs.Count != before)
            SettingsStore.Save(App.Settings);
    }
}
