using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using System.Reflection;

namespace Plugin.Maui.Spine.Widgets.Services;

/// <summary>
/// Maps widget and control kinds to provider types, discovered by scanning the Spine assemblies for
/// <see cref="WidgetAttribute"/> and <see cref="ControlAttribute"/> in one pass.
/// </summary>
internal sealed class WidgetRegistry
{
    private readonly Dictionary<string, Type> _providers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _controls = new(StringComparer.Ordinal);

    public WidgetRegistry(SpineOptions spineOptions, ILogger<WidgetRegistry> logger)
    {
        foreach (var assembly in spineOptions.Assemblies)
        foreach (var type in SafeTypes(assembly))
        {
            if (!type.IsClass || type.IsAbstract) continue;

            if (type.GetCustomAttribute<ControlAttribute>() is { } control)
            {
                if (!typeof(IControlProvider).IsAssignableFrom(type))
                    logger.LogWarning("{Type} is decorated with [Control(\"{Kind}\")] but does not implement IControlProvider; ignored.", type.FullName, control.Kind);
                else if (!_controls.TryAdd(control.Kind, type))
                    logger.LogWarning("Control kind \"{Kind}\" is declared by both {First} and {Second}; the first wins.", control.Kind, _controls[control.Kind].FullName, type.FullName);
            }

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
        ControlKinds = [.. _controls.Keys.Order(StringComparer.Ordinal)];
    }

    public IReadOnlyList<string> Kinds { get; }

    public IReadOnlyList<string> ControlKinds { get; }

    public Type? ControlProviderTypeFor(string kind) => _controls.GetValueOrDefault(kind);

    public string? ControlKindFor(Type providerType) =>
        _controls.FirstOrDefault(p => p.Value == providerType).Key;

    public Type? ProviderTypeFor(string kind) => _providers.GetValueOrDefault(kind);

    public string? KindFor(Type providerType) =>
        _providers.FirstOrDefault(p => p.Value == providerType).Key;

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t is not null)!; }
    }
}
