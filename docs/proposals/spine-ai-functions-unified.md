# Proposal: one set of functions for every AI session

**Status:** Accepted with the owner's answers below, 2026-10-27. Asked for by the owner after testing GPT-Live, Realtime and the Claude pipeline on the iPhone.
**Plan:** `docs/plans/ai-and-security.md` (a new step after 4.7).

## The owner's requirements

1. In any session, typed or spoken, a function can be wired up **on the device** or **on the server**, or both.
2. **Every session ends by itself** when the user's intent says so ("tack, hej då").
3. Functions are **exposed the same way** everywhere and are called with **the parameters they declare**.
4. The function gets **the user's context**: who it is, and what a function needs to answer for that user.

## Summary

Each of the five session kinds builds its own list of functions, its own context and its own way to end. They were added one at a time (#632, #634, #639, #640), and each copied part of the last. As a result the same function behaves differently depending on the session kind, and an app has to configure it in several places that can disagree.

The fix has four parts:
1. **The server feature is the one catalogue.** It answers which functions a session has, with one declaration per function.
2. **One invoker on each side.** Every session kind calls the same device host and the same server runner, and both build the same context.
3. **Ending on intent is a built-in Spine function.** It is defined once and offered by every session kind, typed chat included.
4. **The context grows** with what functions need: the user's time zone and locale, the conversation and call ids, and a signed-in user that does not depend on push.

The GPT-Live part is already fixed (below). The rest is five PRs.

## What exists today

| | Typed chat, device model (Apple Intelligence) | Typed chat, server feature | Realtime (`openai`) | GPT-Live (relay) | Voice pipeline (Claude) |
|---|---|---|---|---|---|
| Where the offered list is built | `SpineFeatureChatClient.PrepareAsync`: **all** app functions | Server: the feature's `Functions` ∩ (server `Add` ∪ app offers that are `AddDevice`d) | App `ToolsUpdate`: server functions from the token + **all** app functions + `end_conversation` | Relay `Tools()`: the app's tools **as sent** + the feature's server functions | Whatever its chat client offers, + `end_conversation` in `ChatOptions.Tools` |
| Is the feature's `Functions` list honoured? | No (ignored) | Yes, both sides | Server side only | Server side only | As the chat client |
| Server functions | **Never** | Yes | Forwarded to `/ai/functions/…` | Run by the relay | As the chat client |
| Where the schema comes from | App | Server (`AddDevice` copy) for device functions | Whichever side runs it | App's, as sent | As the chat client |
| Device context built by | `SpineAIRuntime.DeviceContextAsync` | same | Its own copy in `VoiceSession` (no conversation id) | same copy | None of its own |
| Ends on goodbye | No such concept | No | Built in | Built in, through the backend | Only if the server `AddDevice`s and lists `end_conversation` by hand |
| Bad arguments | Rejected with a message | Rejected | Silently `{}` | Silently `{}` | As the chat client |
| Rounds limit | Library default | 4 per request (`MaxToolRounds`) | None | None | As the chat client |

### Gaps that break the requirements

**Requirement 1: device and server in every session**
- An on-device model (Apple Intelligence) never gets server functions (`SpineFeatureChatClient.cs:114, 119-124`). The sample's `Assistant` has `get_my_bookings` only when the server answers.
- `AddChat(IChatClient)` in a pipeline gets none of the app's functions (`VoicePipeline.cs:95-100`).
- `Prefer(Device)` is ignored in typed server chat unless the server also `AddDevice`d the function (`AIFunctionCalls.cs:96`). In voice it always works.
- If the model calls a server function and a device function in the same round, the server call may be lost (`AIFunctionCalls.cs:140-152`, `SpineAIEndpoints.cs:631-655`; no test covers it).

**Requirement 2: ending on intent**
- The hang-up exists three ways:
  - built into Realtime;
  - an app tool the GPT-Live backend calls;
  - a device function the server must declare and list by hand for the pipeline (`Program.cs:152-154`, the `Claude` feature only).
- If the pipeline's feature does not list it, the offer is dropped without a log (`AIFunctionCalls.cs:90-105`).
- The tool has three descriptions (`VoiceSession.cs:37`, `:1145`, the server's copy). Its constants are on an `internal` class, so neither apps nor servers can use them.
- "The goodbye was already said" is decided three ways, and filler such as "one moment" can set it off. GPT-Live waits up to 8 s to hang up; the others take 0.7 s.
- Typed chat has no idea of ending at all: no tool, no `Ended` event.
- The pipeline does not cancel a farewell under way when `EndsOnGoodbye` turns off.

**Requirement 3: one way to expose and call**
- `show_time` on both sides is declared in four places:
  - the app's `o.Functions.Add`;
  - the server's `ai.Functions.Add` and `.AddDevice`;
  - the name in every feature's `Functions` (five features × two settings files).
- The default side differs by where a function is registered: `Server` on the server, `Device` in the app.
- The server's `Prefer` is parsed but never used. The token's `prefer` is read and dropped (`VoiceTokenClient.cs:215`).
- Voice and the relay turn invalid JSON arguments into `{}` (`VoiceSession.cs:683-702`, `AIRealtimeRelay.cs:463-480`). The model then hears "missing X" instead of "invalid arguments".
- A server refusal in Realtime is "The function failed" for everything but sign-in (`VoiceTokenClient.cs:176-177`). The relay passes the reason on.
- Result caps differ by path: the device is always 16 000; the feature's cap is applied only on the server.

**Requirement 4: the user's context**
- The device context is built twice, differently. Voice leaves out the conversation id and names the feature by the server's name, not the app's.
- `ConversationId` is null on every voice path and in the relay. No call id is passed anywhere.
- No time zone or locale reaches any function. `show_time` run on the server answered `16:12 (Atlantic/Jan_Mayen)`. `get_my_bookings` uses the server's `DateTime.Today`.
- On the device the caller is `SignedIn` only when `ISignedInSession` is registered, which happens only with `LinkPushNotifications`. `GetUserAsync()` there gives the id but no name or email.
- The relay runs every server function of a call with the WebSocket upgrade's scope and principal, shared by concurrent calls.

### GPT-Live delegation (fixed on `fix/gpt-live-delegation-policy`)

GPT-Live's voice calls no function itself: it starts backend work with `<|SpawnThinking|>`, and only the backend calls functions.

**What went wrong.** The sample's prompt said "lämna över till backend". OpenAI's prompting guide says not to write that, and to give a "Delegation policy" with "Generate `<|SpawnThinking|>` when / Do not generate when" instead. On the iPhone the backend saw 0 tokens in a call; the voice invented the time and never hung up.

**The fix.** The relay now writes that policy from the session's tools. It also adds a developer note where the new call starts after the chat's history: the voice had taken an earlier time as current and an earlier goodbye as the end.

**Measured.** A harness on the Mac sends synthesized Swedish speech through the local relay to the real API. With the chat's history and the phrasings from the iPhone call ("Är du säker? Kolla klockan igen.", "Hejdå"), 30 of 30 turns delegated.

The harness belongs in the repo (step 5 below), not in a scratchpad.

## Design

### 1. The server feature is the catalogue

A feature's `Functions` list is the one answer to "what can this session call", for every session kind.

- **One declaration per function.** `ai.Functions.Add(SampleFunctions.ShowTime, sides: AIFunctionSides.Both)` declares the schema once. `Both` means the server can run it and the app may run it. `AddDevice` stays as the shorthand for `sides: Device`.
- **The catalogue.** The server publishes the feature's catalogue:
  - in the realtime token, as today's `tools`, extended;
  - in a new `GET {prefix}/features/{name}/functions` for chat and pipeline features;
  - for each function: name, description, JSON schema, the sides that can run it, the preferred side, and the result cap.
- **The app declares implementations, not offers.** `o.Functions.Add(ShowTime)` registers a device handler. A session offers the model what the catalogue lists. The device handler is used when the side resolves to the device and the app has one.
- **Every session kind honours the list.**
  - An app function the feature does not list is not offered.
  - A listed server function is offered everywhere, including to an on-device model: its calls go to `POST {prefix}/functions/{feature}/{name}`, which today exists only for realtime.
  - A device-only function the app lacks is left out and logged once.
- **One rule for the side:** the session's `Prefer`, else the catalogue's preferred side, else the side that can run it.

The four declarations of `show_time` become two: the server's `Add(…, Both)` with the name in the features that offer it, and the app's handler.

### 2. One invoker on each side

- **Device: `SpineAIFunctionHost`**, in `Plugin.Maui.Spine.AI.Functions`. It is used by `SpineFeatureChatClient`, `VoiceSession` (both protocols) and `PipelineVoiceSession`. It:
  - builds the context once (section 4);
  - parses arguments strictly: invalid JSON or a missing parameter answers the model with `{ error: "invalidArguments", message }`, never `{}`;
  - applies the catalogue's result cap;
  - turns exceptions into `SpineAIFunctionResult.Error`.
  This removes `VoiceSession.DeviceContextAsync`, `ParseArguments` and the copies in the relay client.
- **Server: `AIFunctionRunner`**, in `Plugin.Maui.Spine.Server.AI`. It is used by the chat feature's function-invoking client, `FunctionAsync` and the relay. It runs each call in its own DI scope with the caller's principal and the same rate, budget and cap checks. It answers refusals the same way on every path (`signInRequired`, `rateLimited`, `notFound`, `failed`), so Realtime passes the reason on as the relay does.
- **Rounds:** one setting, the feature's `MaxToolRounds`, counted per user turn on every path (today: 4 per request in typed chat, unlimited in voice).
- **A mixed round** (server and device calls in one response) is answered in full: the server runs its calls before handing the device calls back. The test that is missing today comes with this.

### 3. Ending on intent is a built-in function

- **`SpineAIFunctions.EndConversation`** is a public, built-in `SpineAIFunction` in `Plugin.Maui.Spine.AI.Functions`. It has one name, one description, and the two answers (`SayGoodbye`, `GoodbyeSaid`) as public constants. The server knows it without any `AddDevice`.
- **Every session kind offers it when the app asks.**
  - `IVoiceSession.EndsOnGoodbye` keeps its name.
  - `ChatView.EndsOnIntent` is new: typed chat raises `ChatView.Ended`, and the app decides what ending means (close the page, clear, sign out of a kiosk).
  - The server offers it to a feature's model whenever the app does, without a `Functions` entry, because it only ends the app's own session.
- **One farewell state machine** in a shared `FarewellTracker`:
  - "Already said" means the model said goodbye text or audio after the call's last user turn and *after* any filler. The tracker has the transcript of each path, so it can check the text rather than any audio.
  - It hangs up after the farewell plus 0.7 s on every path. GPT-Live's 8 s fallback stays only for a farewell that never comes.
  - Turning `EndsOnGoodbye` off cancels a farewell under way on every path.
  - The microphone is not listened to during the farewell (as now); Stop and typed text go on.
- **GPT-Live:** the relay's delegation policy comes from the catalogue. When the app toggles `EndsOnGoodbye` mid-call, the relay sends the changed lines with `session.instructions.append`, because the voice's instructions are otherwise fixed at start.

### 4. The user's context

`SpineAIFunctionContext` gains:

| Field | Device | Server |
|---|---|---|
| `TimeZone` (IANA) and `Locale` (BCP-47) | From the device | From two request headers the app sends on every AI call (`Spine-Time-Zone`, `Spine-Locale`), carried into the relay session |
| `ConversationId` | The chat's, or the voice session's own id | The same, sent with the call |
| `CallId` | The model's call id | The same |
| `Caller`, `User`, `GetUserAsync()` | Signed in whenever `Spine.Authentication` is used, without `LinkPushNotifications`; name and email from the signed-in session | As today, through `ISpineUserLookup` |

`show_time` and `get_my_bookings` then answer in the user's time zone on either side. The sample's functions show how to use it.

### 5. Proving it: one harness for every session kind

The Mac harness becomes a test project, `tests/Plugin.Maui.Spine.Voice.LiveTests`. It is skipped unless the provider keys are present, and it is run by hand before a release.

- It drives each session kind (typed chat, Realtime, GPT-Live and the pipeline) through the real server with synthesized speech (`say -v Alva`), the chat's history and a fixed script: the time twice, the bookings signed in and out, a correction, and a goodbye.
- It reports, per session kind, the functions called, the side they ran on, the context they saw (time zone, user, ids) and whether the session ended.
- The bar: every turn delegates or calls, and every goodbye ends the session.

## Order of work

| PR | What | Depends on | Size |
|---|---|---|---|
| 1 | GPT-Live delegation policy and new-call note (done, `fix/gpt-live-delegation-policy`) | — | S |
| 2 | Context: time zone, locale, ids, signed-in caller on the device; the request headers; `show_time`/`get_my_bookings` in the user's zone | — | M |
| 3 | `SpineAIFunctionHost` and `AIFunctionRunner`: one invoker each side, strict arguments, the same refusals and caps, per-call scope in the relay, rounds per turn, the mixed-round fix | 2 | L |
| 4 | The catalogue: `Add(…, sides)`, the functions endpoint and token, every session kind offers what the feature lists, server functions for an on-device model, the pipeline's raw `IChatClient` | 3 | L |
| 5 | `EndConversation` built in, `FarewellTracker`, `ChatView.EndsOnIntent`/`Ended`, the Live policy update on toggle, the stale docs | 4 | M |
| 6 | The live harness as a test project, run for every session kind | 2–5 | M |

Each PR updates `docs/wiki/ai-server.md`, `ai-client.md`, `voice.md`, `chat.md`, the skills and the sample.

## Decisions (owner, 2026-10-27)

1. **A typed chat that ends on intent** shows "Conversation ended", and the next message starts a new conversation.
2. **A function the feature does not list is refused in every session kind:** the feature's `Functions` decides everywhere.
3. **An on-device model gets the feature's server functions**, called over the network, so the same question gets the same answer whichever model answers.
4. **Time zone and locale go with every AI request**, since the server cannot know in advance which functions the model will call.
