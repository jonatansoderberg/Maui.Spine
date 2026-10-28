namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// What an avatar shows of its options and presets (format 1.1): every option's value, merged from its default, the
/// preset's and the app's, and the preset's theme colours. The view puts the app's own colours on top.
/// </summary>
internal static class AvatarChoices
{
    /// <summary>Every option's shown value: the app's, else the preset's, else the option's default.</summary>
    public static Dictionary<string, string> Options(AvatarManifest manifest, string? preset, IReadOnlyDictionary<string, string> chosen)
    {
        var shown = Preset(manifest, preset);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, option) in manifest.Options)
            values[name] = chosen.GetValueOrDefault(name) ?? shown?.Options.GetValueOrDefault(name) ?? option.Default;
        return values;
    }

    /// <summary>The preset's theme colours in one mode, slot → <c>#RRGGBB</c> or <c>#RRGGBBAA</c>; empty without a preset.</summary>
    public static Dictionary<string, string> PresetColors(AvatarManifest manifest, string? preset, bool dark) =>
        Preset(manifest, preset)?.Themes.ToDictionary(p => p.Key, p => dark ? p.Value.Dark : p.Value.Light, StringComparer.Ordinal)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Why <paramref name="value"/> is not a value of the option <paramref name="name"/>, or null when it is.</summary>
    public static string? CheckOption(AvatarManifest manifest, string name, string? value) =>
        !manifest.Options.TryGetValue(name, out var option) ? $"{manifest.Id} has no option '{name}'; it has {List(manifest.Options.Keys)}"
        : value is not null && option.IndexOf(value) < 0 ? $"option '{name}' of {manifest.Id} has no value '{value}'; it has {string.Join(", ", option.Values)}"
        : null;

    /// <summary>Why <paramref name="preset"/> is not a preset of the avatar, or null when it is.</summary>
    public static string? CheckPreset(AvatarManifest manifest, string preset) =>
        manifest.Presets.ContainsKey(preset) ? null : $"{manifest.Id} has no preset '{preset}'; it has {List(manifest.Presets.Keys)}";

    private static AvatarPreset? Preset(AvatarManifest manifest, string? name) =>
        name is not null ? manifest.Presets.GetValueOrDefault(name) : null;

    private static string List(IEnumerable<string> names) => names.Any() ? string.Join(", ", names) : "none";
}
