using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>What survives a restart: the refresh token and who it belongs to. Never the access token.</summary>
internal sealed record StoredSession(string RefreshToken, SpineUser User);

/// <summary>Where <see cref="StoredSession"/> is kept: SecureStorage in the app, memory in tests.</summary>
internal interface ISpineAuthStorage
{
    Task<string?> LoadAsync();
    Task SaveAsync(string value);
    Task ClearAsync();
}

internal sealed class InMemorySpineAuthStorage : ISpineAuthStorage
{
    public string? Value { get; private set; }

    public Task<string?> LoadAsync() => Task.FromResult(Value);

    public Task SaveAsync(string value)
    {
        Value = value;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Value = null;
        return Task.CompletedTask;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StoredSession))]
internal sealed partial class StoredSessionJson : JsonSerializerContext;

/// <summary>
/// The signed-in user and their tokens. The access token lives in memory and is refreshed when less than a minute is
/// left, one refresh at a time; a refresh the server refuses signs the user out.
/// </summary>
internal sealed class SpineAuthSession(SpineAuthClient client, ISpineAuthStorage storage, TimeProvider time, ILogger logger) : ISignedInSession
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _loadGate = new();
    private Task? _loading;
    private string? _refreshToken;
    private string? _accessToken;
    private DateTimeOffset _accessExpiresAt;

    public SpineUser? User { get; private set; }

    public string? UserId => User?.Id;

    public event EventHandler<SpineUser>? SignedIn;
    public event EventHandler? SignedOut;
    public event EventHandler? Changed;

    /// <summary>Reads the stored session once; every call after the first waits for the same read. Raises <see cref="Changed"/> when it finds a user.</summary>
    public Task LoadAsync()
    {
        lock (_loadGate) return _loading ??= LoadCoreAsync();
    }

    public async Task<SpineUser> AcceptAsync(SpineTokens tokens)
    {
        await LoadAsync();
        string? replaced;

        await _gate.WaitAsync();
        try
        {
            replaced = _refreshToken;
            await SaveThenSetAsync(tokens);
        }
        finally
        {
            _gate.Release();
        }

        // A sign-in on top of another (an app's own page calling the password methods) leaves the old sign-in unused.
        if (replaced is not null && replaced != tokens.RefreshToken) _ = client.RevokeAsync(replaced, CancellationToken.None);

        SignedIn?.Invoke(this, tokens.User);
        Changed?.Invoke(this, EventArgs.Empty);
        return tokens.User;
    }

    /// <summary>An access token with at least a minute left, refreshed if needed; <see langword="null"/> when signed out or offline.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync();
        if (_accessToken is { } current && _accessExpiresAt - time.GetUtcNow() > Margin) return current;
        return await RefreshAsync(staleToken: null, cancellationToken);
    }

    /// <summary>The server refused <paramref name="staleToken"/>: a new one, unless another caller already got one.</summary>
    public Task<string?> RefreshAfterUnauthorizedAsync(string staleToken, CancellationToken cancellationToken = default) =>
        RefreshAsync(staleToken, cancellationToken);

    public async Task<string?> GetAuthorizationHeaderAsync(CancellationToken cancellationToken = default) =>
        await GetAccessTokenAsync(cancellationToken) is { } token ? $"Bearer {token}" : null;

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync();
        string? refresh;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            refresh = _refreshToken;
            if (refresh is null && User is null) return;
            await ClearAsync();
        }
        finally
        {
            _gate.Release();
        }

        // Signed out here whatever the server says; revoking only stops the token working anywhere else.
        if (refresh is not null)
        {
            var answer = await client.RevokeAsync(refresh, cancellationToken);
            if (answer.Status is 0 or >= 500) logger.LogWarning("Spine sign-in: the refresh token could not be revoked on sign-out: {Error}", answer.Error);
        }

        RaiseSignedOut();
    }

    /// <summary>Forgets the session without asking the server, for an account that no longer exists.</summary>
    public async Task ForgetAsync()
    {
        await LoadAsync();
        await _gate.WaitAsync();
        try
        {
            await ClearAsync();
        }
        finally
        {
            _gate.Release();
        }

        RaiseSignedOut();
    }

    private async Task<string?> RefreshAsync(string? staleToken, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another caller refreshed while this one waited.
            if (_accessToken is { } fresh && fresh != staleToken && _accessExpiresAt - time.GetUtcNow() > Margin) return fresh;
            if (_refreshToken is not { } refresh) return null;

            // Not the caller's token: a refresh abandoned halfway leaves the server with the token used and the client
            // without its successor. The server forgives one more try within its grace, but not a habit of it.
            using var timeout = new CancellationTokenSource(RefreshTimeout);
            var answer = await client.RefreshAsync(refresh, timeout.Token);
            if (answer.Tokens is { } tokens)
            {
                await SaveThenSetAsync(tokens);
                return tokens.AccessToken;
            }

            // Unknown, expired, revoked or reused: this sign-in is over. Anything else (offline, a 5xx) keeps the session.
            if (answer.Status == 401)
            {
                logger.LogInformation("Spine sign-in: the server refused the refresh token ({Error}); signing out.", answer.Error);
                await ClearAsync();
            }
            else
            {
                logger.LogWarning("Spine sign-in: the access token could not be refreshed: {Error}", answer.Error);
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }

        RaiseSignedOut();
        return null;
    }

    private async Task LoadCoreAsync()
    {
        string? json;
        try
        {
            json = await storage.LoadAsync();
        }
        catch (Exception e)
        {
            // The Keychain is closed before the first unlock after a restart (a push or a background task can launch the
            // app then): signed out for this launch, but the stored session is kept for the next.
            logger.LogWarning(e, "Spine sign-in: the stored session could not be read; signed out until the next launch.");
            return;
        }

        StoredSession? stored;
        try
        {
            stored = json is null ? null : JsonSerializer.Deserialize(json, StoredSessionJson.Default.StoredSession);
        }
        catch (JsonException e)
        {
            logger.LogWarning(e, "Spine sign-in: the stored session is not readable; starting signed out.");
            await storage.ClearAsync();
            return;
        }

        if (stored is null) return;

        _refreshToken = stored.RefreshToken;
        User = stored.User;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Saved before it is used: a refresh token in memory but not in storage would be gone after a restart, and the
    // one in storage already traded in.
    private async Task SaveThenSetAsync(SpineTokens tokens)
    {
        try
        {
            await storage.SaveAsync(JsonSerializer.Serialize(new StoredSession(tokens.RefreshToken, tokens.User), StoredSessionJson.Default.StoredSession));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Spine sign-in: the new refresh token could not be saved; the next launch may have to sign in again.");
        }

        Set(tokens);
    }

    private void Set(SpineTokens tokens)
    {
        _accessToken = tokens.AccessToken;
        _accessExpiresAt = tokens.ExpiresAt;
        _refreshToken = tokens.RefreshToken;
        User = tokens.User;
    }

    private async Task ClearAsync()
    {
        _accessToken = null;
        _refreshToken = null;
        User = null;
        await storage.ClearAsync();
    }

    private void RaiseSignedOut()
    {
        SignedOut?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
