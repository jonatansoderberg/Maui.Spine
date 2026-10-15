using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// The <c>Ai</c> section of configuration: features, prices and the app's budget. Bound again whenever the
/// section changes, so a model swap in App Configuration takes effect without a deploy.
/// </summary>
/// <remarks>Providers are not bound here. Their sections are handed to the factories as they are.</remarks>
public sealed class AIConfiguration
{
    /// <summary>The features by name. The name is what app code and the client ask for.</summary>
    public Dictionary<string, AIFeatureOptions> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prices by provider, then by model: <c>Ai:Prices:anthropic:&lt;model id&gt;</c>.</summary>
    public Dictionary<string, Dictionary<string, AIPrice>> Prices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The currency every price and budget is in. One per app.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>The app-wide budget, on top of each feature's own.</summary>
    public AIBudgetLimits? Budget { get; set; }

    /// <summary>
    /// The time zone a budget's day and month follow, as an IANA or Windows id. UTC when unset.
    /// </summary>
    public string? TimeZone { get; set; }
}

/// <summary>Whether a feature makes text, pictures or live voice sessions.</summary>
public enum AIFeatureKind
{
    /// <summary>An <see cref="IChatClient"/>.</summary>
    Chat,

    /// <summary>An <see cref="IImageGenerator"/>.</summary>
    Image,

    /// <summary>A realtime voice session the app connects to itself, set up by <see cref="IAIRealtimeProvider"/>.</summary>
    Realtime,
}

/// <summary>Who an exposed feature answers.</summary>
public enum AICallers
{
    /// <summary>Signed-in users, installations and anonymous callers, each with their own limits.</summary>
    Any,

    /// <summary>Only signed-in users. Installations and anonymous callers get 403.</summary>
    SignedIn,
}

/// <summary>One named feature: <c>Ai:Features:&lt;Name&gt;</c>.</summary>
public sealed class AIFeatureOptions
{
    /// <summary>Text or pictures.</summary>
    public AIFeatureKind Kind { get; set; } = AIFeatureKind.Chat;

    /// <summary>The provider's name under <c>Ai:Providers</c>, registered with <c>Chat(...)</c> or <c>Image(...)</c>.</summary>
    public string? Provider { get; set; }

    /// <summary>The provider's model id.</summary>
    public string? Model { get; set; }

    /// <summary><see langword="false"/> turns the feature off without removing it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The default cap on output tokens. It also bounds how far a call can overshoot a budget.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>The default temperature.</summary>
    public float? Temperature { get; set; }

    /// <summary>The default reasoning effort, for models that reason.</summary>
    public ReasoningEffort? Reasoning { get; set; }

    /// <summary>Size and count for an image feature.</summary>
    public AIImageOptions? Image { get; set; }

    /// <summary>The session a realtime feature sets up: voice, turn detection, transcription and length.</summary>
    public AIRealtimeOptions? Realtime { get; set; }

    /// <summary>
    /// Makes the feature reachable straight from the app at <c>POST /ai/features/{name}</c>. The server then
    /// owns the prompt; see <see cref="SystemPrompt"/>, <see cref="MaxInputTokens"/> and <see cref="MaxTurns"/>.
    /// </summary>
    public bool Expose { get; set; }

    /// <summary>
    /// Put first in every exposed call; the client's own system messages are dropped. A realtime feature sends it as
    /// the session's instructions.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>The largest exposed request, estimated at four characters a token. Larger gets 413.</summary>
    public int? MaxInputTokens { get; set; }

    /// <summary>The most user messages an exposed conversation may carry. More gets 413.</summary>
    public int? MaxTurns { get; set; }

    /// <summary>
    /// Who an exposed feature answers. Unset, a chat or image feature answers <see cref="AICallers.Any"/> and a realtime
    /// feature only <see cref="AICallers.SignedIn"/>, because a voice session is paid by the minute.
    /// </summary>
    public AICallers? Callers { get; set; }

    internal AICallers EffectiveCallers => Callers ?? (Kind == AIFeatureKind.Realtime ? AICallers.SignedIn : AICallers.Any);

    /// <summary>The feature's budget and the limits per caller.</summary>
    public AIBudgetLimits? Budget { get; set; }

    /// <summary>Calls a caller may start per minute.</summary>
    public AICallerRates? RequestsPerMinute { get; set; }

    /// <summary>Calls one caller may have running at once. A stream counts until it ends.</summary>
    public int? ConcurrentPerCaller { get; set; }

    /// <summary>Response caching over the host's <c>IDistributedCache</c>.</summary>
    public AICacheOptions? Cache { get; set; }

    /// <summary>Records prompts and responses in the OpenTelemetry spans. Off by default.</summary>
    public bool EnableSensitiveData { get; set; }
}

/// <summary>An image feature's defaults.</summary>
public sealed class AIImageOptions
{
    /// <summary><c>WIDTHxHEIGHT</c>, for example <c>1536x1024</c>.</summary>
    public string? Size { get; set; }

    /// <summary>Images per call.</summary>
    public int? Count { get; set; }
}

/// <summary>
/// A realtime feature's session: <c>Ai:Features:&lt;Name&gt;:Realtime</c>. Everything here is set on the server; the
/// app sends only the feature's name.
/// </summary>
/// <remarks>
/// The token an app gets is not a lock: over its connection the app can change the voice, the instructions and the
/// tools. Until <see cref="TokenSeconds"/> have passed, the same token opens further
/// connections to the same session, so keep it short; the reservation covers <see cref="MaxMinutes"/> once.
/// </remarks>
public sealed class AIRealtimeOptions
{
    /// <summary>The provider's voice, such as <c>marin</c> or <c>cedar</c>. Unset, the provider's default.</summary>
    public string? Voice { get; set; }

    /// <summary>
    /// <c>server_vad</c>, <c>semantic_vad</c> or <c>none</c>, when the app commits each turn itself. Unset, the
    /// provider's default.
    /// </summary>
    public string? TurnDetection { get; set; }

    /// <summary>For <c>semantic_vad</c>: <c>low</c>, <c>medium</c>, <c>high</c> or <c>auto</c>.</summary>
    public string? Eagerness { get; set; }

    /// <summary><c>near_field</c> for a phone held close, <c>far_field</c> for a speaker phone. Unset, off.</summary>
    public string? NoiseReduction { get; set; }

    /// <summary>A transcription model for what the user says, such as <c>gpt-4o-mini-transcribe</c>. Unset, none.</summary>
    public string? TranscriptionModel { get; set; }

    /// <summary>The language the user speaks, as ISO-639-1 (<c>sv</c>), to help the transcription.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// The minutes a session is budgeted for. Each token reserves <c>MaxMinutes × PerMinute</c> from the budgets the
    /// moment it is made, because the server does not see the session's real usage. 10 by default.
    /// </summary>
    public int MaxMinutes { get; set; } = 10;

    /// <summary>How long the app has to start its session with the token, 10–7200 seconds. 60 by default.</summary>
    public int TokenSeconds { get; set; } = 60;
}

/// <summary>A feature's response cache.</summary>
public sealed class AICacheOptions
{
    /// <summary>How long an answer is kept. Unset means no caching.</summary>
    public TimeSpan? Ttl { get; set; }
}

/// <summary>Soft and hard limits on spending, in the app's currency, plus the limits per caller.</summary>
/// <remarks>
/// A soft limit raises <see cref="SpineAIOptions.OnBudgetThreshold"/> and marks responses; a hard one turns
/// the feature off for the rest of the day or month. A call is allowed while the spend is under the limit,
/// so the last call can overshoot by its own cost, which <see cref="AIFeatureOptions.MaxOutputTokens"/> bounds.
/// </remarks>
public sealed class AIBudgetLimits
{
    /// <summary>The day's soft limit.</summary>
    public decimal? DaySoft { get; set; }

    /// <summary>The day's hard limit.</summary>
    public decimal? DayHard { get; set; }

    /// <summary>The month's soft limit.</summary>
    public decimal? MonthSoft { get; set; }

    /// <summary>The month's hard limit.</summary>
    public decimal? MonthHard { get; set; }

    /// <summary>The limits each caller has, by kind. Ignored in the app-wide budget.</summary>
    public AICallerBudgets? PerCaller { get; set; }
}

/// <summary>Per-caller limits for signed-in users and for everyone else.</summary>
public sealed class AICallerBudgets
{
    /// <summary>Users signed in through the server.</summary>
    public AICallerBudget? SignedIn { get; set; }

    /// <summary>
    /// Installations and anonymous callers, whose id is only a claim. Set these tighter than
    /// <see cref="SignedIn"/>; unset, the signed-in limits apply.
    /// </summary>
    public AICallerBudget? Installation { get; set; }

    internal AICallerBudget? For(AICallerKind kind) => kind switch
    {
        AICallerKind.SignedIn => SignedIn,
        AICallerKind.Installation or AICallerKind.Anonymous => Installation ?? SignedIn,
        _ => null,
    };
}

/// <summary>What one caller may spend.</summary>
public sealed class AICallerBudget
{
    /// <summary>The caller's hard limit per day.</summary>
    public decimal? DayHard { get; set; }

    /// <summary>The caller's hard limit per month.</summary>
    public decimal? MonthHard { get; set; }

    /// <summary>Calls per day, counted whether or not the model has a price.</summary>
    public long? DayCalls { get; set; }
}

/// <summary>A rate for signed-in users and one for everyone else.</summary>
public sealed class AICallerRates
{
    /// <summary>Users signed in through the server.</summary>
    public int? SignedIn { get; set; }

    /// <summary>Installations and anonymous callers. Unset, the signed-in rate applies.</summary>
    public int? Installation { get; set; }

    internal int? For(AICallerKind kind) => kind switch
    {
        AICallerKind.SignedIn => SignedIn,
        AICallerKind.Installation or AICallerKind.Anonymous => Installation ?? SignedIn,
        _ => null,
    };
}

/// <summary>
/// What a model costs, per million tokens or per image. Prices change more often than Spine is released,
/// so they live in configuration only.
/// </summary>
public sealed class AIPrice
{
    /// <summary>Input tokens that were not read from the provider's cache.</summary>
    public decimal? InputPerMillion { get; set; }

    /// <summary>Input tokens read from the provider's cache. Unset, they cost the same as other input.</summary>
    public decimal? CachedInputPerMillion { get; set; }

    /// <summary>Output tokens, reasoning included unless <see cref="ReasoningPerMillion"/> is set.</summary>
    public decimal? OutputPerMillion { get; set; }

    /// <summary>Reasoning tokens, for the rare model that prices them apart from output.</summary>
    public decimal? ReasoningPerMillion { get; set; }

    /// <summary>Each image generated.</summary>
    public decimal? PerImage { get; set; }

    /// <summary>A minute of a realtime session, which each token reserves <see cref="AIRealtimeOptions.MaxMinutes"/> of.</summary>
    public decimal? PerMinute { get; set; }

    /// <summary>
    /// Counts a provider reports in <see cref="UsageDetails.AdditionalCounts"/>, such as cache writes, priced
    /// per million by the count's name.
    /// </summary>
    public Dictionary<string, decimal> Additional { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
