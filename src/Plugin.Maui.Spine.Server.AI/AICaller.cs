namespace Plugin.Maui.Spine.Server.AI;

/// <summary>How much a caller's id can be trusted.</summary>
public enum AICallerKind
{
    /// <summary>A user the server authenticated. The id is the <c>sub</c> claim.</summary>
    SignedIn,

    /// <summary>
    /// An app installation, from the <c>Spine-Installation</c> header. It is a claim, not an identity: a client
    /// that rotates it gets a fresh allowance, so the feature and app budgets are what protect the bill.
    /// </summary>
    Installation,

    /// <summary>Neither. The id is the remote address.</summary>
    Anonymous,

    /// <summary>The server's own work, with no user behind it. No per-caller limits apply.</summary>
    System,
}

/// <summary>Who a call is made for. Budgets, rate limits and the usage report go by it.</summary>
/// <param name="Id">The id within its kind: a user id, an installation id or an address.</param>
/// <param name="Kind">How far the id can be trusted.</param>
public sealed record AICaller(string Id, AICallerKind Kind)
{
    /// <summary>The header an app sends its installation id in.</summary>
    public const string InstallationHeader = "Spine-Installation";

    /// <summary>The server's own work.</summary>
    public static AICaller System { get; } = new("system", AICallerKind.System);

    /// <summary>A user the server authenticated.</summary>
    /// <param name="userId">The user's id, usually the <c>sub</c> claim.</param>
    public static AICaller SignedIn(string userId) => new(userId, AICallerKind.SignedIn);

    /// <summary>An app installation.</summary>
    /// <param name="installationId">The id the app sent.</param>
    public static AICaller Installation(string installationId) => new(installationId, AICallerKind.Installation);

    /// <summary>A caller known only by address.</summary>
    /// <param name="address">The remote address, or <see langword="null"/> when there is none.</param>
    public static AICaller Anonymous(string? address) => new(address ?? "unknown", AICallerKind.Anonymous);

    /// <summary>
    /// The id with its kind in front, as the usage store and the report know it: <c>user:…</c>,
    /// <c>installation:…</c>, <c>ip:…</c> or <c>system</c>. Two kinds can never share an allowance.
    /// </summary>
    public string Key => Kind switch
    {
        AICallerKind.SignedIn => $"user:{Id}",
        AICallerKind.Installation => $"installation:{Id}",
        AICallerKind.Anonymous => $"ip:{Id}",
        _ => "system",
    };

    /// <inheritdoc />
    public override string ToString() => Key;
}
