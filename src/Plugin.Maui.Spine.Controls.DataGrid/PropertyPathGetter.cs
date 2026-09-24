using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A cached reflection getter for a property path ("Name" or "Customer.Name"). The property chain is
/// resolved from the first item it sees and reused, so the rows are expected to share one type.
/// </summary>
internal sealed class PropertyPathGetter(string path)
{
    private Type? _type;
    private PropertyInfo[]? _chain;

    public string Path { get; } = path;

    [RequiresUnreferencedCode(DataGrid.TrimmingMessage)]
    public object? GetValue(object? item)
    {
        if (item is null || string.IsNullOrEmpty(Path))
            return null;

        if (Path == ".")
            return item;

        var type = item.GetType();
        if (_chain is null || _type != type)
        {
            _chain = Resolve(type);
            _type = type;
        }

        if (_chain.Length == 0)
            return null;

        object? value = item;
        foreach (var property in _chain)
        {
            if (value is null)
                return null;
            value = property.GetValue(value);
        }
        return value;
    }

    [RequiresUnreferencedCode(DataGrid.TrimmingMessage)]
    private PropertyInfo[] Resolve(Type type)
    {
        var parts = Path.Split('.');
        var chain = new PropertyInfo[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (type.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance) is not { } property)
                return [];
            chain[i] = property;
            type = property.PropertyType;
        }
        return chain;
    }
}
