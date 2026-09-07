using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// A register kept in the process. Everything is lost on restart, which makes it right for tests
/// and for a sample server, and wrong for anything that has to survive a deploy.
/// </summary>
/// <param name="timeProvider">The clock used to skip expired registrations; the system clock when omitted.</param>
public sealed class InMemoryPushInstallationStore(TimeProvider? timeProvider = null) : IPushInstallationStore
{
    private readonly ConcurrentDictionary<string, PushInstallation> _installations = new(StringComparer.Ordinal);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>How many registrations the store holds, expired ones included.</summary>
    public int Count => _installations.Count;

    /// <inheritdoc />
    public Task UpsertAsync(PushInstallation installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        _installations[installation.Id] = installation;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PushInstallation?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Task.FromResult(_installations.GetValueOrDefault(id));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        _installations.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<PushInstallation> QueryAsync(
        PushTagExpression expression,
        IReadOnlyCollection<PushPlatform>? platforms = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var now = _time.GetUtcNow();

        foreach (var installation in _installations.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (installation.IsExpired(now)) continue;
            if (platforms is { Count: > 0 } && !platforms.Contains(installation.Platform)) continue;
            if (!expression.Matches(installation.Tags)) continue;

            yield return installation;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var removed = 0;

        foreach (var (id, installation) in _installations)
        {
            if (installation.UpdatedAt >= olderThan && !installation.IsExpired(now)) continue;
            if (_installations.TryRemove(id, out _)) removed++;
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<int> InvalidateAsync(string handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var removed = 0;

        foreach (var (id, installation) in _installations)
        {
            if (!string.Equals(installation.Handle, handle, StringComparison.Ordinal)) continue;
            if (_installations.TryRemove(id, out _)) removed++;
        }

        return Task.FromResult(removed);
    }
}
