namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Puts a navigable page in the platform's search as a fixed entry: Spine indexes it at startup and
/// opens the page, without a parameter, when the result is tapped. Take the attribute away and the
/// entry goes at the next start.
/// </summary>
/// <example>
/// <code>
/// [NavigableRegion(Title = "Theme")]
/// [Searchable(Description = "Light, dark or the system", Icon = "theme", Keywords = ["dark mode", "appearance"])]
/// public partial class ThemePage { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SearchableAttribute : Attribute
{
    /// <summary>What the result says. Defaults to the page's navigable <c>Title</c>, then its type name.</summary>
    public string? Title { get; set; }

    /// <summary>A second line under the title.</summary>
    public string? Description { get; set; }

    /// <summary>An SVG by name, as <see cref="SearchableItem.Icon"/>.</summary>
    public string? Icon { get; set; }

    /// <summary>More words the page is found by.</summary>
    public string[]? Keywords { get; set; }
}
