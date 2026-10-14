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

## Options

| Option | |
|---|---|
| `Backend` | The server's AI prefix |
| `AuthorizationHeader` | The `Authorization` value, read on every call |
| `HttpHandler` | The handler under the client's `HttpClient`, for tests, certificate pinning or logging. The client waits up to 5 minutes for an answer |
| `Feature(name, chain)` | A feature and its sources: `OnDevice(factory, isAvailable)`, `Server(name?)` and `ThenServer(name?)` |

## Sample

The Showcase's **AI client** page runs against the push sample server from [AI on the server](ai-server.md):

1. Start it: `dotnet run` in `samples/MauiSpinePushNotificationsSampleApp.Server`.
2. On Android, run `adb reverse tcp:5100 tcp:5100`. The page uses `http://localhost:5100/ai` everywhere.

**Assistant** tries Apple Intelligence first and then the server's `demo` provider, streamed. **Story** runs on a server feature with no key, which shows a feature that is off as `TaskState`'s empty state.
