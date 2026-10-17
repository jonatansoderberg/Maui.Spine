using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.Server.Messaging;

namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>Makes one ordered image: the app's prompt, the feature's generator, the app's check, the archive.</summary>
internal sealed class GenerateAssetHandler(
    SpineAIAssetsOptions options,
    AssetSettings settings,
    IGeneratedAssetStore store,
    IAIFeatures features,
    GeneratedAssetQueue queue,
    IServiceProvider services,
    IHttpClientFactory http,
    TimeProvider time,
    ILogger<GenerateAssetHandler> logger) : IMessageHandler<GenerateAsset>
{
    public const string HttpClientName = "Plugin.Maui.Spine.Server.AI.Assets";

    public async Task HandleAsync(GenerateAsset message, MessageContext context, CancellationToken cancellationToken)
    {
        if (!options.Assets.TryGetValue(message.Feature, out var asset))
            throw new UnrecoverableMessageException($"No asset feature is named '{message.Feature}' any more.");

        var config = settings.For(asset.Feature);
        var path = AssetPaths.Of(asset.Feature, message.Version, message.Key);
        var imagePath = AssetPaths.Image(path, config);

        if (config.Version != message.Version)
        {
            logger.LogInformation("Generated assets: {Path} was ordered for version {Ordered}, now {Current}; dropped.", imagePath, message.Version, config.Version);
            return;
        }

        // Delivery is at least once, so an image made by an earlier delivery is not paid for twice.
        if (await store.GetAsync(imagePath, cancellationToken) is not null)
            return;

        var stage = GeneratedAssetFailure.Prompt;
        try
        {
            var request = await asset.BuildAsync(services, message.Key, cancellationToken);

            stage = GeneratedAssetFailure.Model;
            var generator = features.Image(asset.Feature, AICaller.System)
                ?? throw new AIFeatureOffException(asset.Feature, GeneratedAssets.OffReasonOf(features.StateOf(asset.Feature)));
            var response = await generator.GenerateAsync(request, config.MediaType is { } type ? new ImageGenerationOptions { MediaType = type } : null, cancellationToken);
            var image = await ImageOfAsync(response, cancellationToken)
                ?? throw new InvalidOperationException("The model answered without an image.");

            stage = GeneratedAssetFailure.Check;
            var verdict = await asset.CheckAsync(services, message.Key, image, cancellationToken);
            if (!verdict.Passed)
                throw new GeneratedAssetRejectedException(verdict.Reason);

            stage = GeneratedAssetFailure.Storage;
            await store.PutAsync(imagePath, image.Data, image.MediaType, cancellationToken);
            await store.DeleteFailureAsync(path, cancellationToken);
            logger.LogInformation("Generated assets: made {Path} on attempt {Attempt}.", imagePath, context.Attempt);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var (reason, final) = e switch
            {
                AIFeatureOffException off => (off.Reason == AIOffReason.Budget ? GeneratedAssetFailure.Budget : GeneratedAssetFailure.Off, true),
                UnrecoverableMessageException => (stage, true),
                _ => (stage, context.Attempt >= queue.Attempts),
            };

            // The provider's message can name keys and quotas, so it stays in the log; the app sees the reason only.
            logger.Log(final ? LogLevel.Error : LogLevel.Warning, e,
                "Generated assets: {Path} failed at the {Stage} stage on attempt {Attempt} of {Attempts}.", imagePath, reason, context.Attempt, queue.Attempts);

            if (!final)
                throw;

            var now = time.GetUtcNow();
            await store.SetFailureAsync(path, new AssetFailureRecord(reason, now, context.Attempt, now + config.FailureCoolDown), cancellationToken);

            if (e is UnrecoverableMessageException)
                throw;
            throw new UnrecoverableMessageException($"The image {imagePath} failed ({reason}).", e);
        }
    }

    private async Task<DataContent?> ImageOfAsync(ImageGenerationResponse response, CancellationToken cancellationToken)
    {
        foreach (var content in response.Contents)
        {
            switch (content)
            {
                case DataContent data when data.HasTopLevelMediaType("image"):
                    return data;

                case UriContent uri when uri.HasTopLevelMediaType("image"):
                    using (var answer = await http.CreateClient(HttpClientName).GetAsync(uri.Uri, cancellationToken))
                    {
                        answer.EnsureSuccessStatusCode();
                        var bytes = await answer.Content.ReadAsByteArrayAsync(cancellationToken);
                        return new DataContent(bytes, answer.Content.Headers.ContentType?.MediaType ?? uri.MediaType);
                    }
            }
        }

        return null;
    }
}

/// <summary>The app's check refused an image.</summary>
internal sealed class GeneratedAssetRejectedException(string? reason)
    : Exception($"The check refused the image{(reason is null ? "" : $": {reason}")}.");
