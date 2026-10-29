using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Avatars;

/// <summary>
/// The avatars the voice pages offer: the sound in and out first, then the characters and ambient ones, the Mini figures
/// (one package, a preset each), and every hosted one the Avatars page has downloaded. A character that names its own voice
/// (<c>AvatarManifest.Voices</c>) gets it from the server; the Minis name none and speak with the session's.
/// </summary>
public static class VoiceAvatars
{
    // Before Bundled, which reads them: static fields start in the order they are written.
    private static readonly AvatarSource Mini = AvatarSource.FromMauiAsset("Avatars/mini.spineavatar");

    private static readonly (string Name, string Preset)[] Minis =
    [
        ("Mini Blue", "blue"), ("Mini Brown", "brown"), ("Mini Red", "red"), ("Mini Orange", "orange"), ("Mini Amber", "amber"),
        ("Mini Green", "green"), ("Mini Teal", "teal"), ("Mini Purple", "purple"), ("Mini Pink", "pink"),
    ];

    /// <summary>The bundled avatars, and every one the Avatars page has downloaded (Kairo, …).</summary>
    public static IReadOnlyList<AvatarChoice> All =>
        [.. Bundled, .. AvatarDownloads.Kept().Select(k => new AvatarChoice(k.Name, AvatarSource.FromFile(k.Path)))];

    private static IReadOnlyList<AvatarChoice> Bundled { get; } =
    [
        new("Bars", AvatarSource.BuiltIn("bars")),
        new("Ring", AvatarSource.BuiltIn("ring")),
        new("Nova", AvatarSource.FromMauiAsset("Avatars/nova.spineavatar")),
        new("Puff", AvatarSource.FromMauiAsset("Avatars/plush-puff.spineavatar")),
        new("Voice Totem", AvatarSource.FromMauiAsset("Avatars/voice-totem.spineavatar")),
        new("Aurora Flow", AvatarSource.FromMauiAsset("Avatars/aurora-flow.spineavatar")),
        .. Minis.Select(m => new AvatarChoice(m.Name, Mini, m.Preset)),
    ];
}

/// <summary>An avatar in a picker; <paramref name="Preset"/> picks one of a package's figures (format 1.1).</summary>
public sealed record AvatarChoice(string Name, AvatarSource Source, string? Preset = null)
{
    public override string ToString() => Name;
}
