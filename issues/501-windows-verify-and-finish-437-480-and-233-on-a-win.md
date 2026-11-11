# Issue #501 — Windows: verify and finish #437, #480 and #233 on a Windows machine

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/501
**Branch:** issue/501-windows-verify-and-finish-437-480-and-233-on-a-win
**Status:** Completed (what needs a person or another machine is listed under "not done" and left open in #501's closing comment)

## Plan

The checklists are the issue body and its seven comments (PR #502/#437, #503/#480, #504/#233 and their review-fix updates). Owner's choices (2026-11-10): verify **only what can be automated**. Everything that needs eyes or hands (crispness, hover and pressed colours, Narrator, dragging the window) stays an open box in #501. Push is tested **unpackaged, locally**, and everything goes in **one PR**. The branch starts from `build/sdk-roll-forward` (the `global.json` change, its own PR) so it builds on this machine.

### The machine
- Windows 11 Pro, SDK 10.0.403 with the newest MAUI workloads (Microsoft.Android.Sdk 36.1.118), Windows App Runtime 1.8 installed.
- 3584 × 2240 at **200 %** on *Microsoft Basic Display Adapter*, apparently a VM without GPU acceleration. Mica/Acrylic and anything timing-sensitive may not look like a real PC. A single monitor, so the per-monitor scale checks and the 125/150 % checks can't be done here.
- The agent's shell is sandboxed. Apps and scripts that touch the app run in a Terminal tab. An app started from there opens behind other windows.

### How
- **Harness, not committed.** PowerShell scripts in the session scratchpad: start the Showcase (`MauiSpineSampleApp`, Debug, `net10.0-windows10.0.19041.0`), drive it through UI Automation (`System.Windows.Automation`: find by name/automation id, Invoke/Toggle/Value/ExpandCollapse patterns, keys via `SendInput` only when the window is in front), read element state, and capture the window and its popups with `PrintWindow`/screen copy for colour and layout checks.
- **Logs.** The Showcase logs to the debugger. A temporary file logger in `MauiProgram.cs` (local only) makes the `Plugin.Maui.Spine.Menus`, `.Search` and `Spine.PushNotifications` lines readable.
- **Temporary code** that a check needs (a disabled menu item, a missing SVG, the app's own `TitleBar.Content`, `SearchInTitleBar = false`, a tab variant, remote push on) is made locally and reverted. Only real fixes are committed, each with a line in *Changes*.
- **Result.** One comment on #501 per part, each box checked (verified here), crossed out (doesn't apply any more), or left open with the reason. The issue body's checkboxes are updated to match.

### #437 — Menu icons (`Platforms/Windows/MenuExtensions.Windows.cs`, `SvgIcon.GetPngFilePath`)
1. Builds on Windows: done by the Release build of `Spine.Packages.slnf` (PRI on) and the Showcase Debug build.
2. Menus, Context menus and Action sheets pages: open each menu, and check that `%LOCALAPPDATA%\SvgIconCache\*_32px.png` files are written (200 %) and that each item has an icon (UIA, capture).
3. Light/dark through the Showcase's theme setting (#789), before and after building a menu (live switch). Compare icon and text colour in the capture, destructive items Firebrick.
4. Temporary `IsEnabled = false` on "Edited" (`MenusPage.ViewModel.cs`): dimmed like the text.
5. Empty flyout filled in `Opening` shows on the first click; Sort/Filter checks follow the model after reopen.
6. Temporary `Svg = "does-not-exist.svg"`: one warning, no exception. Unwritable cache folder: error logged, no crash, no `*.tmp`.
7. Left open: hover/pressed colours, 125/150 % crispness, per-monitor rebuild, multi-colour SVG judgement.

### #480 — Search in the title bar (`SpineApplication.Search.Windows.cs`, `Presentation/SearchLayout.cs`)
1. Search page: the field is in `TitleBar.Content` and the row below the header bar is gone; the title bar is still 32 tall (UIA bounds).
2. Typing filters and the count updates; Enter submits; the clear button; "Clear from code"; "Start a search" focuses; "Show the search field" off/on; "Always in a row (Top)".
3. Swapping: back to the main page and in again, "Open as a sheet" and closing it, navigating away while typing.
4. The app's own `TitleBar.Content` (temporary), `SearchInTitleBar = false`, and a replaced `Window.TitleBar` → error logged, field in the row.
5. Tabs and host swap: a temporary tab variant of the Showcase root (`[NavigableTab]`) with the Search page in one tab and a `SetRootAsync` button. Covers the tab, host-swap, quick-succession and backdrop-on-swapped-host checks.
6. With the `Spine.Windows` options: hide to tray and restore, a second launch (single instance), `Backdrop = None`/`Acrylic`, fullscreen toggle.
7. Tab into the field, Esc behaviour (report; whether Spine should end the search on Esc is the owner's call).
8. Left open: dragging and double-click on the title bar, Narrator, how readable the field is over Mica (judgement).

### #233 — Windows push client (`Plugin.Maui.Spine.PushNotifications/Platforms/Windows`)
1. Local only, not committed: `SpinePushNotificationsRemote=true` and `WindowsAppSDKSelfContained=false` for the Windows TFM in the Showcase, `RemoteId = d03bb049-fead-405e-91c9-e54855f78b12`, and the push base URL pointed at the local `MauiSpineSampleApp.Server`.
2. The owner creates the machine's own WNS secret (`az login`, then `az ad app credential reset … | dotnet user-secrets set`, with the project path now `samples/MauiSpineSampleApp.Server`). The agent never sees it.
3. Without credentials: the build warning with `WindowsAppSDKSelfContained=true`; Unsupported for an empty remote id and for a self-contained build (log reasons); local notifications unpackaged → `IsSupported` false, `need package identity`.
4. With credentials: a channel URI `https://*.notify.windows.com/?token=…` in the Push page and the same handle in the server's `/installations`. Then a wrong remote id (log the HRESULT), restart and renewal, the 10 s timeout offline, one `CreateChannelAsync` per launch, and permission status via Windows settings.
5. Delivery: silent push in the foreground and minimised (`IsForeground`), a raw alert by hand, a WNS toast while running and with the app closed (cold start → `OnOpenedAsync`), tap while running and with single instance, server buttons `ack`/`open`, `CollapseId` replacement (`X-WNS-Tag`), a 410 removing the installation. Toasts are checked in the Notification Center through UIA where possible.
6. Left open: the MSIX path (PFN mapping mail to Microsoft, activators, packaged local notifications), and running elevated (needs an admin launch by the owner).

## Open Questions

- ~~Esc in the title bar's search field~~: answered 2026-11-10, Esc ends the search and keeps the text (finding 6).

## Verified

### #437 (2026-11-10, Showcase Debug, 200 %, app theme Dark and Light)
- Builds on Windows: `Spine.Packages.slnf` Release (all four TFMs) and the Showcase Debug for `net10.0-windows10.0.19041.0`.
- Icons appear in a menu button's flyout (Menus page `more.svg`), its submenu ("Media types"), the pop-up button (Sort) and header page-action menus (Theme and accent, Race actions). `BitmapIcon` from a `file:///` path works unpackaged.
- Size: 16 epx, aligned with the text. The files are `%LOCALAPPDATA%\SvgIconCache\{name}_{hash}_32px.png` at 200 %, and they look crisp.
- Dark: icons white like the text (no longer black). Light: black like the text.
- Live theme switch: the Menus page's `more.svg` menu, built in Dark, then the app theme set to Light from the header menu without leaving the page. The icons and the flyout follow.
- Destructive ("Reset") icon is the item's red; disabled ("Edited", temporarily `IsEnabled = false`) is dimmed like its text.
- The empty flyout is filled in `Opening` and shows on the first click. Sort shows its pick ("Date", "Sorted by date") and the check follows on reopen; the filter picker and the submenu toggles follow the model.
- `Svg = "does-not-exist.svg"` (temporary): the item shows without an icon, with one warning `Menu icon does-not-exist.svg is not embedded…` in `Plugin.Maui.Spine.Menus`, and no exception.

- After the fixes for findings 1 and 2 (mouse clicks): the Context menus page's card (right-click: Follow, Add to calendar, Share with icons; left-click still opens the race) and rows (Copy name, Share, Remove in red; Share reaches the row it was opened on). The Action sheets page's "Race actions": title rows dimmed, icons, Remove in red, the pick returned ("Removed the night sprint").

### Findings 1 and 2 after the fix (mouse, 2026-11-10)
- Start page (hero): "Filter samples" opens its menu and "Settings" opens Settings; dragging the hero photo still moves the window. The passthrough regions are the two buttons and the list's scroll bar where it lies over the photo.
- A page with the title bar hidden (Settings, Menus, Context menus): Back and the palette button take clicks; dragging the header bar's empty part moves the window. The caption region is MAUI's title bar band again (`[0,0 498x64]`), not the start page's hero.
- No unobserved exceptions at start (finding 4), and no OpenSans errors.

### #480 (2026-11-10, Showcase Debug, 500 × 800, 200 %, mouse and keyboard)
- The Search page's field is in the title bar (`TitleBar.Content`), 32 tall, between the subtitle and the palette button; no row below the header bar. Its passthrough region makes it clickable (with finding 2's fix).
- Typing filters ("lu": 3 of 38); Enter submits ("lu": Falun first). Clicking in the middle of the narrow field hits the query button and submits the empty text (see finding 6).
- Clear from code empties it. Start a search focuses it; a click elsewhere takes the focus away. Show the search field off: gone from the title bar. Start a search while off: it appears focused and goes again when the focus leaves. Always in a row on: a row below the header bar (910 × 88 physical); off: back in the title bar.
- Typing, then Back: the main page has no field; Search again: an empty field.
- Open as a sheet: no field in the title bar, the sheet's own row filters the sheet's list (9 of 38 while the page shows 38). Closing by its X and by a click on the dimmed page both bring the title bar field back.
- Keyboard: Tab goes Back → field → palette → content. Esc in the field keeps the text and the focus (the `AutoSuggestBox` default; whether Spine ends the search on Esc is the owner's call).
- Single instance with `CloseToBackground`: Close hides the window; a second launch exits and brings it back, the field still holding "ma" and its filter.

- Temporary variants (environment variable `SPINE_501` read in `MauiProgram`, and `[NavigableTab]` on the start and Search pages; all reverted):
  - The app's own `TitleBar.Content` ("App content", title bar shown on every page): shown on the start page, replaced by the field on Search; content set by the app while Search shows leaves the field alone, and is what shows after Back.
  - `SearchInTitleBar = false`: the app's content stays on Search, the field is in the row.
  - `Window.TitleBar` replaced by the app's own: the field is in the row, one error in `Plugin.Maui.Spine.Search` names `SearchPageViewModel` and `SearchInTitleBar`, no exception.
  - `Backdrop = Acrylic`: title bar and field readable. `Backdrop = None`: the title bar was black, its buttons and the field's box invisible (finding 10, fixed).
  - Tabs (`Main page`, `Search` as `[NavigableTab]`, Windows' `NavigationView` tabs under the title bar): the field follows the selected tab and leaves on the other. `SetRootAsync<SettingsPage>` (tab host → plain host): the title bar hides with Settings, its page action ("Tabs") shows in the header bar, no field. `SetRootAsync<MainPage>` back: tabs, title bar and actions return; the Search tab's field is back and filters ("up": 2 of 38).

### #480, second round (2026-11-11, owner's "verify the rest now"; real mouse input and screen captures from the harness)
- Full screen (temporary flag file read by the Showcase, calling `ToggleFullscreen`): the title bar went and took Back, the palette button and the field with it (finding 18, fixed). Now: on Search, Back, the field (in the row) and the palette button are in the header bar; Settings (title bar hidden) is not cut off at the top; leaving full screen gives the window back at its size, the field in the title bar or the row as before (finding 20, fixed).
- Two changes in quick succession: Search, then Back 119 ms after the click (inside the title bar's 100 ms slide and the push): the start page ends with the title bar hidden and the hero at the top.
- Dragging the empty title bar moved the window by exactly the drag (−200, −100). Double-click maximised it (the field 360 wide, centred in the title bar); double-click again restored it and the field went back to the row.
- Readable over the window's material, maximised, Light and Dark, active and inactive (inactive only the title and caption buttons dim). The VM has no GPU, so Mica draws as a flat colour here.
- Back from Search: from the first frame (50 ms) to 600 ms the start page is in its final state, title bar gone, no band and no search row.
- Tray: "ma" typed in the field, Close (hides to the tray), a click on the tray icon in the overflow: the window comes back with "ma" and its filter (6 of 38).

### #480 not done yet
- Narrator (the field's automation name is "Search towns", which is what it reads; Narrator itself was not run). Display scales other than 200 %.

### #233 (2026-11-10, unpackaged Showcase, local `MauiSpineSampleApp.Server`)
- The owner made the machine's WNS secret ("Windows dev", one year) with a script from the session: `az login` with a device code, then tenant, client id and the secret into the server's user secrets, the secret never shown.
- Build warning with remote push on and the default self-contained build: "this Windows app is self-contained … Status will be Unsupported".
- Unsupported: without `RemoteId`, "RemoteId is not set"; self-contained with a remote id, "PushNotificationManager.IsSupported() is false … Set WindowsAppSDKSelfContained=false". Push page: "No push on this platform".
- Local notifications unpackaged: the page says "This platform schedules no notifications"; Plan three logs "need package identity (MSIX) … nothing is scheduled".
- Framework-dependent (`WindowsAppSDKSelfContained=false`): first `Register()` failed with `0x80040154` for both managers. The machine had only the runtime's framework package (from a Store app), not its Main/Singleton/DDLM packages; `winget install Microsoft.WindowsAppRuntime.1.8 --force` added them (finding 11).
- Then: Ask for permission → "Authorized, registered", a `https://…notify.windows.com/?token=…` channel, and the server's `/installations` has the Windows installation (`platform:windows`, `os:winui`).
- Silent push: in the foreground "Silent foreground", minimised "Silent background" (`IsForeground` false).
- WNS toast while the app runs: shown by Windows, the handler not called (as designed). Tapped while minimised: `OnOpenedAsync` ran with route `log` (push log opened), but the window stayed down (finding 12, fixed: it now comes forward).
- WNS toast with the app closed: shown for an unpackaged app; tapped, the app starts (`----AppNotificationActivated: -Embedding`), the activation is `AppNotification`, the window shows and the push log opens ("opened route: log").
- The notifications' name was "MauiSpineSampleApp" (finding 13, fixed: "Spine Showcase", confirmed by the owner).

### #233, second round (2026-11-11)
- Server buttons (the sample server now writes them for `Category = "sample"`, finding 14): Acknowledge → `OnActionAsync` "ack" with the app staying down; Reply → `reply: "Hello world"`; Open the log → `OnOpenedAsync` with route `log` and action `open`, the window forward. Confirmed by the owner and the log.
- `CollapseId`: "Collapse 2" replaced "Collapse 1" in the Notification Center (owner). WNS accepted the 16-character `X-WNS-Tag`.
- A channel WNS does not know: "404 Invalid channel URL" → `Invalid` → the installation is removed. A real 410 cannot be had on demand; it takes the same path (`StatusFor`).
- Renewal: after a restart the same installation id and channel. The app does not register again when the in-memory server lost its data (by design; "Register again" does it).
- Wrong remote id (`11111111-…`): the channel comes without an error and the app registers; every send fails with a bare 403 (finding 15).
- Settings → notifications off: the Push page says "Denied, not registered", the log "permission Denied"; the server keeps the installation, as on the other platforms. On again: Authorized.
- Offline, automated with scripts that wait for the owner to pull the network: with a channel the machine already has, it comes back at once offline. Without one, WNS fails at once with `0x800704C6` (ERROR_NO_NETWORK), no `InProgressRetry`, so the 10 s wait never applies; nothing asked again (finding 16, fixed). After the fix: registered 4 s after the network came back, and a send reached it.
- Running as administrator (owner): the Push page says "No push on this platform".

### #233 not done yet
- The MSIX path: the PFN mapping mail to Microsoft (the owner's), the activators, packaged local notifications, push to a closed packaged app. Not attempted.

### #437, second round (2026-11-11)
- Pressed, with the real mouse: the item's background changes and the icon keeps the text's colour, on plain items in Dark and on the red "Reset", which takes the template's pressed colour, as its text does (the pre-existing behaviour #437 described).

### #437 not done yet
- Windows (system) theme switch: changing the system setting is the owner's. Unwritable cache folder: optional, not done.
- Hover: a pointer moved by `SetCursorPos` gave no pointer-over state in the captures, so hover is unchecked. 125/150 %, per-monitor rebuild (one monitor), multi-colour SVGs in a menu (no sample menu has one).

## Findings (not in the #501 lists)

1. **HeroCollectionView's drag region outlives its page.** `HeroCollectionView.Windows.cs` sets the hero header as `NonClientRegionKind.Caption` and re-applies it after every WinUI layout pass (`EnforceDragRegions` on `LayoutUpdated`). The Showcase start page stays loaded under pushed pages, so its 270 DIP header stays a window drag area on every page above it. Mouse clicks there move the window instead: the Context menus page's card (left and right click), and the start page's header-bar buttons ("Filter samples", "Theme and accent"). UI Automation `Invoke` on the same buttons works. Only the hero's own top/bottom/overlay actions are passthrough, never Spine's header bar.
2. **The hidden title bar keeps its caption region.** `SetTitleBarVisibilityAsync` hides the MAUI `TitleBar` with `Opacity = 0` and a −32 host padding. Its non-client caption area stays over the top 32 DIP, where Spine's header bar now is. Mouse clicks on header buttons go through only where one of the title bar's invisible page actions happens to lie (Back did; the Menus page's palette button did not).
3. **`KeepsMenuOpen` closes the menu on Windows.** `docs/wiki/menus.md` says the menu stays open on iOS 16+ and Windows. A `ToggleMenuFlyoutItem` click closes the WinUI `MenuFlyout` (mouse and UIA). The toggle itself works.
4. **Unobserved task exceptions at start.** Four `ArgumentException: Unable to find IAnimationManager for 'PageActionView'` from `HeaderBarView.AnimateVisibility` → `FadeToAsync`, on a view not in a window yet.
5. Smaller: the title bar's title reads "My Maui App"; the window opens 400 × 400 DIP although the Showcase asks for 500 × 800; `FontManager` errors for `Assets/Fonts/OpenSansRegular` and `Consolas` (relative URI, unpackaged); the start list's automation name is `Microsoft.Maui.Controls.BoxView` and rows read `…ItemTemplateContext`.
6. **The title bar's search field gets what the title and subtitle leave.** At 500 × 800 it is 62 wide: the placeholder shows one letter, and the query button sits in its middle. At 400 wide it is gone (offscreen). The subtitle repeats the page title the header bar shows under it.
7. **Back twice on a page that shows the title bar**: the title bar's leading action and the header bar's. Fixed: the header bar hides its leading action there, as it already did its trailing ones (region pages only; a sheet has no title bar).
8. **The initial window size was in physical pixels.** `AppWindow.Resize(InitialWidth, InitialHeight)` at 200 % made 250 × 400, raised to 400 × 400 by `MinWidth`. Fixed: scaled by `GetDpiForWindow`, as `MinWidth`/`MinHeight` already were (WinUIEx).
9. **A crash at idle** (13:41): `0xc000027b` (a stowed WinRT exception) in `CoreMessagingXP.dll`, no managed trace. The passthrough update ran from the dispatcher with a narrow `catch`; it now catches everything, logs it once per message, and clips each rectangle to its region and the window. Not seen again since.
10. **`Backdrop = None` left the title bar black.** Spine's title bar is transparent (for Mica and Acrylic); with no backdrop, nothing is behind it. Fixed: the window's content gets the theme's page brush (`ApplicationPageBackgroundThemeBrush`) where it has no background, and the title bar is bound to the host page's background colour. Verified on the Search page: title bar, field and buttons as on the page.
11. **"Class not registered" with only the runtime's framework package.** Push and tapped notifications in an unpackaged app need the Windows App Runtime's Singleton package, which only its installer adds. Not a Spine bug; the log now names the cause and the installer (`WindowsPushPlatform.RuntimeHint`), and the wiki says so.
12. **A tapped toast did not bring the window forward.** `Window.Activate()` neither restores a minimised window nor takes the foreground. Fixed: `BringForward` shows a hidden window, restores a minimised one, activates it and calls `SetForegroundWindow`. Verified by the owner.
13. **An unpackaged app's notifications are named after its .exe.** "MauiSpineSampleApp", in the toast's header and in Settings; a copy in another folder got a new registration with the same name, so it is the file name, not a cache. Fixed: Spine registers with `AppInfo.Current.Name` (the `AssemblyTitle`) and the `.ico` next to the `.exe` (`Register(displayName, iconUri)`); the Showcase sets `AssemblyTitle`/`Product` from `ApplicationTitle` on Windows. The Push page's "Local notifications still work" on Windows was wrong too, and #786's changelog with it.
14. **No buttons on a pushed Windows toast.** WNS draws it from the server's XML and knows nothing of the categories the app declared, so `Category = "sample"` gave none. By design; the sample server now writes the Showcase's three buttons into the toast (`SampleButtons`), and the wiki says how a reply's field is named.
15. **A wrong `RemoteId` shows only on the server.** WNS gives a channel for any id, so the app registers; sends fail with a bare 403. The transport's reason now says to check the remote id against the service principal's Object ID.
16. **Offline, the channel was never asked for again.** WNS fails at once without a network (`0x800704C6`), and nothing asked again until the next launch or foreground. The Windows platform now asks again when the network comes back, and logs the cause instead of the RemoteId warning.
17. **The Push page missed a registration made without it** (the network back, a new token): it now refreshes on `RegistrationChanged` while it shows.
18. **Full screen hid the title bar's actions and field.** Windows draws no title bar for a full-screen window, and MAUI collapses it; the header bar still hid Back and its trailing actions for it, and the field went in it. Fixed: the page's `ShowsTitleBar` is false while the window is full screen.
19. **`GetRegionRects` returns null in full screen**, which threw in the passthrough update (logged, once). Fixed.
20. **After full screen the field stayed in a too narrow title bar.** The field came back at the width it had when it left, raised no `SizeChanged`, and the learnt width had been cleared while the window was full screen. Fixed: the learnt width is kept.

## Changes

- Starts from `build/sdk-roll-forward` (`global.json` rolls forward to the newest .NET 10 SDK, no workload pin; its own PR).
- Findings 1 and 2: `SpineApplication.NonClient.Windows.cs` (new) keeps the window's passthrough regions. After each layout pass (once per dispatcher pass), it hit-tests the title bar's band and every caption region (a hero header) and makes each control on top there (`ButtonBase`, `TextBox`, `AutoSuggestBox`, `ComboBox`, …) passthrough. When a control gives its caption region back and none is left, the band is set again. A hidden title bar is `InputTransparent`, so its invisible buttons no longer take the header bar's clicks.
- Finding 1: `HeroCollectionView` gives its drag region back on `Unloaded` (a page pushed over its page) and sets it again on `Loaded`. With `HostPassthroughRequested` set (Spine sets it through reflection, like `CaptionButtonColorRequested`), it registers only its caption region and leaves passthrough to the host.
- Finding 3: `docs/wiki/menus.md` now says Windows closes the menu on every pick, as Android does. Tried first and reverted: cancelling the `MenuFlyout.Closing` that follows a `KeepsMenuOpen` toggle's flip kept the menu on screen, but the menu then took no clicks and no Esc.
- Finding 4: `HeaderBarView.AnimateVisibility` sets the end state at once for a view without a `MauiContext` up its tree, instead of a `FadeToAsync` that throws into a discarded task.
- Finding 5: the Showcase's `AppTitle` is "Spine Showcase". `Styles.xaml` no longer names `OpenSansRegular`, which was never registered (the fallback font was already in use everywhere, so nothing looks different). The `Consolas` lines are MAUI looking for a font file before the system font (unpackaged), harmless, and are left alone.
- Docs: `hero-collection-view.md` (the region outside its page, and in a Spine app), `page-actions.md` (the header bar under a hidden title bar).
- Finding 6 (owner's choices, 2026-11-10: hide the subtitle with a row as the fallback; Esc ends the search; "fix the look, you can't see it's a search bar"):
  - `SearchField` gives its `SearchBar` an empty style of its own on Windows, so the app's implicit style (the template's: transparent, 44 tall) no longer turns it into a line of placeholder text. Row and title bar both look like Windows' search box.
  - `SearchField.CompactHeight` (title bar: 28) sets the `AutoSuggestBox`'s `TextControlThemeMinHeight` and padding, so the field sits in the 32-point title bar with room above and below, instead of overflowing it.
  - The title bar hides its subtitle while the field is in it.
  - Narrower than 200, the field goes in the row: its width the one time it is too narrow gives the window width it needs (`Window.Width`; MAUI lays the title bar out in WinUI and its `Width` stays −1), and the page's `IsInWindowTitleBar` is false until the window has it. Reset on every page.
  - `SearchField.Windows.cs` (new): Esc in the field ends the search (`End()`); the text stays.
  - Verified: 500 × 800, the field in the row with the subtitle back; maximized, 360 × 28 centred in the title bar, subtitle gone; restored, back in the row with its text. Esc: the focus leaves, "ma" and its filter stay.
  - `docs/wiki/search.md`'s Windows section says all this.
- Finding 7: `HeaderBarView.UpdatePrimaryActionVisibility` hides the leading action when the title bar shows on a region page, and `OnIsTitleBarVisibleChanged` updates it.
- Finding 8: `SpineApplication.Windows` sizes the window by `InitialWidth`/`InitialHeight` × the window's DPI scale. `SpineOptions` and `windows-options.md` say all four sizes are effective pixels.
- Finding 10: `SpineApplication.Windows` gives the window content the theme's page brush when `Backdrop = None` (`ApplyWindowBackground`), and binds the title bar's background to the host page's (`ApplyHostBackgroundForBackdrop`, now an instance method).
- Finding 9: `UpdateNonClientRegions` catches and logs everything (`Plugin.Maui.Spine.Windows`) and clips the rectangles.
- Findings 11–13 (`Plugin.Maui.Spine.PushNotifications`, Windows): `WindowsPushPlatform.RuntimeHint` names the missing runtime for `0x80040154` in the `Register()` and channel logs; `BringForward` brings the window up for a tapped toast; `RegisterToasts` registers an unpackaged app with `AppInfo.Current.Name` and its `.ico`. The Showcase sets `AssemblyTitle`/`Product` on Windows, and its Push page no longer says local notifications work there. `push-notifications.md`: the runtime's installer, and the name and icon.
- Owner's finding (2026-11-10): "Race actions" opened its sheet far from the button. Without an anchor, `ActionSheetPresenter.Windows` opened the flyout in the middle of the window. It now opens at the pointer when the pointer is in the window, as a menu a click opens does on Windows, and in the middle otherwise (`GetCursorPos`/`ScreenToClient` in `NativeMethods`). Verified with the mouse: the click at (1542, 1189) put the menu's first row at (1552, 1199); before, it was at x 1772. `menus.md`'s platform table says so.

- Finding 14: the sample server's `/send` gives a `Category = "sample"` toast the Showcase's buttons on Windows (`SampleButtons`: Open the log, Acknowledge, Reply with its field `spine.reply.reply`), and passes `CollapseId` on. `push-notifications.md`: a reply's field id.
- Finding 15: `WnsTransport` gives a bare 403 the reason `ForbiddenHint` (check the remote id); test `A_bare_403_says_to_check_the_remote_id`. `push-notifications.md` says a wrong `RemoteId` shows only there.
- Finding 16: `WindowsPushPlatform` asks for the channel again when `Connectivity` reports the internet back, and logs a failure without a network as that, not as a RemoteId problem. `push-notifications.md`: offline behaviour.
- Finding 17: the Showcase's Push page refreshes on `RegistrationChanged` while it shows.
- Finding 18: `ViewModelBase.IsWindowTitleBarHidden` (set on Windows from the `AppWindow` presenter, re-read on every presenter change) and `ShowsTitleBar`; the header bar, the search layout and the title bar's slide use `ShowsTitleBar`. The header bar gets it from `NavigationRegion.ApplyHeaderPage` (a typed `SetBinding` lambda needs MAUI's binding source generator, which this library does not run: it threw on every navigation). `search.md` and `page-actions.md` say what full screen does.
- Finding 19: `UpdateNonClientRegions` reads a null region list as empty.
- Finding 20: `_titleBarSearchNeedsWidth` is kept as the window width the field needs, compared on every window size change, instead of being cleared once the window is wide enough.

## Decisions

- Verification is automated-only; the harness and temporary code stay out of the repository.
- Push is tested unpackaged against a local server; the Showcase keeps remote push off on Windows in the repository (#786's decision stands).
- **Spine owns the passthrough regions on Windows.** Three writers set them, last one wins: MAUI's `TitleBar` (its own content only), the hero header (re-applied after every layout, also when its page was gone) and nobody for the header bar. One owner that hit-tests what is on top replaces the hero's own list and covers the header bar, the title bar's content and anything an app puts there. The caption regions stay with MAUI's title bar and the hero. In a Spine app the hero's `IsInteractiveElementOverride` no longer applies.
- **Under a hidden title bar the header bar drags the window** where it is empty, as the title bar did (owner, 2026-11-10: "fix the findings", after the proposal to make it a drag region with passthrough buttons).
- **OpenSans is not registered** to stop the font errors: that would change the Showcase's font on every platform. The dead style setters went instead.
- The harness starts the app from a Terminal tab. A process started from the agent's shell runs but creates no windows (the agent's job), and starting it through Explorer to get around that was refused by the permission check.
