namespace Plugin.Maui.Spine.Controls;

/// <summary>How fast the points of a <see cref="MeshBackground"/> wander.</summary>
public enum MeshDrift
{
    /// <summary>A still mesh, drawn once and again only when something changes.</summary>
    None,

    /// <summary>A point takes about half a minute to come round; barely noticed, for a background that is always there.</summary>
    Slow,

    /// <summary>About fifteen seconds a round.</summary>
    Medium,

    /// <summary>About seven seconds a round, for a moment of attention such as onboarding or a success screen.</summary>
    Fast,
}
