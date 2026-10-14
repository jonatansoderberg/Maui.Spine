# Plugin.Maui.Spine.Server.AI.Assets

Generated images for a Spine server, made once and served from blob storage. A lookup answers with the image's URL, or orders the image and answers 202. A Spine messaging handler makes it through a metered, budgeted `Plugin.Maui.Spine.Server.AI` image feature, runs the app's check, and archives it under a versioned path. A version bump remakes images lazily, and failures are visible to the app.

```bash
dotnet add package Plugin.Maui.Spine.Server.AI.Assets
```

```csharp
builder.Services.AddSpineAI(cfg.GetSection("Ai"), ai => ai.Image("openai", (p, model) => new OpenAIClient(p["ApiKey"]).GetImageClient(model).AsIImageGenerator()).UseInMemoryUsageStore());
builder.Services.AddSpineMessaging(m => m.UseAzureStorage().AddGeneratedAssets());
builder.Services.AddSpineAIAssets(cfg.GetSection("Ai"), assets =>
{
    assets.Asset<string, PostcardPrompt>("Postcard").Check<PostcardCheck>();
    assets.UseAzureBlobStore();
});
builder.Services.AddSpineMessagePump(GeneratedAssetMessagingExtensions.DefaultSubscriber);

app.MapSpineAIAssets();   // GET /ai/assets/{feature}/{key}: 200 ready, 202 pending, 502 failed
```

```json
"Ai": { "Features": { "Postcard": {
  "Kind": "Image", "Provider": "openai", "Model": "gpt-image-1-mini",
  "Asset": { "Version": 1, "Expose": true, "OrdersPerMinute": { "Installation": 5 } }
} } }
```

`IGeneratedAssetPrompt<TKey>` says which keys exist and builds the request, and an optional `IGeneratedAssetCheck<TKey>` refuses images that are not right. Spine never sees the data behind a key.

Documentation: [Generated assets](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/ai-assets.md).
