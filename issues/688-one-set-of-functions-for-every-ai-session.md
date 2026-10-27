# Issue #688 — One set of functions for every AI session: device and server everywhere, ending on intent, the user's context

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/688
**Branch:** issue/688-ai-function-context (PR 2); one branch per PR
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

## Decisions
- **The conversation id is ours, not the provider's.** `ChatOptions.ConversationId` reaches the provider as its own conversation (OpenAI Responses), so Spine's id travels in a header and an `AdditionalProperties` key.
- **On the server without a zone, UTC.** The server's own zone says nothing about the user.
- **`ISpineSignedInUser` beside `ISignedInSession`.** The push link stays opt-in (Q11). The new interface only tells the app's own functions who is signed in and sends nothing to any server.
