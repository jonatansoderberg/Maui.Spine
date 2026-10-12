namespace Plugin.Maui.Spine.Controls;

/// <summary>How <see cref="Highlight"/> draws attention to a view.</summary>
public enum HighlightKind
{
    /// <summary>No highlight.</summary>
    None,

    /// <summary>A rim of moving colour round the view: a model is working on it.</summary>
    Edge,

    /// <summary>Rings that ripple out from the view: "tap here" without covering the screen.</summary>
    Pulse,

    /// <summary>Dims everything but the view and shows a tip. Comes with tours; plays nothing yet.</summary>
    Spotlight,

    /// <summary>One light sweep across the view: a quiet "new" marker.</summary>
    Sheen,

    /// <summary>A point of light running along the view's border: progress without a number.</summary>
    Trace,

    /// <summary>A halo that swells and fades: a state that lasts, such as a live microphone.</summary>
    Breathe,
}
