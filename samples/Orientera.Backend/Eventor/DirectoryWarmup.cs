using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Orientera.Backend.Eventor;

/// <summary>
/// Fetches Eventor's organisation list while the host is starting.
/// </summary>
/// <remarks>
/// Club names, districts and badges all come from that list, so nothing about a calendar can be
/// answered without it — and it is 2.2 MB across three thousand organisations. Cold, the first
/// request paid for the whole download before it could answer, which took longer than the app's
/// twenty-second timeout and left the runner looking at an empty screen.
///
/// The download has to happen either way; this only decides who waits for it. Starting it here
/// means the host does, in the seconds before anyone is asking.
/// </remarks>
public sealed class DirectoryWarmup(IServiceScopeFactory _scopes, ILogger<DirectoryWarmup> _logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();

            var source = scope.ServiceProvider.GetRequiredService<EventorSource>();

            await source.DirectoryAsync(cancellationToken);

            _logger.LogInformation("Organisationslistan är hämtad och cachad.");
        }
        // A backend that cannot start because Eventor is slow is worse than a cold one: every
        // other route still works, and the first real request will try again.
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Organisationslistan kunde inte värmas vid start.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
