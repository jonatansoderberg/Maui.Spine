# Generated assets

```bash
dotnet add package Plugin.Maui.Spine.Server.AI.Assets
```

Generated images are slow (tens of seconds) and cost money, so they should be made once, kept, and served from storage. `Plugin.Maui.Spine.Server.AI.Assets` does that for any image feature of [AI on the server](ai-server.md). The work goes through [Messaging](messaging.md):

```
app: GET /ai/assets/{feature}/{key}
  ├─ the image exists → 200 { "status": "ready", "url": ".../postcard/v1/visby.png?v=1791960912" }
  ├─ it failed lately  → 502 { "status": "failed", "reason": "check", "retryAfter": "…" }   + Retry-After
  └─ it is missing     → an order on the queue (once, however many ask) → 202 { "status": "pending" } + Retry-After
handler: the app's prompt → the feature's IImageGenerator (metered, budgeted) → the app's check → blob storage
```

The lookup never waits for a model. The app shows a placeholder and asks again after `Retry-After`.

## Setup

Three registrations, in every host that orders or makes images. The messaging declaration is usually shared with the Functions app (see [Messaging](messaging.md)).

```csharp
builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai => ai
    .Image("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetImageClient(model).AsIImageGenerator())
    .UseAzureTableUsageStore(builder.Configuration.GetConnectionString("tables")!));

builder.Services.AddSpineMessaging(m =>
{
    m.UseAzureStorage();
    m.AddGeneratedAssets();          // the order message and the "spine-ai-assets" subscriber
});

builder.Services.AddSpineAIAssets(builder.Configuration.GetSection("Ai"), assets =>
{
    assets.Asset<ArenaKey, ArenaPrompt>("ArenaImage").Check<ArenaCheck>();
    assets.UseAzureBlobStore();      // ConnectionStrings:blobs, container spine-ai-assets
});

// ASP.NET Core: make the images in this host, and map the lookup.
builder.Services.AddSpineMessagePump(GeneratedAssetMessagingExtensions.DefaultSubscriber);
app.MapSpineAIAssets();              // GET /ai/assets/{feature}/{**key}
```

In Azure Functions, a trigger on the `spine-ai-assets` queue calls `dispatcher.DispatchAsync("spine-ai-assets", message, ct)`, and an HTTP function calls `SpineAIAssetEndpoints.GetAsync(request, feature, key, ct)`.

## The app's half: prompt and check

Spine never sees the data behind a key. The app says which keys exist and how to make the image for one:

```csharp
public sealed record ArenaKey(string EventId, string Season) : IParsable<ArenaKey>
{
    public override string ToString() => $"{EventId}/{Season}";
    // Parse and TryParse read the same text back.
}

public sealed class ArenaPrompt(Competitions competitions) : IGeneratedAssetPrompt<ArenaKey>
{
    public async ValueTask<bool> AcceptsAsync(ArenaKey key, CancellationToken ct) =>
        await competitions.FindAsync(key.EventId, ct) is not null;

    public async ValueTask<ImageGenerationRequest> BuildAsync(ArenaKey key, CancellationToken ct)
    {
        var competition = await competitions.FindAsync(key.EventId, ct)
            ?? throw new UnrecoverableMessageException("The competition is gone.");
        return new ImageGenerationRequest(PromptFor(competition, key.Season), [await SceneOf(competition, ct)]);
    }
}

public sealed class ArenaCheck : IGeneratedAssetCheck<ArenaKey>
{
    public ValueTask<GeneratedAssetCheckResult> CheckAsync(ArenaKey key, DataContent image, CancellationToken ct) =>
        ValueTask.FromResult(LooksRight(image.Data.Span) ? GeneratedAssetCheckResult.Pass : GeneratedAssetCheckResult.Fail("The boundary is gone."));
}
```

- **The key** is any `IParsable<TKey>`, `string` included. Its `ToString()` is the name in blob paths, so it must parse back to an equal key. It may hold letters, digits, `.`, `_` and `-`, with `/` between segments, up to 200 characters. Anything else is "not found".
- **`AcceptsAsync`** is asked before an order only, not on every lookup. It is what stops an anonymous caller from ordering images for keys that mean nothing.
- **`BuildAsync`** runs in the handler. Pass original images in the request for an edit. Throw `UnrecoverableMessageException` when the key can never be made; any other exception is retried.
- **The check** is optional. A refused image is made again (a new model call) until the attempts run out.
- The prompt and the check are resolved in a new scope, so they can take scoped services.

## Configuration

The settings live beside the feature's own, under `Asset`:

```json
"Ai": {
  "Providers": { "openai": { "ApiKey": "" } },
  "Features": {
    "ArenaImage": {
      "Kind": "Image", "Provider": "openai", "Model": "gpt-image-1",
      "Image": { "Size": "1536x1024" },
      "Budget": { "DayHard": 5 },
      "Asset": {
        "Version": 3,
        "Expose": true,
        "FailureCoolDown": "01:00:00",
        "OrdersPerMinute": { "Installation": 5, "SignedIn": 20 },
        "MediaType": "image/png"
      }
    }
  },
  "Prices": { "openai": { "gpt-image-1": { "PerImage": 0.063 } } }
}
```

| Setting | Default | What it does |
|---|---|---|
| `Version` | 1 | Part of every path. Raise it when the prompt, the model or the check changes: every key is then missing, and images are made again one at a time as they are asked for. Old versions stay in the container. |
| `Expose` | `false` | Whether `GET /ai/assets/{feature}/{key}` answers. Server code can always look up through `IGeneratedAssets`. |
| `FailureCoolDown` | 1 hour | How long a failed image answers "failed" before a lookup orders it again. |
| `OrdersPerMinute` | none | Orders per caller and minute, by the caller [Server.AI identifies](ai-server.md#who-the-caller-is). Lookups of ready and pending images do not count. |
| `MediaType` | `image/png` | Asked of the model and used for the file extension. Unset, the model's default format is kept. |

In code, `SpineAIAssetsOptions` has `ReadyCacheTime` (5 minutes: how long a ready answer is kept in memory), `PendingRetryAfter` (15 seconds) and `OrderWindow` (15 minutes: how long an order holds, which must outlast the generation and its retries; the host warns at startup when it may not). `AddGeneratedAssets(attempts: 3, maxConcurrency: 2)` sets the retries (30 seconds, then doubling up to 5 minutes) and how many images one pump makes at a time.

## Cost and abuse

- Generation runs as the server (`AICaller.System`), so the feature's and the app's budgets apply. A spent budget fails the order at once with the reason `budget`. It is not retried, and new keys answer "off" until the period ends.
- An archived image is served even while the feature is off or its budget is spent. It costs nothing to serve.
- A burst of lookups orders once. Each server remembers its own orders for `OrderWindow`, and the publish's `DeduplicationId` (`feature|version|key`) catches the rest across servers.

## Failures

The handler knows its attempts. After the last one, or at once for a spent budget, a feature that is off, or an `UnrecoverableMessageException`, it writes a failure record to a private container beside the images. The message ends among messaging's failed messages too.

An order that never reaches the handler's last attempt, such as one whose worker is killed on every delivery, leaves no record: Messaging keeps it among its failed messages, and the image answers "pending" until `OrderWindow` has passed and the next lookup orders it again.

The app sees `failed` with one of these reasons: `model`, `check`, `prompt`, `budget`, `off` or `storage`. The provider's own message goes to the log only, because it can name keys and quotas. After `FailureCoolDown` the next lookup orders the image again, and a success removes the record.

## Storage

`UseAzureBlobStore()` keeps images in the container `spine-ai-assets`, created with public read access for blobs, at `{feature}/v{version}/{key}.png`. The URL carries `?v=` from the blob's last change, so a remade image under the same path gets a new URL in the app's image cache (`Plugin.Maui.Spine.Images` caches by URL) and in a CDN.

- The storage account must allow blob public access. If it does not, the host stops at startup with a message saying so. An existing private container is logged as an error, because the app would get 403 for every image.
- With a CDN, set `PublicBaseUrl`, and `PublicAccess = false` if the CDN reads a private container.
- The connection is read as `ConnectionStrings:blobs`, the same as messaging's, or as `blobs:blobServiceUri` with a `Credential`.
- `UseInMemoryStore()` is for tests, and `UseStore(...)` takes an `IGeneratedAssetStore` of your own.

## From server code

An app's own route, with its own key, uses `IGeneratedAssets` and `ToResult`:

```csharp
app.MapGet("/competitions/{id}/arena", async (string id, IGeneratedAssets assets, IAIFeatures ai, HttpRequest request, CancellationToken ct) =>
{
    var key = new ArenaKey(id, Seasons.Of(DateTime.UtcNow));
    var asset = await assets.LookUpAsync("ArenaImage", key, await ai.IdentifyAsync(request), ct);
    return asset.ToResult("ArenaImage");
});
```

## Sample

The messaging sample (`samples/SpineMessagingSample.*`) makes postcards. Its API registers the feature `Postcard` on `gpt-image-1-mini`, with `PostcardPrompt` (four Swedish cities) and `PostcardCheck`, and runs the `spine-ai-assets` pump. Start the AppHost with the secret parameter `openaikey` set (`Parameters__openaikey` in its environment or user secrets), then `GET /ai/assets/Postcard/visby` from `send.http`. Without a key the feature is off and the lookup answers 404 `{ "off": "notConfigured" }`.

In the app, `Plugin.Maui.Spine.AI` looks the images up and shows them on any `Image` with `GeneratedImage`; see [AI in the app](ai-client.md#generated-images). The Showcase's **Generated images** page shows the sample's postcards.
