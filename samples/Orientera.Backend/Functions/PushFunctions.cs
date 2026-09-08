using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Eventor;
using Orientera.Backend.Push;
using Plugin.Maui.Spine.Server;

namespace Orientera.Backend.Functions;

/// <summary>
/// The push side of the backend: where phones register, and where the results they asked to hear
/// about are noticed and sent.
/// </summary>
public sealed class PushFunctions(
    EventorSource _events,
    IPushSender _push,
    AnnouncedStore _announced,
    TimeProvider _time,
    ILogger<PushFunctions> _logger)
{
    private const string Kind = "results-published";

    /// <summary>
    /// The register. Spine owns the body and the semantics; the Function is only the door it
    /// arrives through, which is what lets the same endpoints work in an ASP.NET host too.
    /// </summary>
    [Function("PushInstallation")]
    public Task<IResult> Installation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "delete", Route = "push/installations/{id}")] HttpRequest request,
        CancellationToken cancellationToken) =>
        SpinePushEndpoints.HandleAsync(request, cancellationToken);

    /// <summary>
    /// Results are the one thing in the app no phone can notice on its own: they appear in Eventor
    /// at a moment nobody announces. So the backend watches, and the device is told.
    /// </summary>
    [Function("AnnounceResultsPublished")]
    public async Task AnnounceResultsPublished(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var competitions = await _events.GetCompetitionsAsync(cancellationToken: cancellationToken);
        var announced = await _announced.ReadAsync(Kind, cancellationToken);

        foreach (var competition in ResultsPublished.Pending(competitions, announced, now))
        {
            var result = await _push.SendAsync(
                PushTarget.Tags($"kind:{Kind} && competition:{competition.Id.Value}"),
                new PushNotification
                {
                    Title = competition.Name,
                    Body = "Resultaten är publicerade.",
                    Route = $"results/{competition.Id.Value}",
                    Channel = "competitions",
                },
                cancellationToken);

            // Marked whatever the outcome. A competition that reached nobody is not worth retrying
            // every five minutes for a day — the phones that were not registered then are not
            // registered now, and the ones that were have it already.
            await _announced.MarkAsync(Kind, competition.Id, now, cancellationToken);

            // A competition nobody is registered for is not an error, but it is the shape a lost
            // register takes: everything looks like it worked, and no phone ever hears anything.
            // It is said out loud so that "push is broken" and "nobody asked for this one" can be
            // told apart from the log alone.
            if (result.Deliveries.Count == 0)
            {
                _logger.LogWarning(
                    "Results published for {Competition}, but no installation matched {Target}.",
                    competition.Id.Value, $"kind:{Kind} && competition:{competition.Id.Value}");
            }
            else
            {
                _logger.LogInformation(
                    "Results published for {Competition}: {Sent} sent, {Invalid} invalid, {Failed} failed.",
                    competition.Id.Value, result.Sent, result.Invalid, result.Failed);
            }
        }
    }
}
