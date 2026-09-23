namespace Plugin.Maui.Spine.Common;

/// <summary>Helpers for naming widget assets.</summary>
public static class WidgetAsset
{
    /// <summary>
    /// An asset id for a dated picture that reuses a fixed set of slots, so a timeline of pictures
    /// (one per day) keeps the asset store at a fixed size: a stored asset cannot be deleted, and with
    /// more slots than days in the timeline a slot is only overwritten once its day has left it.
    /// </summary>
    /// <param name="prefix">Groups the pictures, e.g. the widget kind: <c>"today"</c>.</param>
    /// <param name="date">The day the picture is for.</param>
    /// <param name="slots">How many slots to rotate through; more than the days the timeline covers.</param>
    /// <returns>An id such as <c>today/17.png</c>.</returns>
    public static string Rolling(string prefix, DateOnly date, int slots = 64) =>
        $"{prefix}/{date.DayNumber % slots}.png";
}
