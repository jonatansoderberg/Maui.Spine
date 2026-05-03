# macOS Tray Icon & Close-to-Background Debug Log

Two bugs reported:
1. `options.MacOS.ShowTrayIcon = true` has no effect — no icon appears in the menu bar ✅ **FIXED**
2. Closing the window quits the app even when `CloseToBackground = true` ❌ **OPEN** → see GitHub issue

---

## ShowTrayIcon fix — root cause & solution

The original implementation stored raw `IntPtr` values for all NSStatusBar objects. The Xamarin/MAUI
runtime does not retain native objects that are not wrapped as managed `NSObject` instances — they
were discarded before being displayed.

The fix (mirroring the [WeatherTwentyOne reference sample](https://github.com/dotnet/maui-samples/blob/main/10.0/Apps/WeatherTwentyOne/src/WeatherTwentyOne/Platforms/MacCatalyst/TrayService.cs)):
- Store all status bar objects as `NSObject?` fields
- Wrap every native handle via `Runtime.GetNSObject()`
- Use `void`-returning P/Invoke declarations (`Void_msgSend_*`) for setter/action calls
- Use `NSVariableStatusItemLength` (`-1`) instead of `NSSquareStatusItemLength` (`-2`)
- Defer setup to `window.Activated` — AppKit status-bar APIs are not accessible during `CreateWindow`

---

## CloseToBackground — what was tried and why it failed

### Attempt A — NSWindow delegate via `window.Created` + `Task.Delay(200)`
`window.Created` does not fire reliably on Mac Catalyst. The 200 ms delay was a race condition.
The `SpineWindowDelegate.windowShouldClose:` was never called.

### Attempt B — NSWindow delegate via `window.Activated`
`window.Activated` fires correctly. The delegate was created. But `AttachWindowDelegate` found
**0 windows** because `NSApplication.sharedApplication.windows` is always empty on Mac Catalyst —
UIKit owns the windows, NSApplication has no knowledge of them.

### Attempt C — `NSApplication.keyWindow` / `mainWindow`
Both properties return `nil` on Mac Catalyst for the same reason — NSApplication does not track
UIKit-managed windows.

### Attempt D — `UIApplication.SharedApplication.Windows` → `UIWindow.nsWindow`
`UIWindow` does not publicly expose `nsWindow`. Calling the selector throws an
`ObjCException: unrecognized selector sent to instance`.

### Attempt E — `UIWindowScene._nsWindowScene.window`
`UIWindowScene` does not expose `_nsWindowScene` publicly. Calling the selector throws an
`ObjCException: unrecognized selector sent to instance`.

### Current state
`SetupCloseToBackground` wires only the `window.Destroying` cleanup (removes the status item
when the app quits). `SpineWindowDelegate` is preserved in the codebase for when the underlying
NSWindow access problem is solved.

### Possible paths forward
- Find the correct private selector that Mac Catalyst uses internally to bridge
  `UIWindowScene` → `NSWindow` (MAUI's own `WindowHandler` for Mac Catalyst does this somewhere)
- Implement `applicationShouldTerminateAfterLastWindowClosed:` on the NSApplication delegate
  to return NO, then recreate the MAUI window from the tray icon
- Override the UIKit scene lifecycle (`sceneWillDisconnect`) in the AppDelegate to cancel
  scene disconnection when the red button is clicked
