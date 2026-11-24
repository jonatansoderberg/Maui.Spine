namespace Plugin.Maui.Spine.Controls;

/// <summary>How an <see cref="AnimatedLabel"/> catches the light.</summary>
public enum AnimatedLabelSheen
{
    /// <summary>Flat colour, no sweep.</summary>
    None,

    /// <summary>The text keeps its colour; a bright band sweeps over the glyphs now and then.</summary>
    Glint,

    /// <summary>The text colour as polished metal, lighter on top and darker below the middle, and the glint.</summary>
    Metal,
}
