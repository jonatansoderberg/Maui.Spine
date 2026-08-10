using System.Globalization;
using System.Text;

namespace Orientera.Domain;

/// <summary>
/// A runner as the live source knows them: a name and a club, no id. Everything that has to
/// answer "is this me, or someone in Min grupp?" against such a list goes through here, so the
/// answer is the same everywhere.
/// </summary>
/// <remarks>
/// Deliberately local. LiveResults carries no person id, and the alternative — sending my name
/// and my group's names to a server so it can mark the rows — would move personal data out of
/// the phone for no gain. See spike SP-04.
/// </remarks>
public readonly record struct RunnerIdentity
{
    private RunnerIdentity(string name, string club)
    {
        Name = name;
        Club = club;
    }

    /// <summary>Normalised name; the identity's primary key.</summary>
    public string Name { get; }

    /// <summary>Normalised club, or empty when the source did not say.</summary>
    public string Club { get; }

    public string Key => Club.Length > 0 ? $"{Name}|{Club}" : Name;

    public static RunnerIdentity Of(string? name, string? club = null) =>
        new(Normalise(Reorder(name)), Normalise(club));

    /// <summary>
    /// The fold two sources have to agree on: case, diacritics, punctuation and spacing
    /// removed. Also what competition and organiser names are compared through when Eventor
    /// and LiveResults are matched against each other (SP-04).
    /// </summary>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var folded = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(folded.Length);
        bool pendingSpace = false;

        foreach (char c in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(c) || c == '-')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (!char.IsLetterOrDigit(c))
                continue;

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Names decide, the club only separates namesakes: two sources rarely write a club the
    /// same way ("Gävle OK", "Gävle Orienteringsklubb"), so an unknown or differing club must
    /// not by itself break a match — but two runners with the same name must not be merged.
    /// </summary>
    public bool Matches(RunnerIdentity other)
    {
        if (Name.Length == 0 || Name != other.Name)
            return false;

        return Club.Length == 0 || other.Club.Length == 0 || Club == other.Club;
    }

    public override string ToString() => Key;

    /// <summary>Result lists write "Efternamn, Förnamn"; start lists write "Förnamn Efternamn".</summary>
    private static string? Reorder(string? name)
    {
        if (name is null || !name.Contains(','))
            return name;

        var parts = name.Split(',', 2);
        return $"{parts[1].Trim()} {parts[0].Trim()}";
    }
}
