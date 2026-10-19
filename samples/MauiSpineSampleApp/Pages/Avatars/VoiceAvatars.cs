using Plugin.Maui.Spine.Controls;

namespace MauiSpineSampleApp.Pages.Avatars;

/// <summary>The avatars the voice pages offer: the sound in and out first, then the characters and ambient ones.</summary>
public static class VoiceAvatars
{
    public static IReadOnlyList<AvatarChoice> All { get; } =
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
