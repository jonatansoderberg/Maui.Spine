using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Authentication;
using Plugin.Maui.Spine.Server.AI;

namespace Plugin.Maui.Spine.AI;

/// <summary>
/// Who a function call is for and where it runs. A function takes it as a parameter, which the model's schema leaves out.
/// </summary>
/// <remarks>
/// On the server it is made for every request from the request's user. On the device the caller is the installation, or
/// the signed-in user's id when Spine.Authentication shares it (<c>ISignedInSession</c>).
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

    /// <summary>The provider's conversation id, when there is one.</summary>
    public string? ConversationId { get; init; }

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
        // The app knows its user by id only, unless Spine.Authentication's lookup is there.
        if (id is null) return RunsOn == SpineAIFunctionSide.Device ? new SpineUser(Caller.Id, null, null, "") : null;
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
