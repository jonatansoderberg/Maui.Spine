using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Server;

/// <summary>
/// Delivers to Firebase Cloud Messaging through the Firebase Admin SDK, in batches of
/// <see cref="BatchSize"/> registration tokens.
/// </summary>
/// <remarks>
/// Spine always sends data-only messages, never FCM's <c>notification</c> block, so the app draws
/// the notification itself and behaves the same in foreground and background (§5.2).
/// </remarks>
public sealed class FcmTransport : IPushTransport
{
    /// <summary>The most tokens FCM accepts in one multicast call.</summary>
    public const int BatchSize = 500;

    private readonly FirebaseMessaging _messaging;

    /// <summary>Creates a transport that talks to Firebase.</summary>
    /// <param name="options">The service account to authenticate with.</param>
    /// <exception cref="InvalidOperationException">The service account is missing.</exception>
    public FcmTransport(AndroidPushOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        // A named app, so a host that already created the default FirebaseApp is left alone.
        var name = $"spine-push-{Guid.NewGuid():N}";
        var app = FirebaseApp.Create(new AppOptions
        {
            Credential = CredentialFactory
                .FromJson<ServiceAccountCredential>(options.ServiceAccountJson!)
                .ToGoogleCredential(),
        }, name);

        _messaging = FirebaseMessaging.GetMessaging(app);
    }

    /// <inheritdoc />
    public PushPlatform Platform => PushPlatform.Android;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PushDelivery>> SendAsync(
        IReadOnlyList<PushInstallation> installations,
        PushEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installations);
        ArgumentNullException.ThrowIfNull(message);

        var deliveries = new List<PushDelivery>(installations.Count);

        foreach (var batch in installations.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var multicast = BuildMessage(message, batch.Select(i => i.Handle).ToList());

            try
            {
                var response = await _messaging.SendEachForMulticastAsync(multicast, cancellationToken);

                for (var i = 0; i < batch.Length; i++)
                {
                    var sent = response.Responses[i];
                    deliveries.Add(sent.IsSuccess
                        ? new PushDelivery(batch[i].Id, Platform, PushStatus.Sent)
                        : new PushDelivery(batch[i].Id, Platform, StatusFor(sent.Exception?.MessagingErrorCode), Reason(sent.Exception)));
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The whole batch failed before Firebase looked at the tokens.
                deliveries.AddRange(batch.Select(i => new PushDelivery(i.Id, Platform, PushStatus.Failed, e.Message)));
            }
        }

        return deliveries;
    }

    /// <summary>Turns one envelope and a batch of tokens into the SDK's multicast message.</summary>
    /// <param name="envelope">The message Spine built.</param>
    /// <param name="tokens">The registration tokens in this batch.</param>
    /// <returns>The message to hand the SDK.</returns>
    internal static MulticastMessage BuildMessage(PushEnvelope envelope, IReadOnlyList<string> tokens)
    {
        var message = FcmMessageReader.Read(envelope.Json);

        return new MulticastMessage
        {
            // Tokens is marked obsolete in FirebaseAdmin 3.6.0 in favour of Fids, but the two have
            // separate backing fields and only Tokens is expanded into per-device messages:
            // GetMessageList() leaves Message.Token null when Fids is set. Switching would send
            // tokenless messages, so Tokens it is until the SDK actually wires Fids up.
#pragma warning disable CS0618
            Tokens = [.. tokens],
#pragma warning restore CS0618
            Data = new Dictionary<string, string>(message.Data),
            Android = new AndroidConfig
            {
                Priority = message.HighPriority ? Priority.High : Priority.Normal,
                TimeToLive = message.TimeToLive,
                CollapseKey = message.CollapseKey,
            },
        };
    }

    /// <summary>
    /// Maps a Firebase error to what the register should do. An unregistered or malformed token is
    /// worth removing; a quota or availability error is worth retrying.
    /// </summary>
    /// <param name="code">What the SDK reported for one token, or <see langword="null"/> when it said nothing.</param>
    /// <returns>The status to record.</returns>
    internal static PushStatus StatusFor(MessagingErrorCode? code) => code switch
    {
        MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch => PushStatus.Invalid,
        MessagingErrorCode.QuotaExceeded or MessagingErrorCode.Unavailable => PushStatus.Throttled,
        MessagingErrorCode.InvalidArgument => PushStatus.Invalid,
        _ => PushStatus.Failed,
    };

    private static string? Reason(FirebaseMessagingException? exception) =>
        exception?.MessagingErrorCode?.ToString() ?? exception?.Message;
}
