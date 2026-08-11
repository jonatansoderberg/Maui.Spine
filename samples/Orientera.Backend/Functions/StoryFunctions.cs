using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Orientera.Backend.Story;
using Orientera.Services.Sources;

namespace Orientera.Backend.Functions;

/// <summary>
/// The one endpoint the app posts to. It sends facts, not a race: no name, no club, no person
/// id — nothing that would make a race narrative a place where a runner's identity leaks.
/// </summary>
public sealed class StoryFunctions(RaceStoryWriter _writer, ILogger<StoryFunctions> _logger)
{
    [Function("WriteRaceStory")]
    public async Task<IResult> WriteRaceStory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stories/race")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        RaceStoryRequest? facts;

        try
        {
            facts = await JsonSerializer.DeserializeAsync<RaceStoryRequest>(
                request.Body, OrienteraJson.Options, cancellationToken);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Loppberättelsen kunde inte läsas.");

            return Results.BadRequest();
        }

        if (facts is null)
            return Results.BadRequest();

        return await Bff.ServeAsync(_logger, () => _writer.WriteAsync(facts, cancellationToken));
    }
}
