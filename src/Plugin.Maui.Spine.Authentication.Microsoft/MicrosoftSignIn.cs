using Microsoft.Identity.Client;

namespace Plugin.Maui.Spine.Authentication;

/// <summary><c>AddMicrosoft</c>: MSAL's ID token to the Spine server, which needs no nonce from Microsoft (MSAL picks its own).</summary>
internal sealed class MicrosoftNativeSignIn(MicrosoftClient client) : INativeSignIn
{
    public bool UsesNonce => false;

    public async Task<NativeCredential?> SignInAsync(string? nonce, CancellationToken cancellationToken) =>
        await client.SignInAsync(freshIdToken: true, cancellationToken) is { } result
            ? new NativeCredential(result.IdToken, result.ClaimsPrincipal?.FindFirst("name")?.Value, result.Account.HomeAccountId.Identifier)
            : null;

    public Task SignedOutAsync() => client.ForgetAsync(accountId: null);

    public Task RefusedAsync(NativeCredential credential) => client.ForgetAsync(credential.AccountId);

    public Task<byte[]?> PictureAsync(CancellationToken cancellationToken) => client.PictureAsync(accountId: null, cancellationToken);
}

/// <summary>
/// <c>UseMicrosoft</c>: MSAL's own tokens are the session's. The session keeps the MSAL account's id where it would
/// keep a refresh token, and a refresh is MSAL's silent token request.
/// </summary>
internal sealed class MicrosoftDirectSignIn(MicrosoftClient client, SpineMicrosoftOptions options) : IDirectSignIn
{
    private string? _issued;

    public async Task<(SpineTokens? Tokens, SignInResult? Problem)> SignInAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await client.SignInAsync(freshIdToken: false, cancellationToken) is { } result ? (Tokens(result), null) : (null, SignInResult.Canceled);
        }
        catch (InvalidOperationException e)
        {
            return (null, SignInResult.Failure(e.Message));
        }
    }

    public async Task<ServerAnswer> RefreshAsync(string refreshToken, SpineUser? user, CancellationToken cancellationToken)
    {
        try
        {
            if (await client.SilentAsync(refreshToken, forceRefresh: false, cancellationToken) is not { } result)
                return new ServerAnswer(null, 401, "MSAL no longer has the account.");

            // MSAL hands out its cached token until five minutes before it expires, and the session asks sooner only
            // after an API refused that token (a revocation, a policy change): then only a new one helps.
            if (result.AccessToken == _issued)
                result = await client.SilentAsync(refreshToken, forceRefresh: true, cancellationToken) ?? result;

            return new ServerAnswer(Tokens(result), 200, null);
        }
        catch (MsalUiRequiredException e)
        {
            // Revoked, expired, a changed password, consent withdrawn: this sign-in is over.
            return new ServerAnswer(null, 401, e.ErrorCode);
        }
        catch (Exception e) when (e is MsalException or HttpRequestException)
        {
            return new ServerAnswer(null, 0, e.Message);
        }
    }

    public Task<byte[]?> PictureAsync(CancellationToken cancellationToken) => client.PictureAsync(accountId: null, cancellationToken);

    public async Task<ServerAnswer> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        await client.ForgetAsync(refreshToken);
        return new ServerAnswer(null, 200, null);
    }

    private SpineTokens Tokens(AuthenticationResult result) => new(
        _issued = result.AccessToken,
        result.ExpiresOn,
        result.Account.HomeAccountId.Identifier,
        new SpineUser(
            result.UniqueId ?? result.Account.HomeAccountId.ObjectId,
            // The sign-in name, unverified (EmailVerified stays false); a phone number is no email.
            result.Account.Username is { } username && username.Contains('@') ? username : null,
            Claim(result, "name"),
            options.Name,
            GivenName: Claim(result, "given_name"),
            FamilyName: Claim(result, "family_name"),
            Organization: result.TenantId is { } tenant && tenant != ConsumersTenant ? tenant : null));

    // Personal Microsoft accounts all sign in through this tenant: no organization.
    private const string ConsumersTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";

    private static string? Claim(AuthenticationResult result, string type) => result.ClaimsPrincipal?.FindFirst(type)?.Value;
}
