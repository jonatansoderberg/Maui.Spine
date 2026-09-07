using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Widgets.Serialization;

namespace Plugin.Maui.Spine.Widgets.Services;

internal sealed class WidgetService(
    WidgetRegistry _registry,
    IWidgetPlatform _platform,
    WidgetIconAssets _icons,
    IServiceProvider _services,
    ILogger<WidgetService> _logger) : IWidgetService
{
    public bool IsSupported => _platform.IsSupported;

    public IReadOnlyList<string> Kinds => _registry.Kinds;

    public Task RefreshAsync<TProvider>(CancellationToken cancellationToken = default) where TProvider : IWidgetProvider
    {
        var kind = _registry.KindFor(typeof(TProvider))
            ?? throw new InvalidOperationException($"{typeof(TProvider).Name} is not decorated with [Widget].");
        return RefreshAsync(kind, cancellationToken);
    }

    public async Task RefreshAsync(string kind, CancellationToken cancellationToken = default)
    {
        if (!_platform.IsSupported) return;

        var providerType = _registry.ProviderTypeFor(kind)
            ?? throw new InvalidOperationException($"No [Widget(\"{kind}\")] provider was discovered.");

        var provider = (IWidgetProvider)ActivatorUtilities.CreateInstance(_services, providerType);
        var timeline = await provider.BuildTimelineAsync(new WidgetContext(kind), cancellationToken);
        if (timeline.Entries.Count == 0)
        {
            _logger.LogWarning("Widget \"{Kind}\" built an empty timeline; the widget keeps its previous content.", kind);
            return;
        }

        await _icons.EnsureAsync(timeline.Entries.SelectMany(e => e.Trees?.Values ?? [e.Tree!]), cancellationToken);
        _platform.WriteTimeline(kind, WidgetJson.Serialize(timeline));
        _platform.Reload(kind);
    }

    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        if (!_platform.IsSupported) return;

        foreach (var kind in _registry.Kinds)
        {
            try
            {
                await RefreshAsync(kind, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Refreshing widget \"{Kind}\" failed.", kind);
            }
        }
    }

    public Task StoreAssetAsync(string assetId, Stream png, CancellationToken cancellationToken = default) =>
        _platform.StoreAssetAsync(assetId, png, cancellationToken);

    public Uri LinkFor(string kind) => new($"{AppInfo.Current.PackageName}://widget/{Uri.EscapeDataString(kind)}");
}
