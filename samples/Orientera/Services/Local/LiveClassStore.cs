using System.Text.Json;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Services.Local;

/// <summary>
/// Which class the user was watching in each competition's live list.
/// </summary>
/// <remarks>
/// A competition has forty classes and a runner follows one or two of them. Remembering the
/// choice is what makes the live tab open where it was left rather than at a picker — and like
/// identity and favourites it stays on the phone, because nobody else needs to know.
/// </remarks>
public sealed class LiveClassStore(string _path)
{
    private Dictionary<string, string>? _byCompetition;

    public string? For(CompetitionId competition) =>
        Load().TryGetValue(competition.Value, out var className) ? className : null;

    public void Save(CompetitionId competition, string className)
    {
        var current = Load();
        current[competition.Value] = className;

        File.WriteAllText(_path, JsonSerializer.Serialize(current, OrienteraJson.Options));
    }

    private Dictionary<string, string> Load()
    {
        if (_byCompetition is not null)
            return _byCompetition;

        try
        {
            if (File.Exists(_path))
                _byCompetition = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(_path), OrienteraJson.Options);
        }
        catch (Exception)
        {
            // A remembered class that will not deserialise costs one tap to set again.
            _byCompetition = null;
        }

        return _byCompetition ??= [];
    }
}
