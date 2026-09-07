using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// The register of devices the server can reach. Spine keeps it here rather than at a third party,
/// so the app only ever talks to its own backend and the backend decides which tags a client may set.
/// </summary>
/// <remarks>
/// An implementation over Cosmos DB or SQL is about a hundred lines; see
/// <see cref="InMemoryPushInstallationStore"/> for the shape.
/// </remarks>
public interface IPushInstallationStore
{
    /// <summary>Writes <paramref name="installation"/>, replacing any registration with the same id.</summary>
    /// <param name="installation">The registration to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task UpsertAsync(PushInstallation installation, CancellationToken cancellationToken = default);

    /// <summary>Reads one registration, or <see langword="null"/> when there is none.</summary>
    /// <param name="id">The installation id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The registration, or <see langword="null"/>.</returns>
    Task<PushInstallation?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Removes one registration. Does nothing when there is none.</summary>
    /// <param name="id">The installation id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>The registrations matching <paramref name="expression"/>, skipping expired ones.</summary>
    /// <param name="expression">The tag expression to match.</param>
    /// <param name="platforms">Only these platforms, or every platform when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the enumeration.</param>
    /// <returns>The matching registrations, streamed.</returns>
    IAsyncEnumerable<PushInstallation> QueryAsync(
        PushTagExpression expression,
        IReadOnlyCollection<PushPlatform>? platforms = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes registrations not written since <paramref name="olderThan"/>, and expired ones.</summary>
    /// <param name="olderThan">The cutoff; registrations older than this go.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many were removed.</returns>
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every registration holding <paramref name="handle"/>. Called when a transport reports
    /// the token dead, so the same device is not retried.
    /// </summary>
    /// <param name="handle">The device token, registration token, or channel URI.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many were removed.</returns>
    Task<int> InvalidateAsync(string handle, CancellationToken cancellationToken = default);
}
