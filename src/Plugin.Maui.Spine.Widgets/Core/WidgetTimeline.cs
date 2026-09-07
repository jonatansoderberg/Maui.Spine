namespace Plugin.Maui.Spine.Widgets;

/// <summary>
/// What a widget shows over time: one or more dated entries, each carrying a tree per family. The
/// platform switches entries at their dates without the app running, so a provider can pre-compute
/// "starts in 40 min", "started" and "finished" in one go.
/// </summary>
public sealed class WidgetTimeline
{
    private readonly List<WidgetTimelineEntry> _entries = [];

    /// <summary>The entries in chronological order.</summary>
    public IReadOnlyList<WidgetTimelineEntry> Entries => _entries;

    /// <summary>
    /// How long after the last entry the platform should ask the provider again. The platform
    /// treats it as a request and may wait longer; leave <see langword="null"/> to be asked
    /// when the timeline ends.
    /// </summary>
    public TimeSpan? RefreshAfter { get; private set; }

    /// <summary>The URL the app is opened with when the user taps the widget.</summary>
    public Uri? Link { get; private set; }

    /// <summary>A timeline with one entry that shows <paramref name="tree"/> in every family.</summary>
    public static WidgetTimeline Single(WidgetNode tree) => new WidgetTimeline().Add(DateTimeOffset.UtcNow, tree);

    /// <summary>A timeline with one entry that shows a different tree per family.</summary>
    public static WidgetTimeline Single(IReadOnlyDictionary<WidgetFamily, WidgetNode> trees) => new WidgetTimeline().Add(DateTimeOffset.UtcNow, trees);

    /// <summary>Adds an entry that shows <paramref name="tree"/> in every family from <paramref name="date"/>.</summary>
    public WidgetTimeline Add(DateTimeOffset date, WidgetNode tree)
    {
        _entries.Add(new WidgetTimelineEntry(date, tree, null));
        return this;
    }

    /// <summary>Adds an entry with a different tree per family from <paramref name="date"/>.</summary>
    public WidgetTimeline Add(DateTimeOffset date, IReadOnlyDictionary<WidgetFamily, WidgetNode> trees)
    {
        if (trees.Count == 0) throw new ArgumentException("At least one family is required.", nameof(trees));
        _entries.Add(new WidgetTimelineEntry(date, null, trees));
        return this;
    }

    /// <summary>Asks the platform to call the provider again <paramref name="after"/> the last entry.</summary>
    public WidgetTimeline Refresh(TimeSpan after)
    {
        RefreshAfter = after;
        return this;
    }

    /// <summary>Opens the app with <paramref name="url"/> when the widget is tapped.</summary>
    public WidgetTimeline OpenUrl(Uri url)
    {
        Link = url;
        return this;
    }
}

/// <summary>One dated entry of a <see cref="WidgetTimeline"/>.</summary>
/// <param name="Date">From when the entry is shown.</param>
/// <param name="Tree">The tree for every family, when the entry is not family-specific.</param>
/// <param name="Trees">The tree per family, when the entry is family-specific.</param>
public sealed record WidgetTimelineEntry(DateTimeOffset Date, WidgetNode? Tree, IReadOnlyDictionary<WidgetFamily, WidgetNode>? Trees);
