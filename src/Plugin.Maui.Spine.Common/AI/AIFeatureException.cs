namespace Plugin.Maui.Spine.AI;

/// <summary>Why a feature does not answer.</summary>
public enum AIOffReason
{
    /// <summary>There is no such feature, its provider has no factory or key, or it has no model.</summary>
    NotConfigured,

    /// <summary><c>Enabled</c> is <see langword="false"/>.</summary>
    Disabled,

    /// <summary>A hard budget is spent: the app's, the feature's or the caller's.</summary>
    Budget,

    /// <summary>The feature answers only signed-in users.</summary>
    SignInRequired,
}

/// <summary>
/// A failure that means there is nothing to show rather than that something broke. Spine's <c>TaskState</c>
/// shows it as its empty state, not as an error with a retry.
/// </summary>
public interface IEmptyResultException;

/// <summary>
/// A named AI feature refused a call. The same types are thrown on the server (<c>Plugin.Maui.Spine.Server.AI</c>)
/// and in the app (<c>Plugin.Maui.Spine.AI</c>), so both handle one shape.
/// </summary>
/// <param name="feature">The feature that refused.</param>
/// <param name="message">What happened.</param>
public abstract class AIFeatureException(string feature, string message) : Exception(message)
{
    /// <summary>The feature that refused.</summary>
    public string Feature { get; } = feature;
}

/// <summary>The feature is off for this call: not configured, disabled, out of budget, or for signed-in users only.</summary>
/// <param name="feature">The feature.</param>
/// <param name="reason">Why.</param>
public sealed class AIFeatureOffException(string feature, AIOffReason reason)
    : AIFeatureException(feature, $"The AI feature '{feature}' is off: {reason}."), IEmptyResultException
{
    /// <summary>Why the feature is off.</summary>
    public AIOffReason Reason { get; } = reason;
}

/// <summary>The caller has made too many calls, or has too many running.</summary>
/// <param name="feature">The feature.</param>
/// <param name="retryAfter">When a new call may succeed, when it is known.</param>
public sealed class AIRateLimitedException(string feature, TimeSpan? retryAfter)
    : AIFeatureException(feature, $"Too many calls to the AI feature '{feature}'.")
{
    /// <summary>When a new call may succeed, when it is known.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>An exposed feature refused a conversation that is too long.</summary>
/// <param name="feature">The feature.</param>
/// <param name="limit"><c>turns</c> (<c>MaxTurns</c>) or <c>tokens</c> (<c>MaxInputTokens</c>).</param>
/// <param name="max">The feature's limit.</param>
public sealed class AIRequestTooLargeException(string feature, string limit, int max)
    : AIFeatureException(feature, $"The conversation is too long for the AI feature '{feature}': more {limit} than {max}.")
{
    /// <summary><c>turns</c> or <c>tokens</c>.</summary>
    public string Limit { get; } = limit;

    /// <summary>The feature's limit.</summary>
    public int Max { get; } = max;
}

/// <summary>The words app and server use for <see cref="AIOffReason"/> in JSON (<c>{ "off": "budget" }</c>).</summary>
internal static class AIOffReasons
{
    public static string ToJson(AIOffReason reason) => reason switch
    {
        AIOffReason.NotConfigured => "notConfigured",
        AIOffReason.Disabled => "disabled",
        AIOffReason.Budget => "budget",
        _ => "signInRequired",
    };

    public static AIOffReason? FromJson(string? text) => text switch
    {
        "notConfigured" => AIOffReason.NotConfigured,
        "disabled" => AIOffReason.Disabled,
        "budget" => AIOffReason.Budget,
        "signInRequired" => AIOffReason.SignInRequired,
        _ => null,
    };
}
