namespace Plugin.Maui.Spine.Core;

/// <summary>Theme configuration, reached through <see cref="SpineOptions.Theme"/>.</summary>
public sealed class SpineThemeOptions
{
    /// <summary>
    /// Whether the choice made through <see cref="IThemeService.Current"/> is stored in
    /// <see cref="Preferences"/> and applied again at the next launch. Default <see langword="true"/>.
    /// </summary>
    public bool Persist { get; set; } = true;

    /// <summary>
    /// Colour resource that holds the light-mode accent and that <see cref="IThemeService.Accent"/>
    /// overwrites. Default <c>Primary</c>, as in the MAUI template. <see langword="null"/> skips it.
    /// </summary>
    public string? AccentLightKey { get; set; } = "Primary";

    /// <summary>
    /// Colour resource that holds the dark-mode accent. Default <c>PrimaryDark</c>; an app without
    /// one uses the light accent in dark mode too. <see langword="null"/> skips it.
    /// </summary>
    public string? AccentDarkKey { get; set; } = "PrimaryDark";

    /// <summary>
    /// Colour resource Spine keeps at the accent of the theme in effect, for
    /// <c>{DynamicResource Accent}</c> in styles: one key that follows both a theme switch and
    /// an accent change. Default <c>Accent</c>. <see langword="null"/> skips it.
    /// </summary>
    public string? AccentKey { get; set; } = "Accent";

    /// <summary>
    /// Colour resource Spine keeps at black or white, whichever reads on <see cref="AccentKey"/>
    /// (see <see cref="SpineAccent.TextOn"/>). Default <c>OnAccent</c>. <see langword="null"/> skips it.
    /// </summary>
    public string? OnAccentKey { get; set; } = "OnAccent";

    internal Func<ResourceDictionary>? LightTokens { get; private set; }

    internal Func<ResourceDictionary>? DarkTokens { get; private set; }

    /// <summary>
    /// Registers a light and a dark token dictionary declaring the same keys. Spine merges one
    /// dictionary into the application resources and copies the right set into it on every
    /// theme change, so every <c>{DynamicResource}</c> that uses a token re-resolves. A
    /// <c>{StaticResource}</c> or a <c>DataTrigger</c> keeps the value it captured; use
    /// <c>{DynamicResource}</c> for tokens.
    /// </summary>
    public SpineThemeOptions UseTokens<TLight, TDark>()
        where TLight : ResourceDictionary, new()
        where TDark : ResourceDictionary, new()
    {
        LightTokens = () => new TLight();
        DarkTokens = () => new TDark();
        return this;
    }
}
