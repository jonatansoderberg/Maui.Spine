using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Turns <see cref="PageActionAttribute"/> declarations on a view model into
/// <see cref="PageAction"/> instances, and its <see cref="PageSearchAttribute"/> into
/// <see cref="ViewModelBase.Search"/>, once per view model instance.
/// </summary>
internal static class PageActionDiscovery
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed record Template(PageActionAttribute Attribute, PropertyInfo Command);

    private static readonly ConcurrentDictionary<Type, Template[]> _templates = new();

    /// <summary>
    /// Adds the view model's declared actions to <see cref="ViewModelBase.PageActions"/>.
    /// A second call on the same instance does nothing, so singleton pages keep one set.
    /// </summary>
    public static void Populate(ViewModelBase viewModel)
    {
        if (viewModel.DeclaredActionsAdded)
            return;

        viewModel.DeclaredActionsAdded = true;
        viewModel.Search ??= PageSearchDiscovery.Create(viewModel);

        foreach (var template in _templates.GetOrAdd(viewModel.GetType(), Scan))
        {
            if (template.Command.GetValue(viewModel) is not ICommand command)
                throw new InvalidOperationException(
                    $"[PageAction] on {viewModel.GetType().Name}: '{template.Command.Name}' is null when the page is prepared.");

            var attribute = template.Attribute;
            // Svg before Role, so a role keeps an icon the attribute names; the attribute's placement already
            // falls back to the role's slot
            var action = command is IAsyncRelayCommand asyncCommand
                ? new PageAction(attribute.Text, asyncCommand) { Svg = attribute.Svg, Role = attribute.Role, Placement = attribute.Placement }
                : new PageAction(attribute.Text, command) { Svg = attribute.Svg, Role = attribute.Role, Placement = attribute.Placement };

            action.Badge = attribute.Badge;
            action.IsVisible = attribute.IsVisible;
            action.Haptic = attribute.Haptic;

            viewModel.PageActions.Add(action);
        }
    }

    private static Template[] Scan(Type type)
    {
        var found = new List<(int Order, Template Template)>();

        foreach (var member in type.GetMembers(MemberFlags))
        {
            if (member.GetCustomAttribute<PageActionAttribute>() is not { } attribute)
                continue;

            var commandName = attribute.Command ?? member switch
            {
                PropertyInfo property when IsCommand(property) => property.Name,
                MethodInfo method => ToolkitNames.CommandFor(method.Name),
                _ => member.Name,
            };

            var command = type.GetProperty(commandName, MemberFlags);
            if (command is null || !IsCommand(command))
                throw new InvalidOperationException(
                    $"[PageAction] on {type.Name}.{member.Name} needs a command property named '{commandName}'. " +
                    "Put the attribute on a [RelayCommand] method or an ICommand property, or set Command to the property's name.");

            found.Add((attribute.Order, new Template(attribute, command)));
        }

        return found.OrderBy(f => f.Order).Select(f => f.Template).ToArray();
    }

    private static bool IsCommand(PropertyInfo property) => typeof(ICommand).IsAssignableFrom(property.PropertyType);
}
