namespace Plugin.Maui.Spine.Svg;

/// <summary>Picks the manifest resource a short SVG file name stands for.</summary>
internal static class SvgResourceMatch
{
    /// <summary>
    /// The resource whose name is <paramref name="fileName"/>, or ends with it after a <c>.</c>
    /// (the separator between a resource's folders and its file name), or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// A bare suffix match let <c>lock.svg</c> resolve to <c>Clock.svg</c>, <c>SmartClock.svg</c> or
    /// <c>Unlock.svg</c>, whichever the dictionary listed first. When several resources still match
    /// (the same file name in two assemblies or folders), the shortest name wins, so the choice is
    /// stable, and <paramref name="ambiguous"/> is set so the caller can report it.
    /// </remarks>
    public static string? Find(IEnumerable<string> resourceNames, string fileName, out bool ambiguous)
    {
        string? best = null;
        ambiguous = false;

        foreach (var name in resourceNames)
        {
            if (!IsMatch(name, fileName))
                continue;

            if (best is null)
            {
                best = name;
                continue;
            }

            ambiguous = true;
            if (name.Length < best.Length || (name.Length == best.Length && string.CompareOrdinal(name, best) < 0))
                best = name;
        }

        return best;
    }

    public static bool IsMatch(string resourceName, string fileName) =>
        resourceName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)
        && (resourceName.Length == fileName.Length || resourceName[resourceName.Length - fileName.Length - 1] == '.');
}
