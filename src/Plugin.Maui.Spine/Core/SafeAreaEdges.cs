namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Identifies which edges of the screen Spine manages system-bar padding for.
/// Edges included here are padded by the content host so that content stops at the system bar;
/// excluded edges cause content to render edge-to-edge behind that system bar — use
/// <see cref="ViewModelBase.SafeAreaInsets"/> to offset your content on those edges manually.
/// Inset values are provided by <see cref="ISystemInsetsProvider"/> and are non-zero
/// on platforms that require manual compensation (e.g. Android status bar and navigation bar).
/// </summary>
[Flags]
public enum SafeAreaEdges
{
    /// <summary>No edges are managed; content renders edge-to-edge on all sides.</summary>
    None = 0,

    /// <summary>The host pads the top edge (status bar).</summary>
    Top = 1,

    /// <summary>The host pads the bottom edge (navigation / gesture bar).</summary>
    Bottom = 2,

    /// <summary>The host pads the left edge.</summary>
    Left = 4,

    /// <summary>The host pads the right edge.</summary>
    Right = 8,

    /// <summary>The host pads all edges. This is the default for most pages.</summary>
    All = Top | Bottom | Left | Right,
}
