# AI in the app

```bash
dotnet add package Plugin.Maui.Spine.AI
```

`Plugin.Maui.Spine.AI` gives the app one `IChatClient` per named feature. A feature runs on the app's [Spine.Server.AI](ai-server.md) backend, on a model on the device such as Apple Intelligence, or on the device first and then the server. App code sees the same Microsoft.Extensions.AI `IChatClient` wherever the model runs. There are no provider keys in the app: server features run with the server's keys and under its budgets.

The design is in [the proposal](../proposals/spine-ai.md), §7. To put a feature on screen as a conversation, see [Chat](chat.md).

---

## Setup

```csharp
builder.UseSpineAI(o =>
{
    o.Backend = new Uri("https://api.example.com/ai");                 // the prefix given to MapSpineAI
    o.AuthorizationHeader = ct => auth.GetHeaderAsync(ct);              // optional; read on every call

    o.Feature("Assistant", f => f
        .OnDevice(_ => OperatingSystem.IsIOSVersionAtLeast(26)
            ? new AppleIntelligenceChatClient()
            : throw new PlatformNotSupportedException())
        .ThenServer());

    o.Feature("RaceStory", f => f.Server());
    o.Feature("QuickAnswer", f => f.Server("Assistant"));              // another name for a server feature
});
```

- **Registration.** `UseSpine()` registers the package for an app that references it. The app calls `UseSpineAI(...)` to set the backend and the features, before or after `UseSpine()`.
- **Injection.** Each feature is injected as `[FromKeyedServices("Name")] IChatClient`.
- **The on-device model is the app's own reference.** `AppleIntelligenceChatClient` comes from `Microsoft.Maui.Essentials.AI`, a preview whose last build for .NET 10 is `10.0.80-preview.1.26319.4` and which needs iOS or macOS 26. Its types are marked as an evaluation preview, so add `<NoWarn>$(NoWarn);MAUIAI0001</NoWarn>`. Spine does not reference it, so an app that only uses the server pulls in nothing.

## Calling a feature

```csharp
public partial class AssistantViewModel([FromKeyedServices("Assistant")] IChatClient chat) : ViewModelBase
{
    [RelayCommand]
    private async Task Send(CancellationToken ct)
    {
        await foreach (var update in chat.GetStreamingResponseAsync(Prompt, cancellationToken: ct))
        {
            AnsweredBy ??= update.AnsweredBy();      // Device or Server, on the first update
            Answer += update.Text;
        }
    }
}
```

`GetResponseAsync` works the same way and returns one `ChatResponse`. Its `AnsweredBy()` says where it ran.

## Where it runs

The sources are tried in order:

- **Unavailable sources are skipped.** `isAvailable` on `OnDevice` is asked before every call, and `false` skips the device.
- **A source that fails before its first update falls back** to the next. Apple Intelligence that is off, not downloaded or not supported throws at once. In the iOS simulator, `AppleIntelligenceChatClient` throws an `NSErrorException` from FoundationModels (`GenerationError`, with `ModelManagerError 1026` underneath) before any text. The server's refusals fall back too, so with the server first and then the device, a spent budget sends the call to the device.
- **After the first update there is no fallback.** The user has already seen text, so a failure then reaches the caller.
- **A cancelled call never falls back.**
- **When every source fails,** the last failure is thrown. With the usual order of device, then server, that is the server's answer.
- **Each failure that fell back** is logged at Warning, with the source and the exception.

## Functions

The app's own functions are tools the model calls that run on the device: the time, the location, opening a page. They are `SpineAIFunction`s from `Plugin.Maui.Spine.AI.Functions`, declared once and often shared with the server (see [Functions on the server](ai-server.md#functions)):

```csharp
builder.UseSpineAI(o =>
{
    o.Backend = new Uri("https://api.example.com/ai");
    o.AuthorizationHeader = ct => auth.GetHeaderAsync(ct);    // so the server's functions know the user
    o.Feature("Assistant", f => f.OnDevice(_ => new AppleIntelligenceChatClient()).ThenServer());
    o.Functions.Add(SharedFunctions.ShowTime);
    o.Functions.Add("open_page", "Opens a page of the app.", (string page) => Navigate(page));
});
```

- **One set for the app.** `SpineAIOptions.Functions` and `SpineVoiceOptions.Functions` are the same set, so a function reaches text chat, on-device models and voice sessions alike.
- **On the device.** An on-device source gets every function in `ChatOptions.Tools`. Apple Intelligence runs them itself; any other device client is run through Microsoft.Extensions.AI's `FunctionInvokingChatClient`. Server-only functions are not offered to it.
- **On the server.** The request names the app's functions, and a server feature that lists one (and declared it with `AddDevice`) hands the model's call back. The client runs it and asks again with the result, all inside one `GetResponseAsync` or stream. Calls the server ran itself arrive marked `InformationalOnly` and are never run again.
- **The context.** A `SpineAIFunctionContext` parameter is filled in and left out of the schema:
  - `RunsOn` is `Device`.
  - `Caller` is the signed-in user when Spine.Authentication is used (`ISpineSignedInUser`, no push link needed), else the push link's user, else the installation. `GetUserAsync()` gives that user's name and email.
  - `TimeZone` and `Locale` are the device's own. `Now()` is the time in the user's zone.
  - `ConversationId` is the voice session's or the pipeline's own id, and `CallId` the model's id for the call when the path has it.
  - `Services` are the app's.
- **The user's context on the server.** Every request to the server carries the device's time zone and language (`Spine-Time-Zone`, `Spine-Locale`) and, from a voice session or a pipeline, its conversation (`Spine-Conversation`). The server's functions then get the same `TimeZone`, `Locale` and `ConversationId`.
- **The order.** A function both sides have runs where `SpineAIFunction.Prefer` says, and a chat changes it with `ChatOptions.Prefer(name, side)`, for example on `ChatView.ChatOptions`.
- **What the user sees.** The model tells the user what a function did, from its result. `ChatView` shows a card per call only with `ShowToolCalls="True"`, for debugging; every call is in the log.
- **Failures.** A function that throws, gets arguments its schema does not have, or answers more than 16 000 characters gives the model an error result instead (`failed`, `invalidArguments`, `tooLarge`), so the model can say so.

## Answers in one shape

The server's answers become the same exceptions the server code uses (they live in `Plugin.Maui.Spine.Common`):

| The server answers | The app gets |
|---|---|
| 404 `{ "off": "budget" }`, `"disabled"` or `"notConfigured"` | `AIFeatureOffException` with `Reason` |
| 403 `{ "off": "signInRequired" }` | `AIFeatureOffException` with `SignInRequired` |
| 429 with `Retry-After` | `AIRateLimitedException` with `RetryAfter` |
| 413 `{ "tooLarge": "turns" }` | `AIRequestTooLargeException` with `Limit` and `Max`, so a chat can offer to start over |
| A 404 without `off` (a wrong `Backend`, or a server without `MapSpineAI`) | `HttpRequestException` with the status and the full URL. A misconfigured address is never mistaken for a feature that is off |
| An `error` event in the middle of a stream | `HttpRequestException` with status 502, after the text that came before it |
| A stream that ends without its `done` event | `HttpRequestException`: the answer may be cut short |
| No `Backend` set | `InvalidOperationException` that names `SpineAIOptions.Backend` |

**Cancellation.** A call cancelled through its token always ends in `OperationCanceledException`. On iOS, `NSUrlSessionHandler` ends a cancelled read with a `TimeoutException` ("The request timed out"); the client turns that into a cancellation, so a Stop button does not show a timeout.

**In a `TaskState`.** `AIFeatureOffException` implements `IEmptyResultException`, so a feature that is off shows as empty, not as an error with a retry. `ErrorMessage` holds the reason; see [Loading states](loading-states.md).

## What the server sees

- **The request** is `POST {Backend}/features/{name}` with `{ "messages": [...], "conversationId": … }` in Microsoft.Extensions.AI's own JSON. A stream sends `Accept: text/event-stream` and is read as server-sent events.
- **`Authorization`** comes from `AuthorizationHeader` when it is set. It is read on every call, so a token that rotates can be returned from there.
- **`Spine-Installation`** carries the installation id. It is the same id Spine.PushNotifications registers with, from the same `SecureStorage` key. With push in the app, the server sees one id per installation for both. Without push, the id is made here, and push adopts it later.
- **Only exposed features answer.** On the server, the feature needs `"Expose": true`, and the server owns the prompt (see [Exposed features](ai-server.md#exposed-features)).

## On-device usage

On-device calls are counted on the device, per feature and day, and kept for 62 days in `Preferences`. They are not sent to the server; calls on the server are metered there.

```csharp
var today = usage.Today("Assistant");      // IAIUsage, injected
Status = $"{today.Calls} calls, {today.InputTokens} tokens in, {today.OutputTokens} out";
```

`ForDay(feature, day)` and `ForRange(feature, from, to)` read other days. When a model reports no usage, tokens are estimated at four characters a token. Apple Intelligence (`AppleIntelligenceChatClient` in Essentials.AI 10.0.80-preview) reports none, so its counts are always the estimate: good for comparing use over time, not exact.

## Generated images

Images a model makes once on the server ([Generated assets](ai-assets.md)) are looked up by feature and key. The server answers at once: ready with a URL, pending while it makes the image, or failed.

On any `Image`:

```xml
<ActivityIndicator x:Name="making" IsRunning="True" IsVisible="False" />
<Label x:Name="failed" Text="This postcard could not be made." IsVisible="False" />
<Image GeneratedImage.Feature="Postcard"
       GeneratedImage.Key="{Binding City}"
       GeneratedImage.Placeholder="postcard_blank.png"
       GeneratedImage.FailedSource="postcard_missing.png"
       GeneratedImage.PendingView="{x:Reference making}"
       GeneratedImage.FailedView="{x:Reference failed}" />
```

- **While pending,** the placeholder shows, and the server is asked again after its `Retry-After` (between 2 seconds and a minute; 15 seconds when the server gives none), for at most 10 minutes. After that the image shows as failed instead of a spinner that never ends, and the next time it is shown it asks again. The image shows as soon as it is ready. With `Plugin.Maui.Spine.Images` its URL goes through the image cache, and the URL changes when the server makes the image again.
- **Off screen and in the background nothing is asked.** An image starts when it is loaded, stops when it is unloaded, and waits while the app's window is stopped. On Mac Catalyst a minimised window raises no `Stopped`, so it keeps asking there.
- **Failed** covers an image the server could not make, an unknown key, a feature that is off, a backend that cannot be reached, and an image not made within the watch. `FailedSource` shows, or the placeholder when it is unset. The watch ends there and does not wait out the server's cool-down: the next time the image is shown, it asks again.
- **`PendingView` and `FailedView`** are other views, given with `{x:Reference}`, that the image shows and hides itself. They are declared before the image in the markup. MAUI bindings cannot follow an attached property, so `{Binding Path=(GeneratedImage.Status)}` does not work. The read-only `GeneratedImage.Status` and `GeneratedImage.Asset` are for the image's own triggers and for code.

In code, `IAIAssets` gives the same answers:

```csharp
public partial class PostcardViewModel(IAIAssets assets) : ViewModelBase
{
    async Task LoadAsync(string city, CancellationToken ct)
    {
        await foreach (var asset in assets.WatchAsync("Postcard", city, cancellationToken: ct))
            Picture = asset.Status == AIAssetStatus.Ready ? ImageSource.FromUri(asset.Url!) : null;
    }
}
```

- `LookUpAsync` asks once.
- `WatchAsync` asks again while the image is pending or rate limited, yields each change, and ends at ready, failed, not found or off. `AIAssetWatch` sets the intervals. A key with an empty segment is not found without asking.
- A route that does not exist (a wrong backend, or a server without `MapSpineAIAssets`) throws `HttpRequestException` with the URL, as the features do.

The lookups go to `AssetsBackend` when it is set, for images served by another service such as a Functions app of their own, and otherwise to `Backend`. They carry the same `Authorization` and installation headers as the features, so the server's per-installation order limit applies.

## Options

| Option | |
|---|---|
| `Backend` | The server's AI prefix |
| `AssetsBackend` | The prefix given to `MapSpineAIAssets`, when generated images come from another service; unset, `Backend` |
| `AuthorizationHeader` | The `Authorization` value, read on every call |
| `HttpHandler` | The handler under the client's `HttpClient`, for tests, certificate pinning or logging. The client waits up to 5 minutes for an answer |
| `Feature(name, chain)` | A feature and its sources: `OnDevice(factory, isAvailable)`, `Server(name?)` and `ThenServer(name?)` |
| `Functions` | The app's [functions](#functions), shared with `UseSpineVoice` |

## Sample

The Showcase's **AI client** page runs against the push sample server from [AI on the server](ai-server.md):

1. Start it: `dotnet run` in `samples/MauiSpinePushNotificationsSampleApp.Server`.
2. On Android, run `adb reverse tcp:5100 tcp:5100`. The page uses `http://localhost:5100/ai` everywhere.

The **Generated images** page shows postcards from the messaging sample's API (`samples/SpineMessagingSample.*`) at `http://localhost:5240/ai`. Start it with an OpenAI key (see [Generated assets](ai-assets.md#sample)), and on Android run `adb reverse tcp:5240 tcp:5240`.

**Assistant** tries Apple Intelligence first and then the server's `demo` provider, streamed. **Story** runs on a server feature with no key, which shows a feature that is off as `TaskState`'s empty state.

The **Chat** page's options pick the server's Assistant, which lists `get_my_bookings` (server, the signed-in user) and `show_time` (the phone, or the server with "Read the clock on the server"). The Showcase sends the Sign-in page's user with every call, so the bookings are the user's own; signed out, the model says to sign in.
