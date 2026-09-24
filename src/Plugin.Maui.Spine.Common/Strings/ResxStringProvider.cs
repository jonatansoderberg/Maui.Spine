using System.Collections;
using System.Globalization;
using System.Resources;

namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Strings from a <c>.resx</c> <see cref="ResourceManager"/>, so an app that already has one keeps it.
/// </summary>
public sealed class ResxStringProvider(ResourceManager resources) : IStringProvider
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string>? Load(CultureInfo culture)
    {
        if (resources.GetResourceSet(culture, true, false) is not { } set)
            return null;

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in set)
        {
            if (entry.Key is string key && entry.Value is string value)
                strings[key] = value;
        }

        return strings;
    }
}
