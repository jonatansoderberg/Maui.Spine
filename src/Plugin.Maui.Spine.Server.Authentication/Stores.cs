using System.Collections.Concurrent;

namespace Plugin.Maui.Spine.Server.Authentication;

/// <summary>A user of the app, as the server keeps them.</summary>
public sealed class SpineAuthUser
{
    /// <summary>The id the access token carries as <c>sub</c>. Never changes.</summary>
    public required string Id { get; init; }

    /// <summary>The email, if known. At most one user owns an email.</summary>
    public string? Email { get; set; }

    /// <summary>The email was shown to belong to the user: an emailed code, or a provider that vouches for it.</summary>
    public bool EmailVerified { get; set; }

    /// <summary>The name to show.</summary>
    public string? DisplayName { get; set; }

    /// <summary>A public https address of the user's picture, from the provider they last signed in with that gives one.</summary>
    public string? PictureUrl { get; set; }

    /// <summary>The first name, as first given.</summary>
    public string? GivenName { get; set; }

    /// <summary>The last name, as first given.</summary>
    public string? FamilyName { get; set; }

    /// <summary>The organization of the account the user last signed in with: a Microsoft tenant id, a Google Workspace domain.</summary>
    public string? Organization { get; set; }

    /// <summary>The password as ASP.NET Core Identity's <c>PasswordHasher</c> hashes it, or <see langword="null"/>.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>Wrong passwords in a row.</summary>
    public int FailedSignIns { get; set; }

    /// <summary>No password sign-in before this.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>When the account was made.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The store's concurrency token as the user was read (an ETag, a row version). <see cref="IUserResolver.UpdateAsync"/>
    /// fails when it no longer matches, so two requests that read the same user cannot overwrite each other.
    /// </summary>
    public string? Version { get; set; }

    internal SpineUser ToContract(string provider, IReadOnlyList<SpineAuthLogin> logins) =>
        new(Id, Email, DisplayName, provider, PictureUrl, GivenName, FamilyName, Organization,
            logins.Select(l => l.Provider).Distinct().Order().ToList(),
            logins.OrderBy(l => l.LinkedAt ?? DateTimeOffset.MinValue).Select(l => l.ToContract()).ToList());
}

/// <summary>One way a user signs in: a provider and the provider's id for them.</summary>
/// <param name="Provider">The provider's name, such as <c>apple</c> or <c>password</c>.</param>
/// <param name="Subject">The provider's <c>sub</c>, or the user id for <c>password</c>.</param>
/// <param name="UserId">The user it belongs to.</param>
public sealed record SpineAuthLogin(string Provider, string Subject, string UserId)
{
    /// <summary>A refresh token the provider gave the server, kept to revoke it when the account is deleted (Apple).</summary>
    public string? ProviderRefreshToken { get; init; }

    /// <summary>The client id <see cref="ProviderRefreshToken"/> was issued to.</summary>
    public string? ProviderClientId { get; init; }

    /// <summary>The email this provider gave for the user, vouched for or not; shown on the app's accounts page.</summary>
    public string? Email { get; init; }

    /// <summary>The name this provider gave.</summary>
    public string? DisplayName { get; init; }

    /// <summary>When the login was added to the user.</summary>
    public DateTimeOffset? LinkedAt { get; init; }

    /// <summary>A passkey's public key, as COSE; only on <c>passkey</c> logins.</summary>
    public byte[]? PasskeyPublicKey { get; init; }

    /// <summary>A passkey's signature counter, which a sign-in must move forward when the authenticator keeps one.</summary>
    public uint PasskeySignCount { get; init; }

    /// <summary>The AAGUID of the authenticator that made a passkey; empty for those that do not say.</summary>
    public Guid? PasskeyAaguid { get; init; }

    /// <summary>The name of the authenticator, from <c>SpinePasskeyOptions.AuthenticatorNames</c> at registration.</summary>
    public string? PasskeyAuthenticator { get; init; }

    /// <summary>The device a passkey was made on, as the app said.</summary>
    public string? DeviceName { get; init; }

    /// <summary>When the login last signed in: a provider's token or browser sign-in, a passkey, a password or an emailed code.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    internal SpineLoginInfo ToContract() => new(Provider, Email, DisplayName, LinkedAt, Subject, PasskeyAuthenticator, DeviceName, LastUsedAt);
}

/// <summary>
/// Where users live. The default keeps them in Azure Tables or in memory; an app with users of its own implements
/// this over its database and registers it with <c>UseUsers(...)</c>.
/// </summary>
public interface IUserResolver
{
    /// <summary>Finds a user by id.</summary>
    Task<SpineAuthUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Finds the user who owns <paramref name="email"/>, ignoring case.</summary>
    Task<SpineAuthUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Finds the user a provider's subject is linked to.</summary>
    Task<SpineAuthUser?> FindByLoginAsync(string provider, string subject, CancellationToken cancellationToken = default);

    /// <summary>Every way <paramref name="userId"/> signs in.</summary>
    Task<IReadOnlyList<SpineAuthLogin>> GetLoginsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Adds a user.</summary>
    /// <returns><see langword="false"/> when another user already owns the email; nothing is added then.</returns>
    Task<bool> CreateAsync(SpineAuthUser user, CancellationToken cancellationToken = default);

    /// <summary>Saves a changed user if nobody changed it since it was read. The email does not change through this.</summary>
    /// <returns><see langword="false"/> when <see cref="SpineAuthUser.Version"/> no longer matches or the user is gone; nothing is saved then.</returns>
    Task<bool> UpdateAsync(SpineAuthUser user, CancellationToken cancellationToken = default);

    /// <summary>Adds a login, or replaces the one with the same provider and subject.</summary>
    Task SaveLoginAsync(SpineAuthLogin login, CancellationToken cancellationToken = default);

    /// <summary>Removes a user, their email and every login.</summary>
    Task DeleteAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one login from its user (unlinking a provider). The built-in stores implement it; a resolver over the
    /// app's own database that does not, makes <c>DELETE /auth/link/{provider}</c> answer 501.
    /// </summary>
    Task DeleteLoginAsync(SpineAuthLogin login, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This IUserResolver cannot remove a login.");
}

/// <summary>A refresh token as the store keeps it: by its hash, never the token itself.</summary>
/// <param name="Hash">SHA-256 of the token, base64url.</param>
/// <param name="FamilyId">Every token rotated from one sign-in shares it.</param>
/// <param name="UserId">The user.</param>
/// <param name="Provider">How the user signed in.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="UsedAt">When it was traded in; a second use revokes the family.</param>
public sealed record SpineRefreshToken(string Hash, string FamilyId, string UserId, string Provider, DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt = null);

/// <summary>Where refresh tokens and one-time values (nonces, emailed codes) live. Keys arrive hashed.</summary>
public interface ISpineAuthStore
{
    /// <summary>Keeps a value that can be taken once before <paramref name="expiresAt"/>.</summary>
    Task SaveOneTimeAsync(string key, string value, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Takes a one-time value: the first caller gets it, every later one and any caller after expiry gets <see langword="null"/>.</summary>
    Task<string?> TakeOneTimeAsync(string key, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Keeps a new refresh token.</summary>
    Task AddRefreshAsync(SpineRefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>Finds a refresh token by its hash.</summary>
    Task<SpineRefreshToken?> FindRefreshAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>Marks a refresh token used. Only one caller wins; the others get <see langword="false"/>.</summary>
    Task<bool> TryUseRefreshAsync(string hash, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Whether a family was revoked.</summary>
    Task<bool> IsFamilyRevokedAsync(string userId, string familyId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every token of one sign-in.</summary>
    Task RevokeFamilyAsync(string userId, string familyId, CancellationToken cancellationToken = default);

    /// <summary>Revokes every token the user has.</summary>
    Task RevokeUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Removes refresh tokens and one-time values that expired before <paramref name="now"/>.</summary>
    /// <returns>How many were removed.</returns>
    Task<int> PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>Users in the process. For tests and samples.</summary>
public sealed class InMemoryUserResolver : IUserResolver
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SpineAuthUser> _users = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _emails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, string), SpineAuthLogin> _logins = [];

    /// <inheritdoc />
    public Task<SpineAuthUser?> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_users.TryGetValue(userId, out var user) ? Copy(user) : null);
    }

    /// <inheritdoc />
    public Task<SpineAuthUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_emails.TryGetValue(email.Trim(), out var id) && _users.TryGetValue(id, out var user) ? Copy(user) : null);
    }

    /// <inheritdoc />
    public Task<SpineAuthUser?> FindByLoginAsync(string provider, string subject, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_logins.TryGetValue((provider.ToLowerInvariant(), subject), out var login) && _users.TryGetValue(login.UserId, out var user)
                ? Copy(user)
                : null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SpineAuthLogin>> GetLoginsAsync(string userId, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult<IReadOnlyList<SpineAuthLogin>>(_logins.Values.Where(l => l.UserId == userId).ToList());
    }

    /// <inheritdoc />
    public Task<bool> CreateAsync(SpineAuthUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (_gate)
        {
            if (user.Email is { } email)
            {
                if (_emails.ContainsKey(email.Trim())) return Task.FromResult(false);
                _emails[email.Trim()] = user.Id;
            }

            _users[user.Id] = Copy(user, version: Guid.NewGuid().ToString("N"));
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> UpdateAsync(SpineAuthUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (_gate)
        {
            if (!_users.TryGetValue(user.Id, out var current) || current.Version != user.Version) return Task.FromResult(false);
            _users[user.Id] = Copy(user, current.Email, Guid.NewGuid().ToString("N"));
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task SaveLoginAsync(SpineAuthLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        lock (_gate) _logins[(login.Provider.ToLowerInvariant(), login.Subject)] = login;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteLoginAsync(SpineAuthLogin login, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(login);
        lock (_gate) _logins.Remove((login.Provider.ToLowerInvariant(), login.Subject));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_users.Remove(userId, out var user) && user.Email is { } email) _emails.Remove(email.Trim());
            foreach (var key in _logins.Where(l => l.Value.UserId == userId).Select(l => l.Key).ToList()) _logins.Remove(key);
        }

        return Task.CompletedTask;
    }

    private static SpineAuthUser Copy(SpineAuthUser user, string? email = null, string? version = null) => new()
    {
        Version = version ?? user.Version,
        Id = user.Id,
        Email = email ?? user.Email,
        EmailVerified = user.EmailVerified,
        DisplayName = user.DisplayName,
        PictureUrl = user.PictureUrl,
        GivenName = user.GivenName,
        FamilyName = user.FamilyName,
        Organization = user.Organization,
        PasswordHash = user.PasswordHash,
        FailedSignIns = user.FailedSignIns,
        LockedUntil = user.LockedUntil,
        CreatedAt = user.CreatedAt,
    };
}

/// <summary>Tokens in the process. For tests and samples.</summary>
public sealed class InMemorySpineAuthStore : ISpineAuthStore
{
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)> _oneTime = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SpineRefreshToken> _refresh = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string User, string Family), bool> _families = new();

    /// <summary>The refresh tokens kept, for tests.</summary>
    public IReadOnlyCollection<SpineRefreshToken> RefreshTokens => _refresh.Values.ToList();

    /// <inheritdoc />
    public Task SaveOneTimeAsync(string key, string value, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        _oneTime[key] = (value, expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> TakeOneTimeAsync(string key, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult(_oneTime.TryRemove(key, out var entry) && entry.ExpiresAt > now ? entry.Value : null);

    /// <inheritdoc />
    public Task AddRefreshAsync(SpineRefreshToken token, CancellationToken cancellationToken = default)
    {
        _refresh[token.Hash] = token;
        _families.TryAdd((token.UserId, token.FamilyId), false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SpineRefreshToken?> FindRefreshAsync(string hash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_refresh.TryGetValue(hash, out var token) ? token : null);

    /// <inheritdoc />
    public Task<bool> TryUseRefreshAsync(string hash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        while (_refresh.TryGetValue(hash, out var token))
        {
            if (token.UsedAt is not null) return Task.FromResult(false);
            if (_refresh.TryUpdate(hash, token with { UsedAt = now }, token)) return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> IsFamilyRevokedAsync(string userId, string familyId, CancellationToken cancellationToken = default) =>
        Task.FromResult(!_families.TryGetValue((userId, familyId), out var revoked) || revoked);

    /// <inheritdoc />
    public Task RevokeFamilyAsync(string userId, string familyId, CancellationToken cancellationToken = default)
    {
        _families[(userId, familyId)] = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        foreach (var key in _families.Keys.Where(k => k.User == userId)) _families[key] = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var removed = 0;
        foreach (var (key, entry) in _oneTime) if (entry.ExpiresAt <= now && _oneTime.TryRemove(key, out _)) removed++;
        foreach (var (key, token) in _refresh) if (token.ExpiresAt <= now && _refresh.TryRemove(key, out _)) removed++;
        return Task.FromResult(removed);
    }
}
