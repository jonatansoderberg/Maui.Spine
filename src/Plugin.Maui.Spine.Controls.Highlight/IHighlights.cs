namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Highlights from code, and the keys that make a hint play once per install. Registered by
/// <c>UseHighlights()</c>, which <c>UseSpine()</c> calls for an app that references the package.
/// </summary>
/// <example>
/// <code>
/// await highlights.PlayAsync(saveButton, HighlightKind.Pulse, HighlightRepeat.Times(2));
/// highlights.Reset(); // "Show tips again"
/// </code>
/// </example>
public interface IHighlights
{
    /// <summary>
    /// Plays a highlight on <paramref name="view"/> and completes when it ends by itself or by a tap. A
    /// <see cref="HighlightRepeat.Forever"/> run ends only when <paramref name="cancellationToken"/> is
    /// cancelled. The kind is cleared from the view afterwards. Call it on the main thread.
    /// </summary>
    Task PlayAsync(View view, HighlightKind kind, HighlightRepeat repeat = default, CancellationToken cancellationToken = default);

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
