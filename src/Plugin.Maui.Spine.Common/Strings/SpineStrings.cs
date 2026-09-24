using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// The default <see cref="ISpineStrings"/>: an ordered list of providers, a per-culture cache and
/// the culture fallback walk. <see cref="Current"/> is the instance Spine registers, for controls
/// that need text before dependency injection is reachable.
/// </summary>
public sealed class SpineStrings : ISpineStrings
{
    /// <summary>The instance Spine registers as <see cref="ISpineStrings"/>.</summary>
    public static SpineStrings Current { get; } = new();

    private readonly List<IStringProvider> _appProviders = [];
    private readonly List<IStringProvider> _defaultProviders = [];
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _cache = new();
    private readonly ConcurrentDictionary<(IStringProvider, string), IReadOnlyDictionary<string, string>?> _loaded = new();
    private readonly HashSet<string> _reported = [];
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    /// <inheritdoc/>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (_culture.Equals(value))
                return;

            _culture = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public event EventHandler<string>? Missing;

    /// <inheritdoc/>
    public string this[string key] => Resolve(_culture, key);

    /// <inheritdoc/>
    public string Get(string key, params object?[] args) =>
        args.Length == 0 ? this[key] : string.Format(_culture, this[key], args);

    /// <inheritdoc/>
    public string Get(string key, int count)
    {
        var form = count switch
        {
            1 => key + ".one",
            0 when Has(key + ".zero") => key + ".zero",
            _ => key + ".other",
        };

        return string.Format(_culture, this[form], count);
    }

    /// <summary>
    /// Adds a provider the app owns. App providers are asked before every package's defaults,
    /// in the order they were added.
    /// </summary>
    public SpineStrings AddProvider(IStringProvider provider)
    {
        _appProviders.Add(provider);
        Reload();
        return this;
    }

    /// <summary>
    /// Adds the defaults a Spine package ships. Asked after every app provider, in the order the
    /// packages registered, so an app overrides any of their keys by defining it itself.
    /// </summary>
    public SpineStrings AddDefaults(IStringProvider provider)
    {
        _defaultProviders.Add(provider);
        Reload();
        return this;
    }

    /// <summary>Drops every cached value and asks the providers again on the next lookup.</summary>
    public void Reload()
    {
        _cache.Clear();
        _loaded.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether any provider has <paramref name="key"/> for the current culture or one of its parents.</summary>
    public bool Has(string key) => Lookup(_culture, key) is not null;

    private string Resolve(CultureInfo culture, string key)
    {
        var cache = _cache.GetOrAdd(culture.Name, static _ => new ConcurrentDictionary<string, string>());

        if (cache.TryGetValue(key, out var cached))
            return cached;

        var value = Lookup(culture, key);

        if (value is null)
        {
            value = key;
            Report(key);
        }

        cache[key] = value;
        return value;
    }

    private string? Lookup(CultureInfo culture, string key)
    {
        foreach (var provider in _appProviders)
        {
            if (LookupChain(provider, culture, key) is { } value)
                return value;
        }

        foreach (var provider in _defaultProviders)
        {
            if (LookupChain(provider, culture, key) is { } value)
                return value;
        }

        return null;
    }

    private string? LookupChain(IStringProvider provider, CultureInfo culture, string key)
    {
        for (var current = culture; ; current = current.Parent)
        {
            var map = _loaded.GetOrAdd((provider, current.Name), static (id, c) => id.Item1.Load(c), current);

            if (map is not null && map.TryGetValue(key, out var value))
                return value;

            if (current.Equals(CultureInfo.InvariantCulture))
                return null;
        }
    }

    private void Report(string key)
    {
        lock (_reported)
        {
            if (!_reported.Add(key))
                return;
        }

        Debug.WriteLine($"[Spine] No string for '{key}' in '{_culture.Name}'.");
        Missing?.Invoke(this, key);
    }
}
