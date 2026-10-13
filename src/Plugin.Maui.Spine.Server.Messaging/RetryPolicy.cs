namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>
/// How often a failing message is tried, and how long it waits in between: the delay doubles from
/// <paramref name="FirstDelay"/> up to <paramref name="MaxDelay"/>, with a fifth either way of jitter so that a burst of
/// failures does not come back as a burst.
/// </summary>
/// <param name="MaxAttempts">Attempts in all, the first included.</param>
/// <param name="FirstDelay">The wait after the first failure.</param>
/// <param name="MaxDelay">The longest wait.</param>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan FirstDelay, TimeSpan MaxDelay)
{
    /// <summary>Five attempts, from 10 seconds up to 10 minutes apart.</summary>
    public static RetryPolicy Default { get; } = new(5, TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10));

    /// <summary>The wait after attempt <paramref name="attempt"/> failed.</summary>
    /// <param name="attempt">The attempt that failed, from 1.</param>
    public TimeSpan DelayAfter(int attempt)
    {
        var doubled = FirstDelay.TotalMilliseconds * Math.Pow(2, Math.Clamp(attempt - 1, 0, 30));
        var capped = Math.Min(doubled, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    internal void Validate(string subscriber)
    {
        if (MaxAttempts < 1)
            throw new InvalidOperationException($"Subscriber '{subscriber}': a retry policy needs at least one attempt.");
        if (FirstDelay < TimeSpan.Zero || MaxDelay < FirstDelay)
            throw new InvalidOperationException($"Subscriber '{subscriber}': the retry delays must be positive, and the longest at least the first.");
    }
}
