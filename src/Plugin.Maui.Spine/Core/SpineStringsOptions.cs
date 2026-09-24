using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Core;

/// <summary>String configuration, reached through <see cref="SpineOptions.Strings"/>; see <see cref="ISpineStrings"/>.</summary>
public sealed class SpineStringsOptions
{
    /// <summary>
    /// Whether a culture set through <see cref="ISpineStrings.Culture"/> is stored in
    /// <see cref="Preferences"/> and applied again at the next launch. Default <see langword="true"/>.
    /// </summary>
    public bool Persist { get; set; } = true;

    internal List<IStringProvider> Providers { get; } = [];

    /// <summary>
    /// Adds a provider of the app's own text, asked before the embedded XML documents of the
    /// registered assemblies and before every package's defaults.
    /// </summary>
    public SpineStringsOptions AddProvider(IStringProvider provider)
    {
        Providers.Add(provider);
        return this;
    }
}
