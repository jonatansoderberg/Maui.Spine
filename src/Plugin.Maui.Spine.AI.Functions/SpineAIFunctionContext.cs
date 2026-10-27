using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Authentication;
using Plugin.Maui.Spine.Server.AI;

namespace Plugin.Maui.Spine.AI;

/// <summary>
/// Who a function call is for and where it runs. A function takes it as a parameter, which the model's schema leaves out.
/// </summary>
/// <remarks>
/// On the server it is made for every request from the request's user, with the time zone and language the app sent. On
/// the device the caller is the signed-in user when Spine.Authentication is used, and otherwise the installation.
/// </remarks>
public sealed class SpineAIFunctionContext
{
    private static readonly ClaimsPrincipal Nobody = new(new ClaimsIdentity());

    private Task<SpineUser?>? _user;

    /// <summary>A context for one conversation.</summary>
    /// <param name="caller">Who the calls are for.</param>
    /// <param name="runsOn">The side the calls run on.</param>
    public SpineAIFunctionContext(AICaller caller, SpineAIFunctionSide runsOn)
    {
        ArgumentNullException.ThrowIfNull(caller);
        Caller = caller;
        RunsOn = runsOn;
    }

    /// <summary>Who the calls are for. Only <see cref="AICallerKind.SignedIn"/> is a user the server authenticated.</summary>
    public AICaller Caller { get; }

    /// <summary>The side the calls run on.</summary>
    public SpineAIFunctionSide RunsOn { get; }

    /// <summary>The feature the conversation is with, such as <c>Assistant</c> or <c>Voice</c>.</summary>
    public string? Feature { get; init; }

    /// <summary>The conversation the call belongs to: a voice session's own id, or a chat's <c>ChatOptions.ConversationId</c>.</summary>
    public string? ConversationId { get; init; }

    /// <summary>The model's id for this call, when the path knows it.</summary>
    public string? CallId { get; init; }

    /// <summary>
    /// The user's time zone, an IANA id such as <c>Europe/Stockholm</c>: the device's own, and on the server the one the
    /// app sent. <see langword="null"/> when the app sent none.
    /// </summary>
    public string? TimeZone { get; init; }

    /// <summary>The user's language, a BCP-47 tag such as <c>sv-SE</c>, or <see langword="null"/> when the app sent none.</summary>
    public string? Locale { get; init; }

    /// <summary>On the device, the signed-in user as Spine.Authentication knows them; <see cref="GetUserAsync"/> returns it.</summary>
    public SpineUser? SignedInUser { get; init; }

    /// <summary>
    /// The user's time zone: <see cref="TimeZone"/>, else the device's own on the device and UTC on the server, where the
    /// server's zone says nothing about the user.
    /// </summary>
    public TimeZoneInfo GetTimeZone() =>
        TimeZone is { Length: > 0 } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone
        : RunsOn == SpineAIFunctionSide.Device ? TimeZoneInfo.Local : TimeZoneInfo.Utc;

    /// <summary>Now, in the user's time zone. The services' <see cref="TimeProvider"/> when there is one.</summary>
    public DateTimeOffset Now() =>
        TimeZoneInfo.ConvertTime((Services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow(), GetTimeZone());

    /// <summary>This context for one call.</summary>
    internal SpineAIFunctionContext WithCall(string callId) => new(Caller, RunsOn)
    {
        Feature = Feature,
        ConversationId = ConversationId,
        CallId = callId,
        TimeZone = TimeZone,
        Locale = Locale,
        SignedInUser = SignedInUser,
        User = User,
        Services = Services,
        CancellationToken = CancellationToken,
        MaxResultCharacters = MaxResultCharacters,
    };

    /// <summary>
    /// The context for the app's own functions: the signed-in user when Spine.Authentication is used (with their name
    /// and email), else the push link's user id, else the installation; the device's time zone and language.
    /// </summary>
    internal static async Task<SpineAIFunctionContext> ForDeviceAsync(
        IServiceProvider? services, Func<CancellationToken, Task<string?>> installationId, string? feature, string? conversationId,
        string? callId, CancellationToken cancellationToken, int? maxResultCharacters = null)
    {
        var signedIn = services?.GetService<ISpineSignedInUser>()?.User;
        var userId = signedIn?.Id ?? (services?.GetService<ISignedInSession>()?.UserId is { Length: > 0 } linked ? linked : null);
        var caller = userId is not null ? AICaller.SignedIn(userId)
            : await installationId(cancellationToken) is { Length: > 0 } installation ? AICaller.Installation(installation)
            : AICaller.Anonymous(null);

        return new SpineAIFunctionContext(caller, SpineAIFunctionSide.Device)
        {
            Feature = feature,
            ConversationId = conversationId,
            CallId = callId,
            TimeZone = SpineClientHeaders.DeviceTimeZone(),
            Locale = SpineClientHeaders.DeviceLocale(),
            SignedInUser = signedIn,
            Services = services ?? EmptyServices.Instance,
            CancellationToken = cancellationToken,
            MaxResultCharacters = maxResultCharacters ?? SpineAIFunction.DefaultMaxResultCharacters,
        };
    }

    /// <summary>
    /// The request's user on the server, with the claims of the app's access token (<c>sub</c>, <c>email</c>, <c>name</c>,
    /// <c>provider</c>). An empty principal for installations, anonymous callers and on the device.
    /// </summary>
    public ClaimsPrincipal User { get; init; } = Nobody;

    /// <summary>The app's or the request's services.</summary>
    public IServiceProvider Services { get; init; } = EmptyServices.Instance;

    /// <summary>Cancelled when the conversation or the request ends.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>The longest result that reaches the model, in characters of JSON.</summary>
    public int MaxResultCharacters { get; init; } = SpineAIFunction.DefaultMaxResultCharacters;

    /// <summary>Whether the caller is a signed-in user.</summary>
    public bool IsSignedIn => Caller.Kind == AICallerKind.SignedIn;

    /// <summary>
    /// The signed-in user, or <see langword="null"/> for anyone else. On the server it comes from
    /// Spine.Server.Authentication's users when that is registered, and otherwise from the access token's claims.
    /// </summary>
    /// <remarks>Looked up once per context. Server-only code that needs the server's whole record resolves <c>IUserResolver</c> from <see cref="Services"/>.</remarks>
    public Task<SpineUser?> GetUserAsync() => _user ??= LookUpAsync();

    private async Task<SpineUser?> LookUpAsync()
    {
        if (!IsSignedIn) return null;

        if (Services.GetService<ISpineUserLookup>() is { } lookup)
            return await lookup.FindAsync(Caller.Id, User, CancellationToken);

        return FromClaims();
    }

    private SpineUser? FromClaims()
    {
        var id = Claim("sub") ?? Claim(ClaimTypes.NameIdentifier);
        // On the device: the user Spine.Authentication signed in, else only their id.
        if (id is null) return RunsOn != SpineAIFunctionSide.Device ? null : SignedInUser is { } known && known.Id == Caller.Id ? known : new SpineUser(Caller.Id, null, null, "");
        if (id != Caller.Id) return null;

        return new SpineUser(id,
            Claim("email") ?? Claim(ClaimTypes.Email),
            Claim("name") ?? Claim(ClaimTypes.Name),
            Claim("provider") ?? "");
    }

    private string? Claim(string type) => User.FindFirst(type)?.Value;

    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
