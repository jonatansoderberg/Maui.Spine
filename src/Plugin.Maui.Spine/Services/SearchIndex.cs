using System.Reflection;
using AsyncAwaitBestPractices;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Svg;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// <see cref="ISearchIndex"/>: keeps a <see cref="SearchRecord"/> of every item in
/// <c>spine-search.json</c> and hands the items to the platform (the <c>.Apple</c> and <c>.Android</c>
/// parts). A tapped result comes back through <see cref="Open"/>.
/// </summary>
internal sealed partial class SearchIndex : ISearchIndex
{
    private readonly IServiceProvider _services;
    private readonly NavigationRegistry _registry;
    private readonly SpineOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SearchIndex(IServiceProvider services, NavigationRegistry registry, SpineOptions options, ILoggerFactory loggers)
    {
        _services = services;
        _registry = registry;
        _options = options;
        _logger = loggers.CreateLogger("Plugin.Maui.Spine.Search");
    }

    private static string StorePath => Path.Combine(FileSystem.AppDataDirectory, "spine-search.json");

    public bool IsSupported => PlatformSupported;

    public Task UpsertAsync(SearchableItem item, CancellationToken cancellationToken = default) =>
        UpsertAsync([item], cancellationToken);

    public async Task UpsertAsync(IEnumerable<SearchableItem> items, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var records = items.Select(item => SearchRecords.From(item, _options.Search.JsonOptions, now)).ToList();
        if (!IsSupported || records.Count == 0) return;

        await WithStoreAsync(async stored =>
        {
            foreach (var record in records)
                stored[record.Id] = record;

            await PlatformUpsertAsync(records, stored.Values);
            return true;
        }, cancellationToken);
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken = default) =>
        RemoveWhereAsync(record => record.Id == id, cancellationToken);

    public Task RemoveAllAsync(CancellationToken cancellationToken = default) =>
        RemoveWhereAsync(record => !record.IsPage, cancellationToken);

    public async Task<IReadOnlyList<string>> GetIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported) return [];

        IReadOnlyList<string> ids = [];
        await WithStoreAsync(stored =>
        {
            ids = [.. stored.Values.OrderByDescending(r => r.Updated).Select(r => r.Id)];
            return Task.FromResult(false);
        }, cancellationToken);
        return ids;
    }

    private async Task RemoveWhereAsync(Func<SearchRecord, bool> match, CancellationToken cancellationToken)
    {
        if (!IsSupported) return;

        await WithStoreAsync(async stored =>
        {
            var ids = stored.Values.Where(match).Select(r => r.Id).ToList();
            if (ids.Count == 0) return false;

            foreach (var id in ids)
                stored.Remove(id);

            await PlatformRemoveAsync(ids, stored.Values);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="change"/> on the records, one caller at a time, and writes them back when it
    /// returns <see langword="true"/>. A file that cannot be read is logged and started over: the
    /// platform's index still has the items, but they no longer open anything until indexed again.
    /// </summary>
    private async Task WithStoreAsync(Func<Dictionary<string, SearchRecord>, Task<bool>> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = Load();
            if (await change(stored))
            {
                using var stream = File.Create(StorePath);
                SearchRecords.Write(stream, stored.Values);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, SearchRecord> Load()
    {
        if (!File.Exists(StorePath)) return [];
        try
        {
            using var stream = File.OpenRead(StorePath);
            return SearchRecords.Read(stream);
        }
        catch (System.Text.Json.JsonException e)
        {
            _logger.LogError(e, "{Path} could not be read; the search index starts over.", StorePath);
            return [];
        }
    }

    /// <summary>
    /// A tapped result: waits for the app's root page on a cold start, then shows the record's target.
    /// A record that no longer opens anything is logged with the reason and removed, so the result stops showing.
    /// </summary>
    internal void Open(string id) => OpenAsync(id).SafeFireAndForget(e => _logger.LogError(e, "Opening search result \"{Id}\" failed.", id));

    private async Task OpenAsync(string id)
    {
        var navigation = _services.GetRequiredService<INavigationService>();
        if (navigation is NavigationService spine)
            await spine.WhenRootSet;

        SearchRecord? record = null;
        await WithStoreAsync(stored =>
        {
            stored.TryGetValue(id, out record);
            return Task.FromResult(false);
        }, CancellationToken.None);

        string? problem = "Spine has no record of it";
        var target = record is null ? null : SearchRecords.Resolve(record, _registry.Find, _options.Search.JsonOptions, out problem);
        if (target is null)
        {
            _logger.LogWarning("Search result \"{Id}\" opens nothing: {Problem}. It is removed from the index.", id, problem);
            await RemoveDeadAsync(id);
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => Show(navigation, target));
    }

    /// <summary>Removes the id from the platform's index also when Spine has no record of it.</summary>
    private Task RemoveDeadAsync(string id) => WithStoreAsync(async stored =>
    {
        var known = stored.Remove(id);
        await PlatformRemoveAsync([id], stored.Values);
        return known;
    }, CancellationToken.None);

    private static readonly MethodInfo ShowPage = typeof(INavigationService).GetMethods()
        .Single(m => m.Name == nameof(INavigationService.ShowAsync) && m.GetGenericArguments().Length == 1);

    private static readonly MethodInfo ShowPageWithParameter = typeof(INavigationService).GetMethods()
        .Single(m => m.Name == nameof(INavigationService.ShowAsync) && m.GetGenericArguments().Length == 2);

    private static Task Show(INavigationService navigation, NavigationTarget target) => target.ParameterType is { } parameterType
        ? (Task)ShowPageWithParameter.MakeGenericMethod(target.Page, parameterType).Invoke(navigation, [target.Parameter])!
        : (Task)ShowPage.MakeGenericMethod(target.Page).Invoke(navigation, null)!;

    /// <summary>
    /// Brings the <see cref="SearchableAttribute"/> pages' entries up to date: new and changed ones are
    /// indexed, and entries of pages that lost the attribute (or no longer exist) are removed.
    /// </summary>
    private async Task SyncPagesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var pages = _registry.Pages
            .Select(page => (Page: page.Key, Meta: page.Value, Searchable: page.Key.GetCustomAttribute<SearchableAttribute>()))
            .Where(page => page.Searchable is not null)
            .Select(page => SearchRecords.From(new SearchableItem(
                SearchRecords.PageId(page.Page),
                page.Searchable!.Title ?? (string.IsNullOrEmpty(page.Meta.Title) ? page.Page.Name : page.Meta.Title),
                NavigationTarget.ForPage(page.Page),
                page.Searchable.Description,
                page.Searchable.Icon,
                page.Searchable.Keywords), _options.Search.JsonOptions, now))
            .ToDictionary(r => r.Id);

        await WithStoreAsync(async stored =>
        {
            var changed = pages.Values.Where(r => !stored.TryGetValue(r.Id, out var old) || !old.SameContent(r)).ToList();
            var gone = stored.Values.Where(r => r.IsPage && !pages.ContainsKey(r.Id)).Select(r => r.Id).ToList();

            foreach (var record in changed)
                stored[record.Id] = record;
            foreach (var id in gone)
                stored.Remove(id);

            if (gone.Count > 0)
                await PlatformRemoveAsync(gone, stored.Values);
            await PlatformStartAsync(changed, stored.Values);
            return changed.Count > 0 || gone.Count > 0;
        }, CancellationToken.None);
    }

    /// <summary>
    /// The item's icon as a PNG: the SVG in white (or black, on a light accent) on a tile of the app's
    /// accent. <paramref name="glyph"/> is the share of the side the SVG takes. <see langword="null"/>
    /// without an icon, or when the SVG is not embedded, which is logged.
    /// </summary>
    private byte[]? Tile(SearchRecord record, int size, float glyph, float cornerRadius)
    {
        if (record.Icon is not { } icon || _services.GetService<ISvgIconService>() is not { } svg) return null;

        SvgIcon source;
        try
        {
            source = svg.FromEmbeddedSvg(ShortcutIcons.SvgFile(icon));
        }
        catch (FileNotFoundException e)
        {
            _logger.LogWarning(e, "Search item \"{Id}\": no embedded {Svg}; it shows without an icon.", record.Id, ShortcutIcons.SvgFile(icon));
            return null;
        }

        var accent = _services.GetService<IThemeService>()?.Accent?.Light ?? Color.FromArgb("#007AFF");
        var glyphSize = (int)(size * glyph);
        using var glyphBitmap = SKBitmap.Decode(source.GetPng(glyphSize));

        using var bitmap = new SKBitmap(new SKImageInfo(size, size));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var background = new SKPaint { Color = accent.ToSKColor(), IsAntialias = true };
            canvas.DrawRoundRect(0, 0, size, size, cornerRadius, cornerRadius, background);

            using var tint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(SpineAccent.TextOn(accent).ToSKColor(), SKBlendMode.SrcIn) };
            var offset = (size - glyphSize) / 2f;
            canvas.DrawBitmap(glyphBitmap, offset, offset, tint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static partial bool PlatformSupported { get; }

    /// <summary>Hands new and changed records to the platform; <paramref name="all"/> is every record after the change.</summary>
    private partial Task PlatformUpsertAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all);

    /// <summary>Takes the ids out of the platform's index; <paramref name="all"/> is every record after the change.</summary>
    private partial Task PlatformRemoveAsync(IReadOnlyList<string> ids, IEnumerable<SearchRecord> all);

    /// <summary>At startup, after the pages' entries are synced: <paramref name="changed"/> are those that need indexing.</summary>
    private partial Task PlatformStartAsync(IReadOnlyList<SearchRecord> changed, IEnumerable<SearchRecord> all);

    /// <summary>Hooks the platform's way back from a tapped result.</summary>
    internal static partial void ConfigurePlatform(MauiAppBuilder builder);

    /// <summary>Syncs the pages' entries once the app has started, off the main thread.</summary>
    internal sealed class Initializer : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services)
        {
            var index = services.GetRequiredService<SearchIndex>();
            if (!index.IsSupported) return;

            Task.Run(index.SyncPagesAsync).SafeFireAndForget(e => index._logger.LogError(e, "Indexing the [Searchable] pages failed."));
        }
    }
}
