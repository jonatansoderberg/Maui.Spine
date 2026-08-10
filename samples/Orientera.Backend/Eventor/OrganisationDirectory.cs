using System.Xml.Linq;

namespace Orientera.Backend.Eventor;

/// <summary>
/// Eventor's clubs and the districts above them. An event only carries the organiser's id, so
/// the club name and the district — which the relevance engine weighs geographically — come
/// from here.
/// </summary>
public sealed class OrganisationDirectory
{
    private readonly Dictionary<string, Entry> _byId;

    private OrganisationDirectory(Dictionary<string, Entry> byId) => _byId = byId;

    public static OrganisationDirectory Empty { get; } = new([]);

    public static OrganisationDirectory From(XElement organisationList)
    {
        var entries = new Dictionary<string, Entry>();

        foreach (var organisation in organisationList.Deep("Organisation"))
        {
            if (organisation.Text("OrganisationId") is not { } id)
                continue;

            var name = organisation.Text("Name") ?? id;
            var parent = organisation.Child("ParentOrganisation").Text("OrganisationId");

            // Every organisation carries its country; the Swedish instance lists foreign clubs
            // too, so this is what makes "Swedish competitions only" a fact rather than a guess.
            var country = organisation.Child("Country").Child("Alpha3").Attr("value");

            entries[id] = new Entry(name, parent, country);
        }

        return new OrganisationDirectory(entries);
    }

    /// <summary>
    /// False only when the organisation is known to be somewhere else. An organiser we have no
    /// record of is not evidence of a foreign competition.
    /// </summary>
    public bool IsSwedish(string? id) =>
        id is null || !_byId.TryGetValue(id, out var entry) || entry.Country is null or "SWE";

    public string NameOf(string? id) =>
        id is not null && _byId.TryGetValue(id, out var entry) ? entry.Name : string.Empty;

    /// <summary>The district (distriktsförbund) the club belongs to, as a place name.</summary>
    public string DistrictOf(string? id)
    {
        if (id is null || !_byId.TryGetValue(id, out var entry) || entry.ParentId is null)
            return string.Empty;

        return _byId.TryGetValue(entry.ParentId, out var parent) ? ShortDistrict(parent.Name) : string.Empty;
    }

    /// <summary>"Gästriklands Orienteringsförbund" is a district's name; "Gästrikland" is its place.</summary>
    private static string ShortDistrict(string name)
    {
        var trimmed = name;

        foreach (var suffix in (string[])[" Orienteringsförbund", " orienteringsförbund", " OF", "s OF"])
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
            {
                trimmed = trimmed[..^suffix.Length];
                break;
            }
        }

        return trimmed.EndsWith('s') ? trimmed[..^1] : trimmed;
    }

    private readonly record struct Entry(string Name, string? ParentId, string? Country);
}
