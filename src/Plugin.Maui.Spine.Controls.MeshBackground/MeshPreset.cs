namespace Plugin.Maui.Spine.Controls;

/// <summary>The colours a <see cref="MeshBackground"/> uses when its <see cref="MeshBackground.Colors"/> are not set; each has a light and a dark version.</summary>
public enum MeshPreset
{
    /// <summary>Built from the app's accent (<c>IThemeService.Accent</c>, or the <c>Primary</c> colour resources): a light wash of it in light mode, glows of it in the dark.</summary>
    Accent,

    /// <summary>Green, teal and violet: northern lights over a night sky in dark mode, pastels in light mode.</summary>
    Aurora,

    /// <summary>Amber, coral and rose: dusk in dark mode, a warm dawn in light mode.</summary>
    Sunset,
}
