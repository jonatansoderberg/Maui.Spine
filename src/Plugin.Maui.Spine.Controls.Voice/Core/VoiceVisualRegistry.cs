namespace Plugin.Maui.Spine.Controls;

/// <summary>The visuals <c>VoiceView</c> knows by name: the built-ins and those the app adds through <c>UseSpineVoiceView</c>.</summary>
internal sealed class VoiceVisualRegistry
{
    /// <summary>The visual a <c>VoiceView</c> draws when it names none, or one that is not registered.</summary>
    public const string Default = "orb";

    private readonly Dictionary<string, Func<object>> _factories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orb"] = static () => new OrbVisual(),
        ["bars"] = static () => new BarsVisual(),
        ["ring"] = static () => new RingVisual(),
        ["avatar"] = static () => new AvatarVisual(),
    };

    /// <summary>Adds or replaces a canvas visual.</summary>
    internal void Add(string name, Func<VoiceVisual> factory) => Set(name, factory);

    internal void Set(string name, Func<object> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _factories[name] = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>A new visual for <paramref name="name"/>, or the default when no visual has that name.</summary>
    internal object Create(string? name, out bool known)
    {
        known = name is not null && _factories.ContainsKey(name);
        return _factories[known ? name! : Default]();
    }
}
