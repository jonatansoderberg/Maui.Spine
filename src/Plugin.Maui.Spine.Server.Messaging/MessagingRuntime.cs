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
    private static readonly TimeSpan RetryProvisioningAfter = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private Task? _ready;
    private bool _provisioned;
    private DateTimeOffset _retryAt;

    public MessagingSettings Settings { get; } = settings;
    public MessageTopology Topology => Settings.Topology;
    public IMessageTransport Transport { get; } = transport;
    public IMessageStore Store { get; } = store;
    public TimeProvider Time { get; } = time;
    public ILogger Logger { get; } = logger;

    /// <summary>
    /// Provisions the transport and the store once. A failure does not stop messaging: the queues may well exist (made
    /// by infrastructure as code, for an identity that may not create them), so publishing and handling go on, and an
    /// operation on a queue that is really missing fails on its own. Provisioning is tried again after half a minute,
    /// for a storage emulator that was still starting.
    /// </summary>
    public Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var ready = _ready;
        if (ready is { IsCompleted: true } && (_provisioned || Time.GetUtcNow() < _retryAt))
            return Task.CompletedTask;

        lock (_gate)
        {
            if (_ready is null || (_ready.IsCompleted && !_provisioned && Time.GetUtcNow() >= _retryAt))
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
            _provisioned = true;
            Logger.LogInformation("Messaging ready on {Transport}: {Subscribers} subscribers, {Lanes} lanes, {Types} message types.",
                Transport.GetType().Name, Topology.Subscribers.Count, Topology.Lanes.Count(), Topology.Types.Count);
        }
        catch (Exception exception)
        {
            _retryAt = Time.GetUtcNow() + RetryProvisioningAfter;
            Logger.LogError(exception, "Messaging could not provision {Transport}. It carries on, assuming the queues exist, and tries again in {Retry}.",
                Transport.GetType().Name, RetryProvisioningAfter);
        }
    }
}
