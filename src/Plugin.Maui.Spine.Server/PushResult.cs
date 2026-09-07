using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>What happened to one installation.</summary>
public enum PushStatus
{
    /// <summary>The service accepted it.</summary>
    Sent,

    /// <summary>The token is dead and the registration has been removed.</summary>
    Invalid,

    /// <summary>The service asked us to slow down. Worth retrying later.</summary>
    Throttled,

    /// <summary>Something else went wrong; see the reason.</summary>
    Failed,
}

/// <summary>The outcome for one installation.</summary>
/// <param name="InstallationId">Which registration this is about.</param>
/// <param name="Platform">Which service it went to.</param>
/// <param name="Status">How it went.</param>
/// <param name="Reason">The service's own words, when there are any.</param>
public readonly record struct PushDelivery(
    string InstallationId,
    PushPlatform Platform,
    PushStatus Status,
    string? Reason = null);

/// <summary>
/// The outcome of one send, per installation. This is the telemetry a hosted service would charge
/// for; here it is the return value.
/// </summary>
public sealed record PushResult
{
    /// <summary>A result with nothing in it, for a target that matched no installation.</summary>
    public static PushResult Empty { get; } = new() { Deliveries = [] };

    /// <summary>One entry per installation the send touched.</summary>
    public required IReadOnlyList<PushDelivery> Deliveries { get; init; }

    /// <summary>How many the services accepted.</summary>
    public int Sent => Count(PushStatus.Sent);

    /// <summary>How many had a dead token, and were removed from the register.</summary>
    public int Invalid => Count(PushStatus.Invalid);

    /// <summary>How many were throttled.</summary>
    public int Throttled => Count(PushStatus.Throttled);

    /// <summary>How many failed for another reason.</summary>
    public int Failed => Count(PushStatus.Failed);

    /// <summary>Whether every installation the target matched was accepted.</summary>
    public bool AllSent => Deliveries.All(d => d.Status == PushStatus.Sent);

    /// <summary>Combines the results of several sends, typically one per platform.</summary>
    /// <param name="results">The results to merge.</param>
    /// <returns>One result holding every delivery.</returns>
    public static PushResult Merge(IEnumerable<PushResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        return new PushResult { Deliveries = results.SelectMany(r => r.Deliveries).ToList() };
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Deliveries.Count} delivered: {Sent} sent, {Invalid} invalid, {Throttled} throttled, {Failed} failed";

    private int Count(PushStatus status)
    {
        var n = 0;
        foreach (var d in Deliveries) if (d.Status == status) n++;
        return n;
    }
}
