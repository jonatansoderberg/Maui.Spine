namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A running edge glow round the whole window, from <see cref="IHighlights.AttachEdge"/>: above the header bar,
/// the tab bar and sheets, taking no touches. Dispose it to fade it out.
/// </summary>
/// <example>
/// <code>
/// using var edge = highlights.AttachEdge(window);
/// edge.Intensity = () =&gt; voice.Frame.OutputLevel;   // Spine.Voice, or any level from 0 to 1
/// </code>
/// </example>
public interface IEdgeGlow : IDisposable
{
    /// <summary>
    /// Read on every frame on the main thread, 0 to 1: the rim's width and the glow's strength follow it, eased.
    /// <see langword="null"/>: Highlight's own Edge animation, "a model is working". Keep it cheap; if it throws,
    /// the glow stops and the exception is written to the debug log.
    /// </summary>
    Func<float>? Intensity { get; set; }

    /// <summary>The colours; unset, Edge's spectrum.</summary>
    IReadOnlyList<Color>? Colors { get; set; }
}
