using System.Reflection;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>Maps widget kinds to provider types, discovered by scanning the Spine assemblies for <see cref="WidgetAttribute"/>.</summary>
internal sealed class WidgetRegistry
{
    private readonly Dictionary<string, Type> _providers = new(StringComparer.Ordinal);

    public WidgetRegistry(SpineOptions spineOptions, ILogger<WidgetRegistry> logger)
    {
        foreach (var assembly in spineOptions.Assemblies)
        foreach (var type in SafeTypes(assembly))
        {
            if (!type.IsClass || type.IsAbstract) continue;
            if (type.GetCustomAttribute<WidgetAttribute>() is not { } widget) continue;

            if (!typeof(IWidgetProvider).IsAssignableFrom(type))
            {
                logger.LogWarning("{Type} is decorated with [Widget(\"{Kind}\")] but does not implement IWidgetProvider; ignored.", type.FullName, widget.Kind);
                continue;
            }

            if (!_providers.TryAdd(widget.Kind, type))
                logger.LogWarning("Widget kind \"{Kind}\" is declared by both {First} and {Second}; the first wins.", widget.Kind, _providers[widget.Kind].FullName, type.FullName);
        }

        Kinds = [.. _providers.Keys.Order(StringComparer.Ordinal)];
    }

    public IReadOnlyList<string> Kinds { get; }

    public Type? ProviderTypeFor(string kind) => _providers.GetValueOrDefault(kind);

    public string? KindFor(Type providerType) =>
        _providers.FirstOrDefault(p => p.Value == providerType).Key;

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null)!; }
    }
}
