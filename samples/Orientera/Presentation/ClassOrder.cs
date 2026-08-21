using System.Text.RegularExpressions;

namespace Orientera.Presentation;

/// <summary>
/// The order a list of classes is read in.
/// </summary>
/// <remarks>
/// Three groups, in the order a runner scans a result list: the main classes, then the youth
/// classes, then the open courses. Inside a group the organiser's own order applies — it is the
/// one on the entry form and the one Eventor shows the class list in, and it is what keeps D21
/// next to H21 instead of eleven D-classes followed by eleven H-classes. What their list does not
/// name is sorted by what a class name is made of: its letters first, then its number as a number,
/// so that D21 sits between D18 and D35 rather than between D2 and D3.
/// <para>
/// Alphabetical was none of that, and to a runner it read as no order at all: "Blå 3,0" above D10,
/// D2 nowhere near D21, the open courses scattered through the age classes.
/// </para>
/// </remarks>
public sealed partial class ClassOrder
{
    private readonly Dictionary<string, int> _listed;

    private ClassOrder(IEnumerable<string> classes)
    {
        _listed = new(StringComparer.OrdinalIgnoreCase);

        foreach (var name in classes)
            _listed.TryAdd(Bare(name), _listed.Count);
    }

    /// <summary>Reads the organiser's own class list. An empty one leaves only the fallback.</summary>
    public static ClassOrder For(IEnumerable<string>? classes) => new(classes ?? []);

    private const int Main = 0;
    private const int Youth = 1;
    private const int Open = 2;

    /// <summary>The oldest a class can be and still be a youth class.</summary>
    private const int YouthAge = 20;

    /// <summary>
    /// Where a class belongs. Sorts as a tuple: the group first, then the organiser's position
    /// inside it, and for anything their list does not name, the letters and the number the name
    /// is built from.
    /// </summary>
    public (int Group, int Listed, string Letters, int Number, string Tail) Rank(string className)
    {
        var name = Bare(className);

        int listed = _listed.TryGetValue(name, out int position) ? position : int.MaxValue;

        var match = PartsPattern().Match(name);

        if (!match.Success)
            return (Open, listed, name.ToLowerInvariant(), 0, string.Empty);

        var letters = match.Groups["letters"].Value.Trim().ToLowerInvariant();
        int number = int.TryParse(match.Groups["number"].Value, out int parsed) ? parsed : 0;

        return (Group(letters, number), listed, letters, number, match.Groups["tail"].Value.ToLowerInvariant());
    }

    /// <summary>
    /// Which of the three a class name describes.
    /// </summary>
    /// <remarks>
    /// An age class is a letter and an age — D21, H45, HD12 — and the age is what separates the
    /// youth from the rest. Everything else is a course anyone may enter, whether it is called
    /// "Öppen 5", "Blå 3,0" or "Gubbar", and those come last. A U-class is a youth course and
    /// belongs with the youth.
    /// </remarks>
    private static int Group(string letters, int number) => letters switch
    {
        "d" or "h" or "hd" or "dh" when number > 0 => number <= YouthAge ? Youth : Main,
        "u" when number > 0 => Youth,
        _ => Open,
    };

    /// <summary>
    /// The class without the race a multi-day event appends to it. "H45, Etapp 3" is H45 five
    /// times over, and the event's class list knows nothing of the etapp.
    /// </summary>
    private static string Bare(string className) => className.Split(',')[0].Trim();

    [GeneratedRegex(@"^(?<letters>\D*)(?<number>\d+)?(?<tail>.*)$")]
    private static partial Regex PartsPattern();
}
