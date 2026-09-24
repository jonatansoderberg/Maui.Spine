namespace Plugin.Maui.Spine.Core;

/// <summary>Theme configuration, reached through <see cref="SpineOptions.Theme"/>.</summary>
public sealed class SpineThemeOptions
{
    /// <summary>
    /// Whether the choice made through <see cref="IThemeService.Current"/> is stored in
    /// <see cref="Preferences"/> and applied again at the next launch. Default <see langword="true"/>.
    /// </summary>
    public bool Persist { get; set; } = true;

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
