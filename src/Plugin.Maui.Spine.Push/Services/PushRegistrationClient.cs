using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Push.Services;

/// <summary>Talks to the backend's <c>MapSpinePush</c> endpoints.</summary>
/// <param name="options">Where the backend is and how to authorize against it.</param>
/// <param name="logger">Where failures are reported.</param>
/// <param name="httpClient">The client to send with; one is created when none is given.</param>
internal sealed class PushRegistrationClient(
    SpinePushOptions options,
    ILogger<PushRegistrationClient> logger,
    HttpClient? httpClient = null)
{
    private readonly HttpClient _client = httpClient ?? new HttpClient();

    /// <summary>Registers or updates the installation.</summary>
    /// <param name="installation">What to send.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see langword="true"/> when the backend accepted it.</returns>
    internal Task<bool> UpsertAsync(PushInstallation installation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);

        return SendAsync(
            HttpMethod.Put,
            installation.Id,
            new StringContent(PushJson.Serialize(installation), Encoding.UTF8, "application/json"),
            cancellationToken);
    }

    /// <summary>Removes the installation.</summary>
    /// <param name="installationId">Which registration to remove.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see langword="true"/> when the backend accepted it.</returns>
    internal Task<bool> DeleteAsync(string installationId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, installationId, content: null, cancellationToken);

    private async Task<bool> SendAsync(
        HttpMethod method, string installationId, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, options.InstallationsEndpoint(installationId))
        {
            Content = content,
        };

        if (options.AuthorizationHeader is { } authorization &&
            await authorization(cancellationToken) is { Length: > 0 } value)
        {
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(value);
        }

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return true;

            logger.LogWarning("Spine.Push: the backend answered {Status} to {Method} {Path}.",
                (int)response.StatusCode, method.Method, request.RequestUri?.AbsolutePath);
            return false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Offline is the normal case here, not an error worth throwing at the app: the next
            // launch or foreground tries again.
            logger.LogDebug(e, "Spine.Push: could not reach the backend.");
            return false;
        }
    }
}
