namespace Plugin.Maui.Spine.Controls;

/// <summary>Registers <c>Plugin.Maui.Spine.Controls.Avatar.Voice</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineAvatarVoiceExtensions
{
    /// <summary>
    /// Adds VoiceView's <c>character</c> visual, an avatar driven by the conversation. <c>UseSpine()</c> calls it for an app
    /// that references this package.
    /// </summary>
    public static MauiAppBuilder UseSpineAvatarVoice(this MauiAppBuilder builder) =>
        builder.UseSpineAvatar().UseSpineVoiceView(o => o.AddVisualView<CharacterVoiceVisual>("character"));
}
