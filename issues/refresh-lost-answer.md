# Sign-in: a refresh answer lost in the background signed the phone out

**GitHub:** no issue; found in Glukos on 2026-10-29. PR: https://github.com/jonatansoderberg/Maui.Spine/pull/724
**Branch:** fix/refresh-lost-answer
**Status:** In Progress

## What happened

Glukos on an iPhone (iOS 27.0.1) lost its sign-in twice in five days, with the same shape in the server log both times:

1. A background run (the app's half-hourly Health task) posted `/auth/token`. The server rotated the token
   (200, 85 ms) and the client made no request after it: iOS had ended the run as the answer came, so the app never
   got the new pair and kept the old token, in memory and in the Keychain.
2. At the next launch, 41 minutes later (50 on 2026-10-25), the app presented that old token. It was marked used,
   the 30-second `RefreshReuseGrace` was long past, and the server revoked the family: "a used refresh token was
   presented again; family … is revoked". Every request was 401 from then on, the app showed the demo, and the
   detail page sat in its loading skeleton.

`SpineAuthSession.RefreshAsync` says it: "a refresh abandoned halfway leaves the server with the token used and the
client without its successor. The server forgives one more try within its grace." The grace assumed the retry comes
within moments; in a background run it comes at the next launch, hours later.

## Plan

Tell a lost answer from reuse by what was issued, not by the clock: a used token presented again is the lost-answer
case when the family's newest token is unused and was issued for that very token. Then nobody has the answer, the
client gets a new pair, and the unused one is retired so a copy of it in other hands is still caught. Once the chain
has moved on, a retired token is a stolen copy and revokes the family as before.

On the client, a request made while the token cannot be refreshed (offline, a server down for a moment) fails as a
network error instead of going without a token and being answered 401, which apps read as "sign in".

## Changes

- `SpineRefreshToken` gains `IssuedFrom` (the hash of the token it replaced) and `SupersededAt`.
- `ISpineAuthStore.FindLatestRefreshAsync(userId, familyId)` and `SupersedeRefreshAsync(hash, now)`, with defaults
  (null, `NotSupportedException`) so a store of the app's own keeps compiling and the grace alone applies to it.
  Both built-in stores keep the newest token's hash per family (the in-memory store in a dictionary, the Azure Table
  store on the family row as `Latest`), and `IssuedFrom` / `SupersededAt` on the token row. A family row from before
  has no `Latest` until its next token, so until then reuse is judged as before.
- `SpineTokenService.RefreshAsync`: a superseded token is reuse; a used token past the grace is accepted when the
  family's latest is unused, not superseded and issued from it, which retires that latest. `IssueAsync` records
  `IssuedFrom` on a refresh.
- `SpineAuthHandler`: signed in but no token after the refresh → `HttpRequestException`, the session kept.
- Tests: `RefreshTests` (a lost answer at the next launch, lost twice in a row, a stale copy once the chain moved
  on revokes, another sign-in survives a real replay), `AzureTableStoreTests` (the family's newest token, retiring
  one), `ClientTests` (the handler fails as offline).
- Docs: `authentication-server.md` (Spine's tokens), `authentication.md` (Tokens).

## Decisions

- The lost-answer rule is judged by `IssuedFrom`, not merely "the newest is unused": a stolen copy of an older token
  presented after the honest device has rotated twice still revokes. What it tolerates: a copy stolen *and* presented
  while the honest device holds its unused successor gets one pair, which the honest device's next refresh exposes.
- The grace window stays for refreshes that cross within seconds; the new rule takes over past it.
- Presenting a superseded token revokes the family: the only way to hold one is to have read an answer the client
  never got.
- "Newest" is a pointer kept at issue, not the largest `ExpiresAt`: two tokens issued in the same instant (a sign-in
  and its first refresh in a test) tie on expiry, and the in-memory store's `MaxBy` then picked either.
