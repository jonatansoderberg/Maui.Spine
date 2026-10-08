namespace Plugin.Maui.Spine.Services;

/// <summary>The names CommunityToolkit.Mvvm's generators give the members they write.</summary>
internal static class ToolkitNames
{
    /// <summary>A <c>[RelayCommand]</c> method's command property: drop a trailing "Async", append "Command".</summary>
    public static string CommandFor(string methodName)
    {
        var name = methodName.EndsWith("Async", StringComparison.Ordinal) ? methodName[..^5] : methodName;
        return name + "Command";
    }

    /// <summary>An <c>[ObservableProperty]</c> field's property: drop a leading "_" or "m_", capitalise the first letter.</summary>
    public static string PropertyFor(string fieldName)
    {
        var name = fieldName.StartsWith("m_", StringComparison.Ordinal) ? fieldName[2..] : fieldName.TrimStart('_');
        return name.Length == 0 ? fieldName : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
