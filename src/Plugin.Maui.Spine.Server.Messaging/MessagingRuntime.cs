using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging;

/// <summary>What the publisher, dispatcher, pump and janitor share, and the one-time provisioning they wait for.</summary>
internal sealed class MessagingRuntime(
    MessagingSettings settings,
    IMessageTransport transport,
    IMessageStore store,
    TimeProvider time,
    ILogger<MessagingRuntime> logger)
{
    private readonly Lock _gate = new();
    private Task? _ready;

    public MessagingSettings Settings { get; } = settings;
    public MessageTopology Topology => Settings.Topology;
    public IMessageTransport Transport { get; } = transport;
    public IMessageStore Store { get; } = store;
    public TimeProvider Time { get; } = time;
    public ILogger Logger { get; } = logger;

    /// <summary>
    /// Provisions the transport and the store once. A failed attempt is tried again by the next caller, so a storage
    /// account that was down at startup does not leave the host broken.
    /// </summary>
    public Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var ready = _ready;
        if (ready is { IsCompletedSuccessfully: true })
            return Task.CompletedTask;

        lock (_gate)
        {
            if (_ready is null || _ready.IsFaulted || _ready.IsCanceled)
                _ready = ProvisionAsync();
            return _ready.WaitAsync(cancellationToken);
        }
    }

    private async Task ProvisionAsync()
    {
        try
        {
            await Transport.ProvisionAsync(Topology, CancellationToken.None);
            await Store.ProvisionAsync(CancellationToken.None);
            Logger.LogInformation("Messaging ready on {Transport}: {Subscribers} subscribers, {Lanes} lanes, {Types} message types.",
                Transport.GetType().Name, Topology.Subscribers.Count, Topology.Lanes.Count(), Topology.Types.Count);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Messaging could not provision {Transport}; it tries again on the next publish or receive.", Transport.GetType().Name);
            throw;
        }
    }
}
