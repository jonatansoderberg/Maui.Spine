using System.Text.Json;
using Orientera.Domain;
using Orientera.Services.Sources;

namespace Orientera.Services.Local;

/// <summary>Who the user says they are, in the terms the live source understands.</summary>
public sealed record LocalIdentity
{
    public required string Name { get; init; }
    public required string Club { get; init; }
    public required string DefaultClass { get; init; }

    public bool IsComplete => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Club);
}

/// <summary>
/// The app's answer to "who am I?" — a name, a club and a class, kept on the phone.
/// </summary>
/// <remarks>
/// Not an account. Eventor's login model is organisation-centred and belongs to M5; the live
/// and result sources identify a runner by name and club anyway, so that is all the app needs
/// to point at the right rows — and all it should hold.
/// </remarks>
public sealed class LocalIdentityStore(string _path)
{
    private LocalIdentity? _identity;
    private bool _loaded;

    public LocalIdentity? Current
    {
        get
        {
            if (_loaded)
                return _identity;

            _loaded = true;

            try
            {
                if (File.Exists(_path))
                    _identity = JsonSerializer.Deserialize<LocalIdentity>(File.ReadAllText(_path), OrienteraJson.Options);
            }
            catch (Exception)
            {
                // An identity that will not deserialise is one the user can set again in a
                // few seconds; failing every read forever is the worse outcome.
                _identity = null;
            }

            return _identity;
        }
    }

    public event EventHandler? Changed;

    public void Save(LocalIdentity identity)
    {
        _identity = identity;
        _loaded = true;

        File.WriteAllText(_path, JsonSerializer.Serialize(identity, OrienteraJson.Options));

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The identity as the domain's <see cref="Person"/>, or null when unset.</summary>
    public Person? AsPerson(Person? template = null) =>
        Current is { IsComplete: true } identity
            ? new Person
            {
                Id = new PersonId($"me:{RunnerIdentity.Of(identity.Name, identity.Club).Key}"),
                Name = identity.Name,
                Club = identity.Club,
                District = template?.District ?? string.Empty,
                DefaultClass = identity.DefaultClass,
                Home = template?.Home ?? default,
            }
            : null;
}
