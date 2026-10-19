# Issue #632 — AI functions: tools the model calls, on the device or the server, with the user's context, for voice and text chat

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/632
**Branch:** issue/632-ai-functions-tools-the-model-calls-on-the-device-o
**Base:** `issue/630-voiceview-bars-and-ring-as-avatars` (PR #631, which carries #546; neither was merged on 2026-10-19)
**Plan:** `docs/plans/ai-and-security.md` (new step 2.7)
**Status:** In Progress

## Plan

### What the code does today (checked 2026-10-19)

- **Voice:** `SpineVoiceOptions.ClientTools` (`IList<AIFunction>`, not in v0.1.15). `VoiceSession.ToolsUpdate()` sends `session.update` with `end_conversation` plus `ClientTools`, which *replaces* the session's tool list. `CallToolAsync` runs the tool with only `AIFunctionArguments(call.Arguments)`: no services, no caller, no cancellation tied to a context.
- **Text chat:** `SpineServerChatClient` posts `{ messages, conversationId }`. `SpineAIEndpoints.FeatureAsync` → `ExposedMessages` keeps the text of user and assistant messages only. The server feature pipeline (`AIFeatureRegistry.BuildFeature`: vendor → OpenTelemetry → logging) has no `UseFunctionInvocation`, and neither does `SpineFeatureChatClient`.
- **Realtime:** `RealtimeSessionAsync` answers `{ feature, protocol, model, url, token, expiresAt, maxMinutes }`; `OpenAIRealtimeProvider` sends no tools to `client_secrets`.
- **Caller:** `AICaller(Id, Kind)` in `Server.AI`. `SpineAuthUser` and `IUserResolver.FindByIdAsync` are in `Server.Authentication`, which `Server.AI` does not reference. `SpineUser` (the app's view of a user) and `ISignedInSession` are in `Common`.
- **Apple Intelligence:** `AppleIntelligenceChatClient` (Essentials.AI 10.0.80-preview.1) has an `AIFunctionToolAdapter` and runs `AIFunction`s from `ChatOptions.Tools` itself.
- **M.E.AI 10.10:** `FunctionInvokingChatClient` returns a call to an `AIFunctionDeclaration` (not an `AIFunction`) to its caller instead of running it, and skips `FunctionCallContent.InformationalOnly` calls. Both are what the device channel needs.
- **The Showcase's Assistant** runs on the `demo` provider in both appsettings files, which never calls a tool.

### Design

**1. One declaration: `SpineAIFunction`** (package `Plugin.Maui.Spine.AI.Functions`)

```csharp
// In a shared project (net10.0), referenced by the app and the server.
public static class SampleFunctions
{
    public static SpineAIFunction GetMyBookings { get; } = SpineAIFunction.Create(
        "get_my_bookings", "Lists the signed-in user's bookings.", GetMyBookingsAsync, prefer: SpineAIFunctionSide.Server);

    public static SpineAIFunction ShowTime { get; } = SpineAIFunction.Create(
        "show_time", "Says what time it is on the user's phone.", () => DateTime.Now.ToString("HH:mm"));

    static async Task<object> GetMyBookingsAsync(SpineAIFunctionContext context, int? days)
    {
        if (await context.GetUserAsync() is not { } user) return SpineAIFunctionResult.SignInRequired();
        …
    }
}
```

- `SpineAIFunction.Create(name, description, Delegate method, prefer)` or `SpineAIFunction.From(AIFunction, prefer)`. Parameters of type `SpineAIFunctionContext`, `CancellationToken` and `IServiceProvider` are bound from the call and left out of the schema (`AIFunctionFactoryOptions.ConfigureParameterBinding`), so the model never sees them.
- `Prefer` is the function's default order: which side runs it when both sides offer it. It can be overridden per instance (below).

**2. The context: `SpineAIFunctionContext`**

- `Caller` (`AICaller`), `Feature`, `ConversationId`, `RunsOn` (`SpineAIFunctionSide`), `User` (`ClaimsPrincipal`; an empty principal for installations and on the device), `Services`, `CancellationToken`, `IsSignedIn`, `Task<SpineUser?> GetUserAsync()`.
- On the server it is made per call from the `HttpContext`. On the device the caller is the installation, or the signed-in user's id through `ISignedInSession` when Authentication is there.
- `SpineAIFunctionResult.SignInRequired()` / `.Error(message)` give the model a plain `{ "error": "signInRequired", "message": "…" }`, so it tells the user to sign in.

**3. The registry**

- Server: `AddSpineAI(section, ai => { ai.Functions.Add(SampleFunctions.GetMyBookings); ai.Functions.AddDevice(SampleFunctions.ShowTime); })`. `AddDevice` registers the declaration only: the server never runs it, but can offer it to the model in text chat.
- Feature config: `"Functions": ["get_my_bookings", "show_time"]` and `"MaxToolRounds": 4` (default 4). A feature offers only the functions it lists. A name it lists that nothing registered is logged at startup.
- App: `UseSpineAI(o => o.Functions.Add(...))` and `UseSpineVoice(o => o.Functions.Add(...))` write to one registry (`SpineAIFunctions`, a singleton both packages share). `SpineVoiceOptions.ClientTools` is removed (unreleased).

**4. Server text chat: `UseFunctionInvocation`**

- `AIFeatures.Chat(feature, caller)` wraps `CallerChatClient` in a per-call `FunctionInvokingChatClient` when the feature lists functions. Function invocation sits *outside* the gate and the meter, so **every round is gated and metered** against the caller's budget, rates and `ConcurrentPerCaller`. `MaximumIterationsPerRequest = MaxToolRounds`.
- Tools per call: the feature's server functions as `AIFunction`s bound to a `SpineAIFunctionContext`, plus the device functions the client offered (below) as `AIFunctionDeclaration`s.
- Server-side calls and results are streamed to the app with `InformationalOnly = true`, so the app's own function invocation never runs them again, and `ChatView` can show them as debug cards.
- The keyed `[FromKeyedServices] IChatClient` (`AICaller.System`) gets the same, with a context whose caller is System.

**5. Exposed chat: device calls through a separate opt-in channel**

- The request body gets `functions: [{ "name": "show_time", "prefer": "device" }]`: the names of device functions the app has, never their schemas or descriptions. The server offers only names the feature lists *and* registered with `AddDevice`, with the server's own declaration.
- When the model calls one, the server ends the response with that `FunctionCallContent` and keeps the call (`callId`, name, arguments, caller key, feature, expiry 5 min) in a pending-call store: in memory, or the host's `IDistributedCache` when registered, for scale-out.
- The app's `SpineServerChatClient` sits under a `FunctionInvokingChatClient` with the device functions, which runs the call and posts the conversation again with the call and its result.
- `ExposedMessages` still drops every client tool message, **except** a `FunctionResultContent` whose `callId` is pending for this caller and feature. The server then puts back its *own* recorded `FunctionCallContent` (not the client's), caps the result's size, and removes the pending entry. A forged or replayed result is dropped and logged. The exposed-chat decision of 2026-10-12 stands for everything else.
- When a function is on both sides, the side is chosen per call: the request's `prefer`, else the function's `Prefer`.

**6. Realtime: the token lists the tools, the app forwards server calls**

- The token response gets `tools: [{ name, description, parameters, runsOn: "server" | "device" }]`: the feature's functions. The same declarations go into the `client_secrets` session config.
- `VoiceSession` builds its `session.update` from `end_conversation` + the token's tools + the app's own device functions (so `show_time` keeps working whatever the server lists). For a function on both sides, the session's order decides.
- A server call is forwarded to `POST {SessionEndpoint}/functions/{feature}/{name}` with `{ callId, arguments }` and the session's `Authorization`. `SpineAIEndpoints.FunctionAsync` checks that the feature is exposed and lists the function, applies the feature's `Callers`, `RequestsPerMinute` and `ConcurrentPerCaller`, validates the arguments (unknown, missing required and wrongly typed arguments answer an error result), runs it with the context, caps the result and records the call in the usage report at no cost. It answers `200 { result }`, including `signInRequired` results, because the result is for the model. A refusal (403/404/429) becomes an error result for the model in the app.
- Mapped by `MapSpineAI` (and callable from a Functions `[Function]`). No relay.

**7. Per-instance order**

- Voice: `IVoiceSession.Prefer(string name, SpineAIFunctionSide side)`, set before or during a session (a change sends a new `session.update`).
- Chat: `ChatOptions.Prefer(name, side)` (an extension writing to `AdditionalProperties`), so `ChatView.ChatOptions` sets it for one conversation.

**8. On-device models**

- `AIFeatureChain.OnDevice(...)` gets the device functions in `ChatOptions.Tools`, bound to the device context. Apple's client runs them itself. A device client that does not is wrapped in `FunctionInvokingChatClient` (checked with `GetService<FunctionInvokingChatClient>()`; Apple's behaviour verified before choosing).
- Server-only functions are not offered to an on-device model.

**9. Logging and debug**

- Every call logs at Information: feature, function, side, caller key, duration, result size, and failures with the exception. Arguments and results at Debug only, or with the feature's `EnableSensitiveData`.
- The OpenTelemetry spans from `FunctionInvokingChatClient` stay.
- No confirmation UI. The model reads the return value and tells the user.

**10. The Showcase and the sample server**

- New `samples/MauiSpineSampleApp.Shared` (net10.0) with `SampleFunctions`: `get_my_bookings` (made-up bookings seeded from the user's id, with their email and name from the context) and `show_time`.
- Sample server: `ai.Functions.Add(GetMyBookings)`, `AddDevice(ShowTime)`; `Assistant` and `Voice` list both (the demo provider calls them locally; the Azure Assistant runs on gpt-5.4-nano).
- Showcase: `o.Functions.Add(ShowTime)` in `UseSpineAI` and `UseSpineVoice`. The AI client page and Voice chat get a "Run get_my_bookings on: Server / Device" switch to show the per-instance order (a device stub of `get_my_bookings` that says it ran on the phone), and the call in the page's log. Code tabs updated.

### Steps

1. Contract package + `AICaller` move (type-forwarded), `SpineAIFunction`, `SpineAIFunctionContext`, results, binding. Tests.
2. Server: registry, feature `Functions` / `MaxToolRounds`, `UseFunctionInvocation` per call, context from `HttpContext`, `GetUserAsync` through Authentication, logging. Tests: rounds metered, budget stops a loop, sign-in result, context reaches the function, schema hides the context.
3. Server: the device channel (pending calls, accepted results, forged/replayed/other-caller results dropped). Tests.
4. Server: realtime tools in the token and `client_secrets`, `POST /ai/functions/{feature}/{name}` with and without sign-in, validation, caps, rates. Tests.
5. App AI: shared registry, `SpineServerChatClient` sends offers and runs device calls, `OnDevice` gets tools, `Prefer`. Tests in `Plugin.Maui.Spine.AI.Tests`.
6. Voice: `Functions` replaces `ClientTools`, token tools, forwarding, `Prefer`. Tests in `Plugin.Maui.Spine.Voice.Tests`.
7. Samples: shared project, server, Showcase pages and code tabs.
8. Docs: ai-server.md, ai-client.md, voice.md, the spine-controls / spine-setup skills, the plan (step 2.7, decisions, Last updated, verification backlog), spine-voice.md §4/§6.2 rule 8 status.
9. Verification: the simulator (text chat and voice against the local sample server on a port other than 5100), Apple Intelligence on the Mac (Catalyst) or the iPhone, signed in and out; the Azure deploy only after asking.

## Open Questions

(none: answered by the owner on 2026-10-19, see Decisions)

## Changes

- **`Plugin.Maui.Spine.AI.Functions`** (new, net10.0): `SpineAIFunction` (`Create`, `From`, `Bind`, `Declaration`, `InvokeAsync`, `Prefer`), `SpineAIFunctionContext` (`Caller`, `Feature`, `ConversationId`, `RunsOn`, `User`, `Services`, `CancellationToken`, `MaxResultCharacters`, `GetUserAsync`), `SpineAIFunctionResult` (`SignInRequired`, `Error`), `SpineAIFunctions` (the set), `SpineAIFunctionOrder.Prefer(ChatOptions, …)`. Bound calls check the arguments against the schema, log, answer errors and cap results; results are compact JSON. `AICaller`/`AICallerKind` moved here, type-forwarded from Server.AI.
- **Common:** `ISpineUserLookup`. **Server.Authentication** registers it over `IUserResolver` (`SpineUser`, never the password hash).
- **Server.AI:** `SpineAIOptions.Functions` (`SpineAIServerFunctions`: `Add`, `AddDevice`); feature settings `Functions`, `MaxToolRounds`, `MaxToolResultCharacters`; `FeatureFunctionsChatClient` (a per-call `FunctionInvokingChatClient` outside the gate and meter, the context from the `HttpContext`, server calls marked informational, device calls kept as pending); `AIPendingCalls` (memory or `IDistributedCache`); `ExposedMessagesAsync` takes a result only for a pending call of this caller; the body's `functions` offers; the realtime token's `tools` and the `client_secrets` session's tools; `POST /ai/functions/{feature}/{name}` (`SpineAIEndpoints.FunctionAsync`) with a limiter lane of its own; a startup warning for unregistered names; no response cache with functions.
- **Spine.AI:** `SpineAIOptions.Functions` (shared with Voice); on-device sources get the functions as tools through `FunctionInvokingChatClient`; server sources send the offers and run handed-back device calls; non-own calls marked informational. References Microsoft.Extensions.AI (for `FunctionInvokingChatClient`).
- **Spine.Voice:** `SpineVoiceOptions.Functions` replaces `ClientTools`; `VoiceSessionToken.Tools`; `session.update` from the token's tools and the app's; server calls forwarded with the app's sign-in; `IVoiceSession.Prefer`; compact JSON to the model.
- **Controls.Chat:** `ChatView.ShowToolCalls`, off by default.
- **Voice:** a goodbye said in the same response that calls `end_conversation` is the farewell: no second response is asked for, and the call ends once it has played (the owner heard "Vi hörs! Ha det bra, vi hörs!" on the iPhone).
- **Controls.Chat:** an answer that spans several messages (a voice transcript's turns, the text after a function ran) starts a paragraph between them instead of running them together.
- **Avatar:** `VoiceAvatarDriver` resets speech when the answer has played: the last cue sat under a playhead that had stopped and held the mouth open (seen by the owner on the iPhone).
- **Voice + Chat:** the user's committed turn is a pending message (`spine.pending`, `ChatView.PendingKey`) that ChatView shows as a shimmering bubble until the words arrive, instead of "…".
- **Core:** a sheet closes the keyboard of the page underneath before it opens (iOS `EndEditing`, Android hides the IME and clears focus): the Chat page's composer kept its keyboard over the Options sheet (seen by the owner on the iPhone).
- **Samples:** `samples/MauiSpineSampleApp.Shared` (`SampleFunctions.GetMyBookings`, `ShowTime`); the push sample server registers them, `Assistant` and `Voice` list them, the demo provider calls them, `send.http` has the three requests, the Azure `Assistant` runs on `gpt-5.4-nano`, the per-minute limits are higher; the Showcase sends the signed-in user's header from Spine.AI and Spine.Voice, the Chat page picks Demo / Assistant / Server with "Read the clock on the server" and "Show function calls", the Voice chat page gets the same two options, code tabs updated.
- **Tests:** Server.AI `FunctionTests` (21: context, schema, offers, signed out, rounds metered, a spent allowance stops the rounds, informational calls, the device round trip with replay/forgery/another caller, the order, realtime tools, the forward endpoint with and without sign-in, 404s, argument checks) and `FunctionContractTests`; AI `FunctionClientTests` (the order both ways, streamed and not, server calls not rerun, on-device tools and context); Voice `SessionTests` (token tools declared, forwarding with sign-in, sign-in refusal, `Prefer` switching sides, a device function the app lacks).
- **Docs:** ai-server.md (Functions, device functions in text chat, functions in a voice session, Azure Functions), ai-client.md (Functions), voice.md, chat.md, packages.md, README, the spine-setup and spine-controls skills, spine-voice.md's status, the plan (step 2.7, decisions, verification backlog).

## Verification

- All tests pass: Server.AI 112 (+5 skipped), AI 46, Voice 68.
- Against the local sample server (port 5199, demo provider), with curl: signed out `get_my_bookings` answers `signInRequired` and the model reads it; signed in (email and password) it answers the user's name and email from Server.Authentication.
- In the iPhone 17 Pro simulator, the Showcase's real clients through a scratch harness: `show_time` called by the server's model ran on the phone and the server took the result; `get_my_bookings` signed out asked for sign-in, signed in (through the app's sign-in) answered the user's bookings; the calls the server ran came back informational.
- Against Azure after a deploy (2026-10-19), signed out, in the iPhone 17 Pro simulator through the Showcase's real clients:
  - Text chat (gpt-5.4-nano): `get_my_bookings` → "logga in på Sign-in-sidan"; `show_time` ran on the phone, and on the server with `ChatOptions.Prefer`.
  - A Voice session (gpt-realtime, typed turns with the microphone muted): the token's tools were declared; `get_my_bookings` was forwarded to `/ai/functions/Voice/get_my_bookings` and the model said to sign in; `show_time` ran on the phone, then on the server after `Prefer("show_time", Server)`.
  - The first deploy failed every tool call: gpt-5.4-nano refuses function tools with `reasoning_effort` on chat completions, so the Azure `Assistant` has `"Reasoning": "None"` (noted in ai-server.md).
- Not seen: Apple Intelligence with a tool (the simulator's FoundationModels fails even without tools; the Mac's model is not downloaded), and the signed-in case against Azure (no account may be made there by Claude). In the plan's verification backlog.

## Decisions

- **2026-10-19, owner: the contract is its own package, `Plugin.Maui.Spine.AI.Functions`** (plain net10.0: Common + M.E.AI.Abstractions). AI, Voice, Server.AI and a shared project reference it. `AICaller` and `AICallerKind` move there with `[TypeForwardedTo]` from Server.AI, keeping their namespace `Plugin.Maui.Spine.Server.AI`. **Why:** the app and the server must share one declaration, and Common should not pull M.E.AI into every app.
- **2026-10-19, owner: `GetUserAsync()` returns `SpineUser`**, Common's contract, through a lookup Server.Authentication registers. Server-only code can resolve `IUserResolver` for the full `SpineAuthUser`. **Why:** a function in a shared project cannot see `SpineAuthUser`, and it should not see the password hash anyway.
- **2026-10-19, owner: the demo provider calls the functions it is given, and the Azure sample server's `Assistant` moves to `openai` / `gpt-5.4-nano`.** **Why:** local runs and tests need no key, and the hosted Showcase shows a real model choosing the tool.
- **2026-10-19, owner: the types are named `SpineAI…`** (`SpineAIFunction`, `SpineAIFunctionContext`, `SpineAIFunctionSide`, `SpineAIFunctionResult`, `SpineAIFunctions`), not `Spine…`; the rest of the names stand, and `ClientTools` is removed. The owner wrote "SpineAi"; the code keeps the `AI` casing of `SpineAIOptions` and `SpineAIRuntime`.
