namespace MauiSpinePushSampleApp.Services;

/// <summary>
/// What the sample's widget shows, as the last silent push set it.
/// </summary>
/// <remarks>
/// Spine hands a widget push straight to <c>IWidgetService</c> — it means "rebuild", and carries no
/// content of its own. That is the right shape: a widget renders from what the app knows, not from
/// whatever the last payload happened to contain, since pushes can be dropped, coalesced or arrive
/// out of order. So the content travels as a silent push, the handler stores it here, and the
/// provider reads it on every build.
/// <para>
/// Kept in <see cref="Preferences"/> rather than in memory: a widget is rebuilt long after the
/// launch that stored anything, often in a process that has since been replaced.
/// </para>
/// </remarks>
public sealed class WidgetContent
{
    private const string TitleKey = "sample.widget.title";
    private const string BodyKey = "sample.widget.body";
    private const string SetAtKey = "sample.widget.set-at";

    /// <summary>The keys the Send page puts in a silent push's data bag.</summary>
    public const string TitleData = "widget.title";

    /// <inheritdoc cref="TitleData" />
    public const string BodyData = "widget.body";

    /// <summary>The last pushed title, or <see langword="null"/> when nothing has been pushed.</summary>
    public string? Title => Read(TitleKey);

    /// <summary>The last pushed body.</summary>
    public string? Body => Read(BodyKey);

    /// <summary>When it arrived, so the widget can show that it is recent.</summary>
    public DateTimeOffset? SetAt =>
        Preferences.Default.Get(SetAtKey, 0L) is > 0 and var unix
            ? DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime()
            : null;

    /// <summary>Replaces what the widget shows.</summary>
    /// <param name="title">The first line.</param>
    /// <param name="body">The second.</param>
    /// <param name="at">When the message arrived.</param>
    public void Set(string? title, string? body, DateTimeOffset at)
    {
        Preferences.Default.Set(TitleKey, title ?? "");
        Preferences.Default.Set(BodyKey, body ?? "");
        Preferences.Default.Set(SetAtKey, at.ToUnixTimeSeconds());
    }

    private static string? Read(string key) =>
        Preferences.Default.Get(key, "") is { Length: > 0 } value ? value : null;
}
