# Issue #715 — Live relay: master's delegation policy made a backend hang up on every question

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/715
**Branch:** issue/715-live-relay-unlisted-tools
**Status:** Completed

## Plan

Find why a backend built on master (0.1.37-dev.1) hung up after every question when the same relay commit on v0.1.36
behaved, then make the relay say what happened.

## Changes

- `AIRealtimeRelay.Tools` keeps the names of the app's functions the feature's catalogue does not list, logs them as a
  warning (feature, caller, names, what to do) and `session.started` carries them as `spine_tools.dropped`.
- A test: an unlisted function is left out of the backend's tools and the voice's policy, and named to the app.
- `docs/wiki/ai-server.md` §8 says so.

## Decisions

- The cause was not the delegation policy. Since #693 ("the feature is the one catalogue for every session") the relay
  offers the backend only the app functions the feature lists; AIDash's `DeskAgent` listed none, so the backend had
  `end_conversation` alone and called it for every question. That is the intended design, silently applied: the fix
  is the warning and the `spine_tools` note, and AIDash declares its device functions on the server.
- Not an error event: the session is usable, and an app that stops on `error` would lose it over a configuration slip.
