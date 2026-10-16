namespace Plugin.Maui.Spine.Authentication;

/// <summary>Each sign-in step against the server, turned into a <see cref="SignInResult"/> and, on success, a session.</summary>
internal sealed class SpineAuthFlows(SpineAuthClient client, SpineAuthSession session)
{
    public Task<SignInResult> PasswordAsync(string email, string password, CancellationToken cancellationToken) =>
        StepAsync("signin", new SpinePasswordSignIn(email, password), cancellationToken);

    public Task<SignInResult> SignUpAsync(string email, string password, string? displayName, CancellationToken cancellationToken) =>
        StepAsync("signup", new SpinePasswordSignUp(email, password, displayName), cancellationToken);

    public Task<SignInResult> VerifyAsync(string email, string code, CancellationToken cancellationToken) =>
        StepAsync("verify", new SpineEmailCode(email, code), cancellationToken);

    public Task<SignInResult> ResendAsync(string email, CancellationToken cancellationToken) =>
        StepAsync("resend", new SpineEmailRequest(email), cancellationToken);

    public Task<SignInResult> ForgotAsync(string email, CancellationToken cancellationToken) =>
        StepAsync("forgot", new SpineEmailRequest(email), cancellationToken);

    public Task<SignInResult> ResetAsync(string email, string code, string password, CancellationToken cancellationToken) =>
        StepAsync("reset", new SpinePasswordReset(email, code, password), cancellationToken);

    public async Task<SignInResult> IdTokenAsync(string provider, SpineIdTokenRequest request, CancellationToken cancellationToken) =>
        await ResultOfAsync(await client.IdTokenAsync(provider, request, cancellationToken));

    /// <summary>A provider's own sheet: the server's nonce in, the provider's ID token out to <c>/{provider}/token</c>.</summary>
    public async Task<SignInResult> NativeAsync(string provider, INativeSignIn native, CancellationToken cancellationToken)
    {
        string? nonce = null;
        if (native.UsesNonce)
        {
            nonce = (await client.NonceAsync(cancellationToken))?.Nonce;
            if (nonce is null) return SignInResult.Failure($"GET {new Uri(client.Root, "nonce")} did not answer with a nonce.");
        }

        if (await native.SignInAsync(nonce, cancellationToken) is not { } credential) return SignInResult.Canceled;

        var result = await IdTokenAsync(provider, new SpineIdTokenRequest(credential.IdToken, nonce, DisplayName: credential.DisplayName), cancellationToken);
        if (result.Status == SignInStatus.Failed) await native.RefusedAsync(credential);
        return result;
    }

    /// <summary>
    /// A browser sign-in through the server's broker: <c>/{provider}/start</c> with a PKCE challenge, then the code it
    /// returns with traded at <c>/token</c> with the verifier.
    /// </summary>
    public async Task<SignInResult> BrowserAsync(string provider, IBrowserSignIn browser, CancellationToken cancellationToken)
    {
        var pkce = Pkce.Create();
        var state = Pkce.Random();

        var answer = await browser.AuthenticateAsync(callback => new Uri(client.Root, $"{Uri.EscapeDataString(provider)}/start?" + Pkce.Query(new Dictionary<string, string>
        {
            ["redirect_uri"] = callback.AbsoluteUri,
            ["code_challenge"] = pkce.Challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
        })), cancellationToken);

        if (BrowserProblem(answer, state) is { } problem) return problem;

        return await ResultOfAsync(await client.CodeAsync(new SpineCodeExchange(answer!.Values["code"], pkce.Verifier, answer.Callback.AbsoluteUri), cancellationToken));
    }

    /// <summary>What a browser answer that brought no usable code means: a cancel, a refusal or a forged answer.</summary>
    internal static SignInResult? BrowserProblem(BrowserAnswer? answer, string state)
    {
        if (answer is null) return SignInResult.Canceled;

        // The state proves the answer is to the request this app made, not one an attacker started.
        if (!answer.Values.TryGetValue("state", out var returned) || returned != state)
            return SignInResult.Failure("The browser answered with another state.");

        if (answer.Values.TryGetValue("error", out var error))
            return error == "access_denied" ? SignInResult.Canceled : SignInResult.Failure(error);

        return answer.Values.ContainsKey("code") ? null : SignInResult.Failure("The browser answered with no code.");
    }

    public async Task<SignInResult> DeleteAccountAsync(CancellationToken cancellationToken)
    {
        if (await session.GetAccessTokenAsync(cancellationToken) is not { } token)
            return SignInResult.Failure(SpineAuthErrors.InvalidGrant);

        var answer = await client.DeleteAccountAsync(token, cancellationToken);
        if (answer.Status is not (204 or 200)) return SignInResult.Failure(answer.Error ?? $"DELETE account answered {answer.Status}.");

        await session.ForgetAsync();
        return new SignInResult(SignInStatus.Deleted);
    }

    private async Task<SignInResult> StepAsync<T>(string step, T body, CancellationToken cancellationToken) =>
        await ResultOfAsync(await client.PasswordAsync(step, body, cancellationToken));

    private async Task<SignInResult> ResultOfAsync(ServerAnswer answer)
    {
        if (answer.Tokens is { } tokens) return SignInResult.Success(await session.AcceptAsync(tokens));

        // The server sent an email with a code: on sign-up, on a resend, on a forgotten password, and when an
        // unverified account tries to sign in.
        if (answer.Accepted || answer.Error == SpineAuthErrors.EmailNotVerified) return SignInResult.Code;

        return SignInResult.Failure(answer.Error ?? $"The server answered {answer.Status}.");
    }
}
