# Issue #323 — ReturnAsync accepts null and CloseAsync closes a sheet without a result

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/323
**Branch:** issue/323-returnasync-null-closeasync
**Status:** Completed
**Stage:** 1 of the app-review plan (#331)

## Plan

### Gap
`ReturnAsync(object result)` throws `ArgumentNullException` on null, which crashed the sample (#146). Orientera's login view models branch between `ReturnAsync` and `BackAsync` to avoid it. The pending `TaskCompletionSource<object?>` uses `null` to mean "dismissed", so a null result could not be told apart from a cancellation.

### Design
- `ReturnAsync(object? result)`: a returned value is wrapped in a private `Returned(object? Value)` record before it completes the pending task, so `ResolveResult` can tell "returned null" (`Success(default)`) from "dismissed" (still a bare `null` → `Canceled`). Type mismatch still throws.
- `CloseAsync()`: closes the sheet, or navigates back when the page is not a sheet, without a result. The pending result is cancelled by the existing back/close hooks, so the caller sees the same canceled outcome as a user dismissal. Named for readability in sheet code, where `BackAsync` reads wrong.

### Steps
1. `NavigationService`: wrapper record, `ReturnAsync(object?)`, `CloseAsync()`, `ResolveResult` on the wrapper.
2. `INavigationService`: signature and docs.
3. Docs: `navigation-results.md`.
4. Sample: the full-screen sheet gets a "Return null" button; the calling page shows "Result: (null)" for a successful null.
5. Build iOS and Android; verify the three outcomes (value, null, dismissed) in the sample.

## Open Questions

None.

## Changes

- `Services/NavigationService.cs`: `ReturnAsync(object? result)` wraps the value in a private `Returned` record; `ResolveResult` maps a `Returned` with null to `Success(default)`, a bare null (every dismissal path) to `Canceled`; new `CloseAsync()` closes the sheet or goes back without a result.
- `Core/INavigationService.cs`: `ReturnAsync(object?)` and `CloseAsync()` with docs.
- Docs: `navigation-results.md` states that null is a result and that `CloseAsync` yields a canceled outcome.
- Sample: the full-screen sheet has "Return null (success, no value)" and "Close (canceled)" next to "Confirm"; the calling page shows "Result: (null)" for a successful null.
- Verified on the Pixel 10 Pro emulator: Confirm → "Result: Confirmed!", Return null → "Result: (null)", Close → "Canceled". The change is platform-independent; iOS build compiles.

## Decisions

- A wrapper record instead of a separate "canceled" sentinel, because every dismissal path (back, close, sheet dismissed) already completes the task with `null` and none of them had to change.
- `CloseAsync` takes no result: `ReturnAsync(null)` is the way to say "done, nothing to give back", and keeping the two apart keeps `IsSuccess` meaningful.
