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
    private RacePreferences? _preferences;

    public RacePreferences Load()
    {
        if (_preferences is not null)
            return _preferences;

        try
        {
            _preferences = File.Exists(_path)
                ? JsonSerializer.Deserialize<RacePreferences>(File.ReadAllText(_path), OrienteraJson.Options)
                  ?? RacePreferences.None
                : RacePreferences.None;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _preferences = RacePreferences.None;
        }

        return _preferences;
    }

    public void Save(RacePreferences preferences)
    {
        _preferences = preferences;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(preferences, OrienteraJson.Options));
        }
        catch (IOException)
        {
            // A preference that cannot be written is one that does not survive the session. It is
            // not worth failing the calendar over.
        }
    }
}
