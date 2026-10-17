using Plugin.Maui.Spine.Server.Authentication;

namespace MauiSpinePushNotificationsSampleApp.Server;

/// <summary>Writes sign-in emails to the log instead of sending them. A real server sends them through its mail service.</summary>
internal sealed class LogEmailSender(ILogger<LogEmailSender> logger) : ISpineAuthEmailSender
{
    public Task SendAsync(SpineAuthEmail email, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Sign-in email ({Kind}) to {To}: code {Code}, valid until {ExpiresAt:t}.", email.Kind, email.To, email.Code ?? "none", email.ExpiresAt);
        return Task.CompletedTask;
    }
}
