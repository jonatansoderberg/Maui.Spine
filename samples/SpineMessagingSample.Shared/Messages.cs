namespace SpineMessagingSample;

/// <summary>A picture to resize. The picture travels in the message, so a large one goes through blob storage.</summary>
public sealed record ResizeImage(string Name, string Picture, int Width);

/// <summary>A receipt to send once the order has settled: published scheduled, and cancellable until then.</summary>
public sealed record SendReceipt(string OrderId, string Email);

/// <summary>Bulk work at low priority: it waits while pictures are being resized.</summary>
public sealed record RebuildIndex(string Reason);

/// <summary>A job that fails its first <see cref="FailTimes"/> attempts, to show retries and failed messages.</summary>
public sealed record FlakyJob(string Id, int FailTimes);
