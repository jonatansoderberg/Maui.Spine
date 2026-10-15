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
