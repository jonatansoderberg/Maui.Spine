using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>
/// Stands in on a platform Spine.Push has no implementation for — Windows in v1. Everything answers
/// that push is unsupported, so an app that multi-targets such a platform degrades instead of
/// throwing the first time it resolves <see cref="IPushService"/>.
/// </summary>
internal sealed class UnsupportedPushPlatform : IPushPlatform
{
    /// <inheritdoc />
    /// <remarks>Nothing reaches a transport from here; the value only completes the contract.</remarks>
    public PushPlatform Platform => PushPlatform.Windows;

    /// <inheritdoc />
    public PushStatus Status => PushStatus.Unsupported;

    /// <inheritdoc />
    public string? Handle => null;

    /// <inheritdoc />
    public ApnsEnvironment? Environment => null;

    /// <inheritdoc />
    public string? WidgetToken => null;

    /// <inheritdoc />
    public event Action<string>? HandleChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task<PushStatus> RequestPermissionAsync(PushPermission permission, CancellationToken cancellationToken) =>
        Task.FromResult(PushStatus.Unsupported);

    /// <inheritdoc />
    public Task OpenSettingsAsync() => Task.CompletedTask;
}
