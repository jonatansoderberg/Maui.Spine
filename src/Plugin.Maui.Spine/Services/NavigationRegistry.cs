using Plugin.Maui.Spine.Core;
using System.Reflection;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Internal registry that maps page types to their resolved <see cref="NavigableAttribute"/>
/// (with global defaults applied). Populated at startup from the assemblies listed in
/// <see cref="SpineOptions.Assemblies"/>.
/// </summary>
internal sealed class NavigationRegistry
{
    private readonly Dictionary<Type, NavigableAttribute> _registry = new();

    private readonly SpineOptions _options;

    /// <summary>
    /// Tab root pages discovered during the scan, ordered by <see cref="NavigableTabAttribute.Order"/>.
    /// Empty when the app declares no <see cref="NavigableTabAttribute"/> pages (no tab host).
    /// </summary>
    public IReadOnlyList<TabDefinition> Tabs { get; }

    /// <summary>
    /// Initializes the registry by scanning all assemblies configured in <paramref name="options"/>.
    /// </summary>
    public NavigationRegistry(SpineOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var assemblies = _options.Assemblies.Count > 0
            ? _options.Assemblies
            : new[] { Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly() };

        foreach (var assembly in assemblies)
            RegisterNavigableTypesFrom(assembly);

        Tabs = CollectTabs();
    }

    private void RegisterNavigableTypesFrom(Assembly assembly)
    {
        var types = assembly
            .DefinedTypes
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetCustomAttributes().Any(a => a is NavigableAttribute));

            foreach (var type in types)
            {
                var attr = type.GetCustomAttributes().OfType<NavigableAttribute>().First();

                _registry[type.AsType()] = attr switch
                {
                    NavigableTabAttribute tab       => tab.WithDefaults(_options.TabDefaults),
                    NavigableRegionAttribute region => region.WithDefaults(_options.RegionDefaults),
                    NavigableSheetAttribute sheet   => sheet.WithDefaults(_options.SheetDefaults),
                    _                               => attr,
                };
            }
    }

    private List<TabDefinition> CollectTabs()
    {
        var tabs = _registry
            .Where(kvp => kvp.Value is NavigableTabAttribute)
            .Select(kvp => new TabDefinition(kvp.Key, (NavigableTabAttribute)kvp.Value))
            .OrderBy(t => t.Meta.Order)
            .ToList();

        if (tabs.Count == 0)
            return tabs;

        var duplicated = tabs.GroupBy(t => t.Meta.Order).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new InvalidOperationException(
                $"Multiple [NavigableTab] pages declare Order = {duplicated.Key}: " +
                $"{string.Join(", ", duplicated.Select(t => t.PageType.Name))}. " +
                "Set a distinct Order on every tab — assembly scan order is nondeterministic.");

        if (tabs.Count > 5)
            throw new InvalidOperationException(
                $"{tabs.Count} [NavigableTab] pages were discovered, but the tab bar supports at most 5. " +
                "iOS would demote extras into a 'More' item and Android guidelines cap at 5 — reduce the tab count.");

        return tabs;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="type"/> is a <see cref="NavigableTabAttribute"/> page.</summary>
    public bool IsTab(Type type) => _registry.TryGetValue(type, out var attr) && attr is NavigableTabAttribute;

    /// <summary>Returns <see langword="true"/> when <paramref name="type"/> is registered as a navigable page.</summary>
    public bool Contains(Type type) => _registry.ContainsKey(type);

    /// <summary>
    /// Returns the <see cref="NavigableAttribute"/> (with defaults applied) for <paramref name="type"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="type"/> is not registered.</exception>
    public NavigableAttribute Get(Type type)
        => _registry.TryGetValue(type, out var value)
            ? value
            : throw new InvalidOperationException(
                $"Type '{type.FullName}' is not registered as a navigable page. " +
                $"Did you forget to add a navigable attribute?");
}

/// <summary>A discovered tab root: the page type and its resolved <see cref="NavigableTabAttribute"/>.</summary>
internal sealed record TabDefinition(Type PageType, NavigableTabAttribute Meta);
