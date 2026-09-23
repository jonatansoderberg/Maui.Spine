namespace Plugin.Maui.Spine.Common;

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

    /// <summary>The URL the platform fetches a fresh timeline document from; see <see cref="RemoteSource"/>.</summary>
    public Uri? Remote { get; private set; }

    /// <summary>The color the widget is drawn on; the platform's widget background when <see langword="null"/>.</summary>
    public WidgetColor? BackgroundColor { get; private set; }

    /// <summary>The gradient the widget is drawn on, instead of <see cref="BackgroundColor"/>.</summary>
    public WidgetGradient? BackgroundGradient { get; private set; }

    /// <summary>The stored image drawn over the surface; see <see cref="BackgroundImage"/>.</summary>
    public string? BackgroundAsset { get; private set; }

    /// <summary>A timeline with one entry that shows <paramref name="tree"/> in every family.</summary>
    public static WidgetTimeline Single(WidgetNode tree) => new WidgetTimeline().Add(DateTimeOffset.UtcNow, tree);

    /// <summary>A timeline with one entry that shows a different tree per family.</summary>
    public static WidgetTimeline Single(IReadOnlyDictionary<WidgetFamily, WidgetNode> trees) => new WidgetTimeline().Add(DateTimeOffset.UtcNow, trees);

    /// <summary>
    /// A timeline with one entry per calendar day, starting now and turning at local midnight, so the
    /// platform shows each day's tree without waking the app.
    /// </summary>
    /// <param name="days">How many days to build, today included.</param>
    /// <param name="build">Builds the tree for a day.</param>
    /// <param name="zone">The time zone whose midnight turns the page; the device's when <see langword="null"/>.</param>
    public static WidgetTimeline Daily(int days, Func<DateOnly, WidgetNode> build, TimeZoneInfo? zone = null) =>
        new WidgetTimeline().AddDaily(days, build, zone);

    /// <summary>
    /// A timeline with one entry per calendar day with a tree per family, starting now and turning at
    /// local midnight.
    /// </summary>
    public static WidgetTimeline Daily(int days, Func<DateOnly, IReadOnlyDictionary<WidgetFamily, WidgetNode>> build, TimeZoneInfo? zone = null) =>
        new WidgetTimeline().AddDaily(days, build, zone);

    /// <summary>Adds one entry per calendar day, the first dated now and the rest at local midnight.</summary>
    public WidgetTimeline AddDaily(int days, Func<DateOnly, WidgetNode> build, TimeZoneInfo? zone = null)
    {
        foreach (var (date, from) in DaysAhead(days, zone ?? TimeZoneInfo.Local))
            Add(from, build(date));
        return this;
    }

    /// <summary>Adds one entry per calendar day with a tree per family, the first dated now and the rest at local midnight.</summary>
    public WidgetTimeline AddDaily(int days, Func<DateOnly, IReadOnlyDictionary<WidgetFamily, WidgetNode>> build, TimeZoneInfo? zone = null)
    {
        foreach (var (date, from) in DaysAhead(days, zone ?? TimeZoneInfo.Local))
            Add(from, build(date));
        return this;
    }

    /// <summary>
    /// The next <paramref name="days"/> days in <paramref name="zone"/>, each with the moment its entry
    /// begins: now for today, local midnight for the rest.
    /// </summary>
    public static IEnumerable<(DateOnly Date, DateTimeOffset From)> DaysAhead(int days, TimeZoneInfo zone)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

        for (var i = 0; i < days; i++)
        {
            var date = today.AddDays(i);
            yield return (date, i == 0 ? now : Midnight(date, zone));
        }
    }

    private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>Adds an entry that shows <paramref name="tree"/> in every family from <paramref name="date"/>.</summary>
    public WidgetTimeline Add(DateTimeOffset date, WidgetNode tree) => Add(date, tree, null);

    /// <summary>Adds an entry with a different tree per family from <paramref name="date"/>.</summary>
    public WidgetTimeline Add(DateTimeOffset date, IReadOnlyDictionary<WidgetFamily, WidgetNode> trees) => Add(date, trees, null);

    /// <summary>
    /// Adds an entry that shows <paramref name="tree"/> in every family from <paramref name="date"/>, on
    /// <paramref name="surface"/> instead of the timeline's own. The platform switches the surface with the entry.
    /// </summary>
    /// <param name="date">From when the entry is shown.</param>
    /// <param name="tree">The tree for every family.</param>
    /// <param name="surface">The entry's surface, replacing the timeline's color, gradient and image alike; <see langword="null"/> for the timeline's.</param>
    public WidgetTimeline Add(DateTimeOffset date, WidgetNode tree, WidgetSurface? surface)
    {
        _entries.Add(new WidgetTimelineEntry(date, tree, null) { Surface = surface });
        return this;
    }

    /// <summary>
    /// Adds an entry with a different tree per family from <paramref name="date"/>, on <paramref name="surface"/>
    /// instead of the timeline's own. The platform switches the surface with the entry.
    /// </summary>
    /// <param name="date">From when the entry is shown.</param>
    /// <param name="trees">The tree per family.</param>
    /// <param name="surface">The entry's surface, replacing the timeline's color, gradient and image alike; <see langword="null"/> for the timeline's.</param>
    public WidgetTimeline Add(DateTimeOffset date, IReadOnlyDictionary<WidgetFamily, WidgetNode> trees, WidgetSurface? surface)
    {
        if (trees.Count == 0) throw new ArgumentException("At least one family is required.", nameof(trees));
        _entries.Add(new WidgetTimelineEntry(date, null, trees) { Surface = surface });
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

    /// <summary>
    /// Lets the widget fetch its own content while the app is not running: at every reload the platform
    /// GETs <paramref name="url"/>, which must answer with a timeline document (see <see cref="ToJson"/>),
    /// and shows that instead of the entries here, which remain the fallback. <see cref="Refresh"/> sets
    /// the pace; without one the platform is asked every 15 minutes.
    /// </summary>
    public WidgetTimeline RemoteSource(Uri url)
    {
        Remote = url;
        return this;
    }

    /// <summary>
    /// Draws the widget on <paramref name="color"/> instead of the platform's widget background, in every
    /// entry without a <see cref="WidgetSurface"/> of its own; replaces a gradient. A fixed color stays fixed
    /// in dark mode, so give the text fixed colors too.
    /// </summary>
    public WidgetTimeline Background(WidgetColor color)
    {
        BackgroundColor = color;
        BackgroundGradient = null;
        return this;
    }

    /// <summary>
    /// Draws the widget on <paramref name="gradient"/> instead of the platform's widget background, in every
    /// entry without a <see cref="WidgetSurface"/> of its own; replaces a color. Android resolves semantic colors in it once, in the app's theme, rather than
    /// following the launcher's light and dark.
    /// </summary>
    public WidgetTimeline Background(WidgetGradient gradient)
    {
        ArgumentNullException.ThrowIfNull(gradient);
        BackgroundGradient = gradient;
        BackgroundColor = null;
        return this;
    }

    /// <summary>
    /// Draws the image stored as <paramref name="assetId"/> with <see cref="IWidgetService.StoreAssetAsync"/>
    /// over the surface, scaled to fill it and cropped at the edges, in every entry without a
    /// <see cref="WidgetSurface"/> of its own. The color or gradient shows through a transparent image, and in
    /// its place until one is stored. Android scales it to at most 1024 pixels on its long side.
    /// </summary>
    public WidgetTimeline BackgroundImage(string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        BackgroundAsset = assetId;
        return this;
    }

    /// <summary>
    /// The timeline as the renderer's JSON document — what a server answers with for a timeline that has a
    /// <see cref="RemoteSource"/>, so a backend can build it with <see cref="W"/> instead of by hand.
    /// </summary>
    public string ToJson() => Serialization.WidgetJson.Serialize(this);
}

/// <summary>One dated entry of a <see cref="WidgetTimeline"/>.</summary>
/// <param name="Date">From when the entry is shown.</param>
/// <param name="Tree">The tree for every family, when the entry is not family-specific.</param>
/// <param name="Trees">The tree per family, when the entry is family-specific.</param>
public sealed record WidgetTimelineEntry(DateTimeOffset Date, WidgetNode? Tree, IReadOnlyDictionary<WidgetFamily, WidgetNode>? Trees)
{
    /// <summary>The entry's own surface, replacing the timeline's; <see langword="null"/> when it is drawn on the timeline's.</summary>
    public WidgetSurface? Surface { get; init; }
}
