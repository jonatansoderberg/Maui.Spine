using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Turns a <see cref="PageSearchAttribute"/> on a view model into a <see cref="PageSearch"/> whose
/// text follows the declared property both ways.
/// </summary>
internal static class PageSearchDiscovery
{
    private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed record Template(PageSearchAttribute Attribute, PropertyInfo Text, PropertyInfo? Submit);

    private static readonly ConcurrentDictionary<Type, Template?> _templates = new();

    /// <summary>
    /// Creates the search the view model declares, bound to its property, or returns
    /// <see langword="null"/> when it declares none.
    /// </summary>
    public static PageSearch? Create(INotifyPropertyChanged viewModel)
    {
        if (_templates.GetOrAdd(viewModel.GetType(), Scan) is not { } template)
            return null;

        ICommand? submit = null;
        if (template.Submit is { } submitProperty && (submit = submitProperty.GetValue(viewModel) as ICommand) is null)
            throw new InvalidOperationException(
                $"[PageSearch] on {viewModel.GetType().Name}: the Submit command '{submitProperty.Name}' is null when the page is prepared.");

        var property = template.Text;
        var search = new PageSearch
        {
            Text = Read(),
            Placeholder = template.Attribute.Placeholder,
            Placement = template.Attribute.Placement,
            SubmitCommand = submit,
        };

        viewModel.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == property.Name)
                search.Text = Read();
        };

        search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageSearch.Text) && !string.Equals(Read(), search.Text ?? "", StringComparison.Ordinal))
                property.SetValue(viewModel, search.Text ?? "");
        };

        return search;

        string Read() => property.GetValue(viewModel) as string ?? "";
    }

    private static Template? Scan(Type type)
    {
        Template? found = null;

        foreach (var member in type.GetMembers(MemberFlags))
        {
            if (member.GetCustomAttribute<PageSearchAttribute>() is not { } attribute)
                continue;

            if (found is not null && found.Text.Name != NameOf(member))
                throw new InvalidOperationException(
                    $"[PageSearch] on {type.Name}: declared on both '{found.Text.Name}' and '{member.Name}'; a page has one search field.");

            var name = NameOf(member);
            var text = type.GetProperty(name, MemberFlags);
            if (text is null || text.PropertyType != typeof(string) || text.GetSetMethod(nonPublic: true) is null)
                throw new InvalidOperationException(
                    $"[PageSearch] on {type.Name}.{member.Name} needs a string property named '{name}' with a setter.");

            found = new Template(attribute, text, attribute.Submit is { } submit ? SubmitCommand(type, submit) : null);
        }

        return found;
    }

    // The toolkit generates the property from a field; the attribute stays on the field.
    private static string NameOf(MemberInfo member) =>
        member is FieldInfo field ? ToolkitNames.PropertyFor(field.Name) : member.Name;

    private static PropertyInfo SubmitCommand(Type type, string name)
    {
        if (type.GetProperty(name, MemberFlags) is { } property && IsCommand(property))
            return property;

        if (type.GetMethod(name, MemberFlags) is not null
            && type.GetProperty(ToolkitNames.CommandFor(name), MemberFlags) is { } generated && IsCommand(generated))
            return generated;

        throw new InvalidOperationException(
            $"[PageSearch] on {type.Name}: Submit names '{name}', which is neither an ICommand property nor a [RelayCommand] method.");
    }

    private static bool IsCommand(PropertyInfo property) => typeof(ICommand).IsAssignableFrom(property.PropertyType);
}
