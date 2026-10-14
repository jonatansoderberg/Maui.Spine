using Microsoft.Extensions.AI;
using Plugin.Maui.Spine.Server.AI.Assets;

namespace SpineMessagingSample.Api;

/// <summary>
/// The app's half of the "Postcard" asset: which cities exist, and the prompt for one. Spine sees only the key's text.
/// </summary>
public sealed class PostcardPrompt : IGeneratedAssetPrompt<string>
{
    private static readonly Dictionary<string, string> Cities = new(StringComparer.Ordinal)
    {
        ["stockholm"] = "Stockholm, the old town by the water",
        ["gothenburg"] = "Gothenburg, the harbour with its cranes",
        ["kiruna"] = "Kiruna under the northern lights",
        ["visby"] = "Visby, the medieval town wall and roses",
    };

    public ValueTask<bool> AcceptsAsync(string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Cities.ContainsKey(key));

    public ValueTask<ImageGenerationRequest> BuildAsync(string key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ImageGenerationRequest(
            $"A vintage travel postcard illustration of {Cities[key]}. Flat colours, soft paper texture. No text, no letters, no people."));
}

/// <summary>Refuses an image too small to be a real picture, as a stand-in for an app's own check.</summary>
public sealed class PostcardCheck(ILogger<PostcardCheck> logger) : IGeneratedAssetCheck<string>
{
    public ValueTask<GeneratedAssetCheckResult> CheckAsync(string key, DataContent image, CancellationToken cancellationToken)
    {
        logger.LogInformation("Postcard {Key}: {Bytes} bytes of {MediaType}.", key, image.Data.Length, image.MediaType);
        return ValueTask.FromResult(image.Data.Length > 1024
            ? GeneratedAssetCheckResult.Pass
            : GeneratedAssetCheckResult.Fail($"Only {image.Data.Length} bytes."));
    }
}
