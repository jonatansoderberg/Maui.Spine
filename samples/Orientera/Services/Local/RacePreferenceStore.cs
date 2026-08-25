using System.Text.Json;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Services.Local;

/// <summary>
/// What the runner does, and what they would rather be doing.
/// </summary>
/// <param name="Sports">
/// The sports worth showing at all. Empty means every one of them — a preference nobody has set
/// must not hide anything, the same rule the district filter follows.
/// </param>
/// <param name="Favourites">
/// The kinds of race they would rather be at, best first. Ordered, because the answer to "what do
/// you like" is rarely one thing and never an equal list of five.
/// </param>
public sealed record RacePreferences(
    IReadOnlySet<Sport> Sports,
    IReadOnlyList<RacePreference> Favourites)
{
    public static RacePreferences None { get; } = new(new HashSet<Sport>(), []);

    /// <summary>Whether a competition is in a sport the runner has said they do.</summary>
    public bool Allows(Sport sport) => Sports.Count == 0 || Sports.Contains(sport);
}

/// <summary>
/// The standing answer to which sports the runner does and which races they prefer.
/// </summary>
/// <remarks>
/// A preference and not a filter: it survives the app, applies before anything the filter sheet
/// says, and never appears as a removable chip above the list. Someone who does not own a bike
/// does not own one next week either.
/// <para>
/// On the phone, like the district choice. It says what a person spends their weekends on, and
/// nobody else needs to know it.
/// </para>
/// </remarks>
public sealed class RacePreferenceStore(string _path)
{
    /// <summary>
    /// The file's own shape, in types the serializer can build.
    /// </summary>
    /// <remarks>
    /// <see cref="RacePreferences"/> exposes <c>IReadOnlySet</c> because that is what a caller
    /// should get, and <c>System.Text.Json</c> cannot instantiate one: reading the file straight
    /// into the record threw <c>NotSupportedException</c> on the first cold start after anything
    /// had been saved — past the catch, and on the launch path.
    /// </remarks>
    private sealed record Stored(HashSet<Sport> Sports, List<RacePreference> Favourites);

    private RacePreferences? _preferences;

    public RacePreferences Load()
    {
        if (_preferences is not null)
            return _preferences;

        try
        {
            _preferences = File.Exists(_path)
                && JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path), OrienteraJson.Options)
                   is { } stored
                ? new RacePreferences(stored.Sports, stored.Favourites)
                : RacePreferences.None;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _preferences = RacePreferences.None;
        }

        _preferences = Normalised(_preferences);

        return _preferences;
    }

    /// <summary>
    /// Old answers, read the way this version means them.
    /// </summary>
    /// <remarks>
    /// A favourite saved as "Indoor sprint" before indoor was known to have no distances is
    /// simply "Indoor" now. Left alone it would sit on the list and be impossible to take off —
    /// the grid it would have to be tapped out of no longer offers it — and the first tap on any
    /// other chip would have deleted it without a word. Duplicates that collapse into the same
    /// answer keep the earliest place, because that is the one the reader arranged.
    /// </remarks>
    private static RacePreferences Normalised(RacePreferences preferences)
    {
        var favourites = new List<RacePreference>();

        foreach (var favourite in preferences.Favourites)
        {
            var settled = SportDistances.HasDistances(favourite.Sport)
                ? favourite
                : new RacePreference(favourite.Sport);

            if (!favourites.Contains(settled))
                favourites.Add(settled);
        }

        return favourites.Count == preferences.Favourites.Count
               && favourites.SequenceEqual(preferences.Favourites)
            ? preferences
            : preferences with { Favourites = favourites };
    }

    public void Save(RacePreferences preferences)
    {
        _preferences = preferences;

        try
        {
            var stored = new Stored([.. preferences.Sports], [.. preferences.Favourites]);

            File.WriteAllText(_path, JsonSerializer.Serialize(stored, OrienteraJson.Options));
        }
        catch (IOException)
        {
            // A preference that cannot be written is one that does not survive the session. It is
            // not worth failing the calendar over.
        }
    }
}
