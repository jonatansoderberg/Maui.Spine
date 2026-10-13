using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Plugin.Maui.Spine.Server.Messaging.Transport;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>
/// Messaging's state in one blob container, for every transport. Everything sits under a dated path, so cleanup removes
/// whole days rather than reading every item:
/// <list type="bullet">
/// <item><c>payloads/{expiry yyyy/MM/dd}/{id}</c>: offloaded payloads.</item>
/// <item><c>failed/{subscriber}/{yyyy/MM/dd}/{id}.json</c>: failed messages with their payload.</item>
/// <item><c>cancelled/{due yyyy/MM/dd}/{id}</c>: cancel markers.</item>
/// <item><c>dedup/{hash}</c>: deduplication ids, with their expiry in metadata.</item>
/// <item><c>locks/{name}</c>: leases for the janitor.</item>
/// </list>
/// </summary>
public sealed class BlobMessageStore : IMessageStore
{
    private const string ExpiresKey = "expires";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BlobContainerClient _container;
    private readonly TimeProvider _time;

    /// <summary>Creates the store on a container; <see cref="ProvisionAsync"/> creates it if it is missing.</summary>
    public BlobMessageStore(BlobContainerClient container, TimeProvider? time = null)
    {
        _container = container;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task ProvisionAsync(CancellationToken cancellationToken) => _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<PayloadReference> PutPayloadAsync(string messageId, ReadOnlyMemory<byte> body, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var location = $"payloads/{Day(expiresAt)}/{messageId}";
        await _container.GetBlobClient(location).UploadAsync(BinaryData.FromBytes(body), overwrite: true, cancellationToken);
        return new PayloadReference(location, body.Length);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>?> GetPayloadAsync(PayloadReference payload, CancellationToken cancellationToken)
    {
        try
        {
            var content = await _container.GetBlobClient(payload.Location).DownloadContentAsync(cancellationToken);
            return content.Value.Content.ToMemory();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task DeletePayloadAsync(PayloadReference payload, CancellationToken cancellationToken) =>
        _container.GetBlobClient(payload.Location).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TryReserveAsync(string deduplicationId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient($"dedup/{Hash(deduplicationId)}");
        var upload = new BlobUploadOptions { Metadata = new Dictionary<string, string> { [ExpiresKey] = expiresAt.UtcTicks.ToString(CultureInfo.InvariantCulture) } };

        try
        {
            upload.Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All };
            await blob.UploadAsync(BinaryData.Empty, upload, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
        }

        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            if (ExpiresOf(properties.Value.Metadata) > _time.GetUtcNow())
                return false;

            // Expired but not yet purged: take it over, unless another publisher just did.
            upload.Conditions = new BlobRequestConditions { IfMatch = properties.Value.ETag };
            await blob.UploadAsync(BinaryData.Empty, upload, cancellationToken);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status is 404 or 412)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task ReleaseReservationAsync(string deduplicationId, CancellationToken cancellationToken) =>
        _container.GetBlobClient($"dedup/{Hash(deduplicationId)}").DeleteIfExistsAsync(cancellationToken: cancellationToken);

    /// <inheritdoc />
    public Task CancelAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken) =>
        _container.GetBlobClient($"cancelled/{Day(deliverAt)}/{messageId}").UploadAsync(BinaryData.Empty, overwrite: true, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> IsCancelledAsync(string messageId, DateTimeOffset deliverAt, CancellationToken cancellationToken) =>
        await _container.GetBlobClient($"cancelled/{Day(deliverAt)}/{messageId}").ExistsAsync(cancellationToken);

    /// <inheritdoc />
    public Task SaveFailedAsync(FailedMessage message, CancellationToken cancellationToken) =>
        _container.GetBlobClient(FailedPath(message)).UploadAsync(BinaryData.FromObjectAsJson(message, Json), overwrite: true, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<FailedMessage> ListFailedAsync(string? subscriber, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prefix = subscriber is null ? "failed/" : $"failed/{subscriber}/";
        await foreach (var item in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = prefix }, cancellationToken))
        {
            BinaryData content;
            try
            {
                content = (await _container.GetBlobClient(item.Name).DownloadContentAsync(cancellationToken)).Value.Content;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                continue;
            }

            if (content.ToObjectFromJson<FailedMessage>(Json) is { } message)
                yield return message;
        }
    }

    /// <inheritdoc />
    public Task DeleteFailedAsync(FailedMessage message, CancellationToken cancellationToken) =>
        _container.GetBlobClient(FailedPath(message)).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IAsyncDisposable?> TryLockAsync(string name, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient($"locks/{name}");
        try
        {
            await blob.UploadAsync(BinaryData.Empty, new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
        }

        var lease = blob.GetBlobLeaseClient();
        try
        {
            await lease.AcquireAsync(LeaseDuration, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            return null;
        }

        return new HeldLease(lease, _time);
    }

    /// <inheritdoc />
    public async Task<int> PurgeAsync(DateTimeOffset now, TimeSpan retention, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var kept = DateOnly.FromDateTime((now - retention).UtcDateTime);

        var removed = await PurgeDaysAsync("payloads/", before: today, cancellationToken);
        removed += await PurgeDaysAsync("cancelled/", before: kept, cancellationToken);

        await foreach (var subscriber in _container.GetBlobsByHierarchyAsync(new GetBlobsByHierarchyOptions { Prefix = "failed/", Delimiter = "/" }, cancellationToken))
        {
            if (subscriber.IsPrefix)
                removed += await PurgeDaysAsync(subscriber.Prefix, before: kept, cancellationToken);
        }

        var expired = new List<string>();
        await foreach (var item in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = "dedup/", Traits = BlobTraits.Metadata }, cancellationToken))
        {
            if (ExpiresOf(item.Metadata) < now)
                expired.Add(item.Name);
        }
        removed += await DeleteAsync(expired, cancellationToken);

        return removed;
    }

    /// <summary>Removes the day folders under <paramref name="root"/> (<c>root/yyyy/MM/dd/</c>) older than <paramref name="before"/>.</summary>
    private async Task<int> PurgeDaysAsync(string root, DateOnly before, CancellationToken cancellationToken)
    {
        var removed = 0;
        await foreach (var year in Folders(root, cancellationToken))
        {
            if (!int.TryParse(Last(year), out var y) || y > before.Year)
                continue;

            await foreach (var month in Folders(year, cancellationToken))
            {
                if (!int.TryParse(Last(month), out var m) || new DateOnly(y, Math.Clamp(m, 1, 12), 1) > before)
                    continue;

                await foreach (var day in Folders(month, cancellationToken))
                {
                    if (!int.TryParse(Last(day), out var d) || !DateOnly.TryParseExact($"{y:D4}-{m:D2}-{d:D2}", "yyyy-MM-dd", out var date) || date >= before)
                        continue;

                    var names = new List<string>();
                    await foreach (var item in _container.GetBlobsAsync(new GetBlobsOptions { Prefix = day }, cancellationToken))
                        names.Add(item.Name);
                    removed += await DeleteAsync(names, cancellationToken);
                }
            }
        }
        return removed;
    }

    private async IAsyncEnumerable<string> Folders(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _container.GetBlobsByHierarchyAsync(new GetBlobsByHierarchyOptions { Prefix = prefix, Delimiter = "/" }, cancellationToken))
        {
            if (item.IsPrefix)
                yield return item.Prefix;
        }
    }

    private async Task<int> DeleteAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken },
            async (name, token) => await _container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: token));
        return names.Count;
    }

    private static string Last(string folder) => folder.TrimEnd('/')[(folder.TrimEnd('/').LastIndexOf('/') + 1)..];

    private static string Day(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static string FailedPath(FailedMessage message) => $"failed/{message.Subscriber}/{Day(message.FailedAt)}/{message.MessageId}.json";

    private static string Hash(string id) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    private static DateTimeOffset ExpiresOf(IDictionary<string, string> metadata) =>
        metadata.TryGetValue(ExpiresKey, out var ticks) && long.TryParse(ticks, CultureInfo.InvariantCulture, out var value)
            ? new DateTimeOffset(value, TimeSpan.Zero)
            : DateTimeOffset.MinValue;

    /// <summary>A lease renewed every half minute until it is disposed.</summary>
    private sealed class HeldLease : IAsyncDisposable
    {
        private readonly BlobLeaseClient _lease;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _renewal;

        public HeldLease(BlobLeaseClient lease, TimeProvider time)
        {
            _lease = lease;
            _renewal = RenewAsync(time);
        }

        private async Task RenewAsync(TimeProvider time)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(LeaseDuration / 2, time, _stop.Token);
                    await _lease.RenewAsync(cancellationToken: _stop.Token);
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
            }
            catch (RequestFailedException)
            {
                // Lost: another instance may take over, which the janitor tolerates.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _renewal;
            try
            {
                await _lease.ReleaseAsync();
            }
            catch (RequestFailedException)
            {
            }
            _stop.Dispose();
        }
    }
}
