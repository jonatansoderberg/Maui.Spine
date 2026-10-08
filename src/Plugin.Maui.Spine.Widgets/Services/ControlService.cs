using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Common.Serialization;

namespace Plugin.Maui.Spine.Widgets.Services;

internal sealed class ControlService(
    WidgetRegistry _registry,
    IWidgetPlatform _platform,
    WidgetIconAssets _icons,
    IServiceProvider _services,
    ILogger<ControlService> _logger) : IControlService
{
    public bool IsSupported => _platform.AreControlsSupported;

    public IReadOnlyList<string> Kinds => _registry.ControlKinds;

    public Task RefreshAsync<TProvider>(CancellationToken cancellationToken = default) where TProvider : IControlProvider
    {
        var kind = _registry.ControlKindFor(typeof(TProvider))
            ?? throw new InvalidOperationException($"{typeof(TProvider).Name} is not decorated with [Control].");
        return RefreshAsync(kind, cancellationToken);
    }

    public async Task RefreshAsync(string kind, CancellationToken cancellationToken = default)
    {
        if (!_platform.AreControlsSupported) return;
        await BuildAsync(kind, cancellationToken);
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_platform.AreControlsSupported) return;

        foreach (var kind in _registry.ControlKinds)
        {
            try
            {
                await RefreshAsync(kind, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Refreshing control \"{Kind}\" failed.", kind);
            }
        }
    }

    public async Task<bool> RequestAddAsync(string kind)
    {
        if (!_platform.AreControlsSupported) return false;
        var state = await BuildAsync(kind, CancellationToken.None);
        return await _platform.RequestAddControlAsync(kind, state);
    }

    /// <summary>The provider's state, stored where the native control reads it, and the control redrawn.</summary>
    private async Task<ControlState> BuildAsync(string kind, CancellationToken cancellationToken)
    {
        var providerType = _registry.ControlProviderTypeFor(kind)
            ?? throw new InvalidOperationException($"No [Control(\"{kind}\")] provider was discovered.");

        var provider = (IControlProvider)ActivatorUtilities.CreateInstance(_services, providerType);
        var state = await provider.GetStateAsync(new ControlContext(kind), cancellationToken);

        if (state.Icon is { } icon)
            await _icons.EnsureAsync([icon], cancellationToken);

        _platform.WriteControl(kind, WidgetJson.Serialize(state));
        _platform.ReloadControl(kind);
        return state;
    }
}
