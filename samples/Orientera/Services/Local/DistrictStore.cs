using System.Text.Json;

namespace Orientera.Services.Local;

/// <summary>
/// Which districts the user wants to see competitions from.
/// </summary>
/// <remarks>
/// A standing preference, like the class choice: someone who lives on the border between
/// Gästrikland and Hälsingland lives there next week too. Kept on the phone, because it says
/// something about where a person lives and nobody else needs to know it.
///
/// The search box and the time range are deliberately <em>not</em> saved. A forgotten search that
/// hides the whole calendar at next launch is a bug the user cannot see the cause of.
/// </remarks>
public sealed class DistrictStore(string _path)
{
    private HashSet<string>? _districts;

    public IReadOnlySet<string> Load()
    {
        if (_districts is not null)
            return _districts;

        try
        {
            _districts = File.Exists(_path)
                ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            _districts = [];
        }

        return _districts;
    }

    public void Save(IReadOnlySet<string> districts)
    {
        _districts = [.. districts];

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_districts));
        }
        catch (IOException)
        {
            // A preference that cannot be written is a preference that does not survive the
            // session. It is not worth failing the filter over.
        }
    }
}
