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
                    NavigableRegionAttribute region => region.WithDefaults(_options.RegionDefaults),
                    NavigableSheetAttribute sheet   => sheet.WithDefaults(_options.SheetDefaults),
                    _                               => attr,
                };
            }
    }

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
