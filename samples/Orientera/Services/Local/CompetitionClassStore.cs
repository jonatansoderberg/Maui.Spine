using System.Text.Json;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Services.Local;

/// <summary>
/// Which class is the user's in each competition.
/// </summary>
/// <remarks>
/// A competition has forty classes and a runner cares about one or two. Remembering the choice
/// is what makes the live tab open where it was left rather than at a picker, and what makes the
/// competition page keep the class the runner picked there. It is one question with one answer,
/// so it is one store: picking a class on the competition page is picking the one live opens in.
///
/// Like identity and interests it stays on the phone, because nobody else needs to know. The
/// file is still called live-classes.json — the name is where it started, and renaming it would
/// throw away the choices already saved on people's phones.
/// </remarks>
public sealed class CompetitionClassStore(string _path)
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
