namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Highlights from code, and the keys that make a hint play once per install. Registered by
/// <c>UseHighlights()</c>, which <c>UseSpine()</c> calls for an app that references the package.
/// </summary>
/// <example>
/// <code>
/// await highlights.TourAsync("onboarding", (addButton, "Add an event"), new TourStep(FilterAction, "Filter by calendar"));
/// await highlights.PlayAsync(saveButton, HighlightKind.Pulse, HighlightRepeat.Times(2));
/// highlights.Reset(); // "Show tips again"
/// </code>
/// </example>
public interface IHighlights
{
    /// <summary>
    /// Spotlights each step in turn over the whole window: the rest is dimmed and a card shows the tip with
    /// Skip and Next. A tap anywhere goes to the next step. A step whose target cannot be shown is left out
    /// and reported in <see cref="TourResult.Missing"/>. Completing or skipping the tour marks
    /// <paramref name="key"/> seen; it returns <see cref="TourOutcome.AlreadySeen"/> at once after that.
    /// One tour runs at a time; a second waits for the first.
    /// </summary>
    Task<TourResult> TourAsync(string key, params IEnumerable<TourStep> steps);

    /// <summary>
    /// Plays a highlight on <paramref name="view"/> and completes when it ends by itself or by a tap. A
    /// <see cref="HighlightRepeat.Forever"/> run ends only when <paramref name="cancellationToken"/> is
    /// cancelled. The kind is cleared from the view afterwards. Call it on the main thread.
    /// </summary>
    Task PlayAsync(View view, HighlightKind kind, HighlightRepeat repeat = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts an Edge glow round the whole window, above the header bar, the tab bar and sheets, taking no
    /// touches, until the returned glow is disposed. Set its <see cref="IEdgeGlow.Intensity"/> to drive it from
    /// a level, such as a voice session's output. The rim follows the display's rounded corners. Call it on the
    /// main thread once the window exists.
    /// </summary>
    IEdgeGlow AttachEdge(Window window);

    /// <summary>Whether a highlight with <see cref="Highlight.KeyProperty"/> <paramref name="key"/> has been seen.</summary>
    bool HasSeen(string key);

    /// <summary>Counts <paramref name="key"/> as seen, so highlights with it no longer play.</summary>
    void MarkSeen(string key);

    /// <summary>
    /// Forgets one key, or every highlight key when <paramref name="key"/> is <see langword="null"/>
    /// ("Show tips again"). Highlights on screen with a forgotten key play again.
    /// </summary>
    void Reset(string? key = null);
}
