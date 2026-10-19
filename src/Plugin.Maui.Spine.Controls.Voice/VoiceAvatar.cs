namespace Plugin.Maui.Spine.Controls;

/// <summary>The avatar a <see cref="VoiceView"/> shows as its <c>character</c>.</summary>
/// <example>
/// <code language="xml"><![CDATA[
/// <VoiceView Session="{Binding Voice}" Kind="character" VoiceAvatar.Source="{Binding Avatar}" HeightRequest="240" />
/// ]]></code>
/// </example>
public static class VoiceAvatar
{
    /// <summary>The attached property for the character's avatar; unset, <see cref="SpineAvatarOptions.DefaultSource"/>.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.CreateAttached(
        "Source", typeof(AvatarSource), typeof(VoiceAvatar), null);

    /// <summary>The character's avatar on <paramref name="view"/>.</summary>
    public static AvatarSource? GetSource(BindableObject view) => (AvatarSource?)view.GetValue(SourceProperty);

    /// <summary>Sets the character's avatar on <paramref name="view"/>.</summary>
    public static void SetSource(BindableObject view, AvatarSource? value) => view.SetValue(SourceProperty, value);
}
