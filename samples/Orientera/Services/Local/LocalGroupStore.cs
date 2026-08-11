using System.Text.Json;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Services.Local;

/// <summary>
/// Who the user follows, on this phone only.
/// </summary>
/// <remarks>
/// Min grupp is local by principle: it works without an account and without coverage, and nobody
/// else needs to know who someone watches. Against a real backend it also has to start
/// <em>empty</em> — the three names the demo dataset seeds are part of the demo, and a real
/// runner opening the app to find three strangers they never chose is the app inventing a
/// social graph.
///
/// The whole person is stored, not a reference. There is nothing to resolve an id against later:
/// the person came out of a result list, and that list will fall out of the calendar window.
/// </remarks>
public sealed class LocalGroupStore(string _path)
{
    private List<FollowedPerson>? _group;

    public IReadOnlyList<FollowedPerson> All() => [.. Load()];

    public void Follow(Person person, FollowReason reason)
    {
        var group = Load();

        if (group.Any(f => f.Person.Id == person.Id))
            return;

        group.Add(new FollowedPerson { Person = person, Reason = reason });
        Save(group);
    }

    public void Unfollow(PersonId person)
    {
        var group = Load();

        if (group.RemoveAll(f => f.Person.Id == person) > 0)
            Save(group);
    }

    private void Save(List<FollowedPerson> group) =>
        File.WriteAllText(_path, JsonSerializer.Serialize(group, OrienteraJson.Options));

    private List<FollowedPerson> Load()
    {
        if (_group is not null)
            return _group;

        try
        {
            if (File.Exists(_path))
                _group = JsonSerializer.Deserialize<List<FollowedPerson>>(
                    File.ReadAllText(_path), OrienteraJson.Options);
        }
        catch (Exception)
        {
            // A group that will not deserialise is better started over than crashed on.
            _group = null;
        }

        return _group ??= [];
    }
}
