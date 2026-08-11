namespace Orientera.Backend.Configuration;

/// <summary>Where Livelox lives, and the key that identifies us to it.</summary>
public sealed class LiveloxOptions
{
    public const string Section = "Livelox";

    public string BaseAddress { get; set; } = "https://api.livelox.com/";

    /// <summary>
    /// Optional for the event lookup, which answers without one. It is sent anyway: it is how
    /// Livelox knows who is calling, and the scoped endpoints need it.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// A published event does not change. The link is worth remembering for a day; the 404 for a
    /// competition Livelox has never heard of is worth remembering just as long.
    /// </summary>
    public int CacheHours { get; set; } = 24;
}
