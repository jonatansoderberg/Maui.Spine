using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Voice;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// VoiceView's <c>character</c>: an <see cref="AvatarView"/> inside the VoiceView, ticked by the VoiceView's own frame (it
/// runs no timer of its own) and driven by a <see cref="VoiceAvatarDriver"/> fed that frame. The avatar is the VoiceView's
/// <see cref="VoiceAvatar.SourceProperty">VoiceAvatar.Source</see>, or <see cref="SpineAvatarOptions.DefaultSource"/>.
/// </summary>
internal sealed class CharacterVoiceVisual : VoiceVisualView
{
    private readonly AvatarView _avatar = new() { IsHosted = true };
    private VoiceView? _host;
    private VoiceAvatarDriver? _driver;
    private bool _warned;

    public CharacterVoiceVisual()
    {
        // The host's description and announcements speak for the conversation; the avatar inside is not a second element.
        AutomationProperties.SetIsInAccessibleTree(_avatar, false);
        Content = _avatar;
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (_host is not null)
            _host.PropertyChanged -= OnHostChanged;
        _host = FindHost();
        if (_host is not null)
            _host.PropertyChanged += OnHostChanged;
        Bind();
    }

    public override void OnFrame(VoiceFrame frame, VoiceVisualContext context)
    {
        if (_driver is null)
            return;
        _driver.Feed(frame);
        _avatar.MotionMode = context.ReducedMotion ? Avatar.AvatarMotionMode.Reduced : Avatar.AvatarMotionMode.System;
        _avatar.TickFromHost(context.Delta);
    }

    private void OnHostChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(VoiceView.Session) or nameof(VoiceView.Audio) || e.PropertyName == VoiceAvatar.SourceProperty.PropertyName)
            Bind();
    }

    private void Bind()
    {
        var source = _host is null ? null : VoiceAvatar.GetSource(_host) ?? SpineAvatarExtensions.Options.DefaultSource;
        if (_host is not null && source is null && !_warned)
        {
            _warned = true;
            (_host.Handler?.MauiContext?.Services ?? IPlatformApplication.Current?.Services)?.GetService<ILoggerFactory>()
                ?.CreateLogger("Plugin.Maui.Spine.Controls.Avatar.Voice")
                .LogWarning("VoiceView: the character has no avatar. Set VoiceAvatar.Source on the VoiceView, or SpineAvatarOptions.DefaultSource in UseSpineAvatar.");
        }
        if (!Equals(_avatar.Source, source))
            _avatar.Source = source;

        var session = _host?.Session;
        var audio = _host?.Audio;
        if (session is null && audio is null)
        {
            _avatar.Driver = _driver = null;
            return;
        }
        _avatar.Driver = _driver = new VoiceAvatarDriver(session, audio);
    }

    private VoiceView? FindHost()
    {
        for (var element = Parent; element is not null; element = element.Parent)
        {
            if (element is VoiceView view)
                return view;
        }
        return null;
    }
}
