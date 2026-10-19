using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Voice;
using SkiaSharp.Views.Maui;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A VoiceView kind drawn by an avatar: an <see cref="AvatarView"/> inside the VoiceView, ticked by the VoiceView's own frame
/// (it runs no timer of its own) and driven by a <see cref="VoiceAvatarDriver"/> fed that frame.
/// </summary>
/// <remarks>
/// <c>character</c> shows the VoiceView's <see cref="VoiceAvatar.SourceProperty">VoiceAvatar.Source</see>, or
/// <see cref="SpineAvatarOptions.DefaultSource"/>. <c>bars</c> and <c>ring</c> show the packages built into this assembly,
/// whose <c>input</c> and <c>output</c> slots take the VoiceView's <see cref="VoiceView.InputColor"/> and
/// <see cref="VoiceView.OutputColor"/>.
/// </remarks>
internal sealed class AvatarVoiceVisual : VoiceVisualView
{
    private readonly AvatarView _avatar = new() { IsHosted = true };
    private readonly AvatarSource? _builtIn;
    private VoiceView? _host;
    private VoiceAvatarDriver? _driver;
    private (SkiaSharp.SKColor Input, SkiaSharp.SKColor Output) _colors;
    private bool _warned;

    /// <summary>The <c>character</c> kind: the VoiceView's own avatar.</summary>
    public AvatarVoiceVisual() : this(null) { }

    /// <summary>A kind with an avatar of its own, coloured by the VoiceView.</summary>
    internal AvatarVoiceVisual(AvatarSource? builtIn)
    {
        _builtIn = builtIn;
        // The host's description and announcements speak for the conversation; the avatar inside is not a second element.
        AutomationProperties.SetIsInAccessibleTree(_avatar, false);
        Content = _avatar;
    }

    /// <summary>bars, built into this assembly.</summary>
    internal static AvatarVoiceVisual Bars() => new(BuiltIn("bars"));

    /// <summary>ring, built into this assembly.</summary>
    internal static AvatarVoiceVisual Ring() => new(BuiltIn("ring"));

    internal static AvatarSource BuiltIn(string name) =>
        AvatarSource.FromStream($"{name}.spineavatar", _ =>
            Task.FromResult(typeof(AvatarVoiceVisual).Assembly.GetManifestResourceStream($"Plugin.Maui.Spine.Controls.Voice.{name}.spineavatar")
                ?? throw new InvalidOperationException($"{name}.spineavatar is not in Plugin.Maui.Spine.Controls.Voice.")));

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
        if (_builtIn is not null && (context.Input, context.Output) != _colors)
        {
            // The VoiceView's colours, which follow the app's accent; a character keeps its own.
            _colors = (context.Input, context.Output);
            _avatar.SetThemeColor("input", context.Input.ToMauiColor());
            _avatar.SetThemeColor("output", context.Output.ToMauiColor());
        }
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
        var source = _builtIn ?? (_host is null ? null : VoiceAvatar.GetSource(_host) ?? SpineAvatarExtensions.Options.DefaultSource);
        if (_host is not null && source is null && !_warned)
        {
            _warned = true;
            (_host.Handler?.MauiContext?.Services ?? IPlatformApplication.Current?.Services)?.GetService<ILoggerFactory>()
                ?.CreateLogger("Plugin.Maui.Spine.Controls.Voice")
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
