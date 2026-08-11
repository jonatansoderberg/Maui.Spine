using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Orientera.Services.Sources;

namespace Orientera.Services.Analysis;

/// <summary>The race story over the BFF, asked once per race and remembered afterwards.</summary>
/// <remarks>
/// A finished race does not change, so neither does its story. The cache is the app's half of
/// that: the backend caches the wording across phones, this keeps the tab from asking again
/// every time the runner switches back to Analys. In-memory only — a story is cheap to fetch
/// again on the next launch, and worth nothing without the result it belongs to.
/// </remarks>
public sealed class BackendRaceStorySource(HttpClient _http) : IRaceStorySource
{
    private readonly ConcurrentDictionary<string, Task<RaceStory?>> _stories = new();

    public async Task<RaceStory?> WriteAsync(RaceStoryRequest request, CancellationToken cancellationToken = default)
    {
        var key = $"{request.Class}\n{string.Join('\n', request.Lines)}";

        // Detached from the caller's token: leaving the tab must not cancel a story the runner
        // will see the moment they come back.
        var story = await _stories.GetOrAdd(key, _ => AskAsync(request));

        // Only a written story is worth keeping. A story missing because the backend was
        // unreachable should be asked for again, not remembered as "there is none".
        if (story is null)
            _stories.TryRemove(key, out _);

        return story;
    }

    private async Task<RaceStory?> AskAsync(RaceStoryRequest request)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("stories/race", request, OrienteraJson.Options);

            // 404 is the backend saying nobody is configured to write it. That is an answer.
            return response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode
                ? null
                : await response.Content.ReadFromJsonAsync<RaceStory>(OrienteraJson.Options);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// No backend, no story. The demo dataset could produce a paragraph, but a written race
/// narrative is the one thing in the app that must not be fabricated — it reads as a person
/// having watched.
/// </summary>
public sealed class NoRaceStorySource : IRaceStorySource
{
    public Task<RaceStory?> WriteAsync(RaceStoryRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<RaceStory?>(null);
}
