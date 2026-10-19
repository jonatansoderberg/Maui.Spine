using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Avatars;

/// <summary>
/// The avatars the voice pages offer: the sound in and out first, then the characters and ambient ones, and every hosted one
/// the Avatars page has downloaded. Each character names its own voice (<c>AvatarManifest.Voices</c>), which the
/// conversation asks the server for.
/// </summary>
public static class VoiceAvatars
{
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
    ];
}

/// <summary>An avatar in a picker.</summary>
public sealed record AvatarChoice(string Name, AvatarSource Source)
{
    public override string ToString() => Name;
}
