# Issue #688 — One set of functions for every AI session: device and server everywhere, ending on intent, the user's context

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/688
**Branch:** one per PR: issue/688-ai-function-context (PR 2, #691, merged), issue/688-one-invoker (PR 3, #692, merged), issue/688-catalogue (PR 4)
**Status:** In Progress

## Plan
The proposal `docs/proposals/spine-ai-functions-unified.md` (accepted 2026-10-27 with the owner's answers), plan step 4.8. Six PRs in order, each merged when its tests pass (owner, 2026-10-27):
1. GPT-Live delegation policy and new-call note (#689, merged).
2. The user's context.
3. One invoker on each side.
4. The feature as the one catalogue.
5. Ending on intent built in, typed chat included.
6. A live harness for every session kind.

## Changes
### PR 2: the user's context
- `SpineAIFunctionContext` gains `TimeZone`, `Locale`, `CallId`, `SignedInUser`, `GetTimeZone()` and `Now()` (the user's zone; UTC on the server when the app sent none).
- One device builder, `SpineAIFunctionContext.ForDeviceAsync`, used by Spine.AI and Spine.Voice (the voice session's own copy is gone). It sets the device's time zone and language.
- `ISpineSignedInUser` (Common), registered by Spine.Authentication always. The device caller is the signed-in user without the push link, and `GetUserAsync()` there gives name and email.
- `SpineClientHeaders`: `Spine-Time-Zone`, `Spine-Locale`, `Spine-Conversation` on every AI request:
  - Spine.AI's requests;
  - the voice token request, the forwarded function calls and the relay socket;
  - the pipeline's requests.
  The conversation travels in `ChatOptions.AdditionalProperties["spine.conversation"]`, not `ChatOptions.ConversationId`, which is the provider's own conversation.
- The server reads them, checked, into the context on all three paths: chat, forwarded call (with the call id) and relay (with the call id).
- A voice session and a pipeline have their own conversation id per call.
- Sample: `show_time` and `get_my_bookings` answer in the user's time zone.

### PR 3: one way to run a function
- **A mixed round loses nothing.** A round with a server call and a device call came back whole to the app, and the server call was never run (a new test showed it). The server now runs its own calls in such a round at once, shows the app their results, and keeps them with the pending record. With the app's answer the model gets the server's stored result, never the app's copy.
- **Strict arguments.** `SpineAIFunctionResult.TryParseArguments` replaces the lenient parsers in the voice session and the relay. JSON that is not an object answers the model with `invalidArguments`, and the function does not run.
- **Refusals with the reason.** A forwarded call from a realtime session answers `refused` with the reason (the feature is off, too many calls) instead of "the function failed", as the relay already did. Forwarded calls now carry the client headers too (missed in PR 2).
- **A call id per call** in typed chat: `Bind(context, callId)` reads `FunctionInvokingChatClient.CurrentContext` for each call, on the device and on the server.
- **The relay runs each server call in its own DI scope**, so concurrent calls of one session no longer share the upgrade request's scope.
- **Moved to PR 4:** the device's result cap and the rounds limit per turn. Both come from the feature's catalogue.

### PR 4: the feature is the catalogue
- **One declaration per function:** `ai.Functions.Add(function, SpineAIFunctionSides.Server | Device | Both)`; `AddDevice` stays as the `Device` shorthand. The sample server declares `show_time` once with `Both`.
- **The catalogue is published:** `GET {prefix}/features/{name}/functions` (tools with `runsOn` and `prefer`, `maxToolRounds`, `maxResultCharacters`). The voice token carries the same limits.
- **Every session kind offers the feature's list and nothing else** (owner's decision 2):
  - Realtime declares only the token's functions: an app function the feature does not list is no longer offered.
  - GPT-Live sends only those, and the relay accepts an app tool only when the feature lists it for the device (besides the session's hang-up).
  - Typed server chat already did.
- **A model on the device gets the feature's server functions** (decision 3):
  - Spine.AI fetches the catalogue (kept ten minutes) and offers the app's own functions where the device runs them.
  - It offers the server's as `ForwardedFunction`s to `POST {prefix}/functions/{feature}/{name}`, which now answers any exposed feature, not only realtime ones.
  - Without a catalogue (no server source, or an older server) the device model keeps the app's functions, and that is logged.
- **Limits from the catalogue:**
  - The device's result cap is the feature's `MaxToolResultCharacters`, on every device path.
  - `MaxToolRounds` bounds the app's function-invoking loop in typed chat, and the rounds per user turn in Realtime and GPT-Live. A call past the limit answers `refused`, and a new user turn starts the count again.
- **A pipeline's own `IChatClient`** (`AddChat(client)`) runs the app's functions through `FunctionInvokingChatClient`, with the device context and the session's conversation. Spine.Voice now references `Microsoft.Extensions.AI`.

## Decisions
- **The conversation id is ours, not the provider's.** `ChatOptions.ConversationId` reaches the provider as its own conversation (OpenAI Responses), so Spine's id travels in a header and an `AdditionalProperties` key.
- **On the server without a zone, UTC.** The server's own zone says nothing about the user.
- **`ISpineSignedInUser` beside `ISignedInSession`.** The push link stays opt-in (Q11). The new interface only tells the app's own functions who is signed in and sends nothing to any server.
