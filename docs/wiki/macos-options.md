# macOS Platform Options

Spine's Mac Catalyst settings live under `SpineOptions.MacOS`. They take effect on Mac Catalyst and are ignored elsewhere. The second half of this page lists what tends to go wrong in a Mac app and how to avoid it.

---

## Configuration

```csharp
builder.UseSpine(options =>
{
    options.AddAssembly(typeof(MauiProgram).Assembly);
    options.AppTitle = "My App";

    options.MacOS.Backdrop = WindowBackdrop.Acrylic;   // translucent window
    options.MacOS.UnifiedTitleBar = true;              // header bar in the title bar row
    options.MacOS.ShowTrayIcon = true;                 // menu-bar icon
    options.MacOS.TrayIconSvg = "app_tray.svg";
    options.MacOS.CloseToBackground = true;            // closing hides the window; quit from the menu
});
```

| Property | Type | Default | Description |
|---|---|---|---|
| `Backdrop` | `WindowBackdrop` | `None` | The window's material: `None` (opaque), `Acrylic` (the desktop blurred behind the window, clearly translucent, like System Settings' sidebar) or `Mica` (AppKit's subtler under-window material). See [A translucent window](#a-translucent-window) |
| `UnifiedTitleBar` | `bool` | `false` | Puts a region page's header bar in the title bar row, after the window buttons. See [One row: the header bar in the title bar](#one-row-the-header-bar-in-the-title-bar) |
| `FitWindowToContent` | `bool` | `false` | Once at launch, when the first page's content does not fit, the window takes the size the page suggests for its width. See [Window sizes from the content](#window-sizes-from-the-content) |
| (page) `SuggestWindowSizes` | `ViewModelBase` override | none | Window sizes offered when the header bar is right-clicked. See [Window sizes from the content](#window-sizes-from-the-content) |
| `ShowTrayIcon` | `bool` | `false` | Shows an icon in the menu bar, with the app's [shortcuts](shortcuts.md) as its menu |
| `TrayIconSvg` | `string?` | `null` | Embedded SVG for the menu-bar icon, drawn as a template image |
| `TrayIconPath` | `string` | `""` | A file path for the icon instead of an SVG |
| `TrayIconTooltip` | `string` | `""` | Tooltip on the menu-bar icon |
| `InDockWhileOpen` | `bool` | `false` | A menu-bar app (`LSUIElement`) is in the Dock and Cmd+Tab while its window is open, and back in the menu bar only when `CloseToBackground` hides the window |
| `CloseToBackground` | `bool` | `false` | The close button hides the window instead of quitting; pair it with `ShowTrayIcon` |

An app that lives in the menu bar also sets `LSUIElement` to `true` in `Platforms/MacCatalyst/Info.plist`, so it has no Dock icon. macOS lists only apps with a Dock icon in Cmd+Tab, so such an app can't be reached with Cmd+Tab either. `options.MacOS.InDockWhileOpen = true` switches the app's activation policy with its window: regular (Dock and Cmd+Tab) while the window is open, accessory (menu bar only) once the close button hides it, and regular again when the menu bar icon brings it back. The Dock shows the app's icon meanwhile, so give it one.

---

## A translucent window

`options.MacOS.Backdrop = WindowBackdrop.Acrylic` is the Mac counterpart of [`Windows.Backdrop`](windows-options.md). Spine:

- puts an `NSVisualEffectView` behind UIKit's content and makes the window non-opaque. It stays translucent when the window has no focus, since a dashboard is often read without clicking into it;
- clears the host page's background, as on Windows. Pages are views inside the host page, so they show the material wherever they have no background of their own. Cards, sections and rows keep theirs;
- tints UIKit's root view with the grouped background at 45 %. Liquid Glass reads only the UIKit content behind it, not AppKit's blur, so header buttons over a clear window turned dark;
- keeps the window's `NSAppearance` on the app's theme, because the material is drawn by AppKit and does not follow `UserAppTheme` on its own;
- lifts a page with no opaque background during navigation the way a sheet's page is lifted. The page stays see-through and the page under it is cut off at its edge, so no colour band crosses the material.

With **Reduce Transparency** turned on, the window stays opaque. Pages then sit on the host page's background, so give that background a real colour (see the first pitfall below).

A page that should stay solid, such as a sheet with a form, sets its own `BackgroundColor`.

---

## One row: the header bar in the title bar

By default a Mac window has its title bar and Spine's header bar below it: two rows. `options.MacOS.UnifiedTitleBar = true` makes them one, the way a unified toolbar does on the Mac and the way the header bar already sits beside the caption buttons on Windows:

![The header bar in the title bar row](images/unified-titlebar-mac.png)

- Spine gives the window an empty `NSToolbar` in the `UnifiedCompact` style, so the title bar row is a toolbar's height with the close, minimise and zoom buttons centred in it.
- A region page's header bar is laid over that row. Its leading action (a back button or a `Primary` action) and the title start after the zoom button; the trailing actions keep their place at the right.
- The bar's buttons are a toolbar's: compact, 34-point glass circles with a 20-point glyph (`HeaderBarConstants.CompactButtonHeight`, `CompactGlyphSize`). They and the title are centred on the window buttons. The page's content starts below the row.
- The window's own title is hidden in the bar.
- A floating page's content starts where the row ends, so the space above the title equals the space below it. It stays for Mission Control and the Window menu.
- Sheets, and pages without a header bar, are unchanged.

`TitleAlignment.Left` reads like a Mac toolbar's title. A centred title stays centred in the window.

---

## Window sizes from the content

Right-clicking the header bar opens a menu of window sizes the page suggests, as snap layouts offer on Windows: one column of cards, two, three. The page decides, from its content:

```csharp
public override IReadOnlyList<WindowSize> SuggestWindowSizes(Size screen) =>
[
    new("One column", new Size(468, ContentHeight(468)), "rectangle"),
    new("Two columns", new Size(904, ContentHeight(904)), "rectangle.split.2x1"),
];
```

- It is asked each time the menu opens, so the sizes follow the content. `screen` is the room the screen leaves, in the app's points. Leave out the sizes that do not fit.
- A size is the whole window, title bar row included, in the app's points (UIKit's, which an app scaled to match iPad has larger than AppKit's).
- Choosing one resizes the window with AppKit's animation, keeping its top left corner and staying on the screen. The size the window has is checked.
- A right-click on a button stays the button's. With no sizes, right-clicking does nothing.
- Leave a few points of slack over a layout's breakpoint: a window exactly at the width three columns need can lay out two.
- `WindowHeightFor(width)` on the page view model measures the height for you: the page's `ScrollView` content at that width, with the header bar's (and the title bar row's) inset, in UIKit's points. It returns null before the page is laid out. Measuring by hand goes wrong easily: MAUI's `Window.Height` is in AppKit's points on a scaled Mac, and a scroll view's frame under the unified title bar reads lower than it is drawn.

### Fitting the window

- `options.MacOS.FitWindowToContent = true`: once, at launch, when the first page's content does not fit, the window takes the suggestion for its width (the widest no wider than the window, or the narrowest). After that the size is the user's.
- `FitWindowToContent()` on the page view model does it now, smaller or larger: after the content changed, for example when the user hid an item.

---

## Pitfalls in a Mac app

Each of these showed up while building a menu-bar app on Spine. Spine handles the window-level ones now; the rest are app choices.

### The template's white page style shows as a white title bar

**Symptom:** the title bar area is white when the window has no focus, and it looks right once the window is clicked.

**Cause:** Spine extends the content under a transparent title bar. Without focus, AppKit draws no title bar material of its own, so the host page's background shows through. The MAUI template's implicit `Page` and `ContentPage` style paints it white.

**Fix:** give the implicit page style the grouped background the pages use, not white:

```xml
<Style TargetType="Page" ApplyToDerivedTypes="True">
    <Setter Property="BackgroundColor" Value="{AppThemeBinding Light=#F2F2F7, Dark=Black}" />
</Style>
```

With `Backdrop` set, Spine clears it.

### The window shows its title twice

**Symptom:** the window's own title sits in the title bar above the header bar, which shows the same title.

**Fix:** hide the title bar's text and keep the title for Mission Control and the Window menu. UIKit's `UITitlebar` does not reach the AppKit window Spine adopts, so set `titleVisibility` on the `NSWindow`. UIKit resets title bar state after focus and resize changes, so apply it on `NSWindowDidUpdateNotification`, and write only when the value differs.

### A window that opens without focus has the opaque system title bar

**Symptom:** a login item or a window opened with `open -g` has the system title bar until it is clicked.

**Fix:** none needed. Spine applies the full-size content view on `NSWindowDidChangeOcclusionStateNotification` as well as on key (#644). If you add your own NSWindow setup, do the same.

### Glass header buttons are dark over a translucent window

**Symptom:** with a home-made AppKit backdrop, the header bar's glass circles turn dark grey in light mode.

**Fix:** use `options.MacOS.Backdrop`, which tints UIKit's root view for the glass to read. Don't turn glass off: without it the buttons become bare glyphs against the window edge.

### Text fields in grouped rows get a double frame

**Symptom:** an `Entry` in a `SpineRow` has a square UIKit border with a rounded focus ring outside it.

**Fix:** per Apple's guidelines, a field in a grouped row has no frame of its own; the row is the frame. Remove the border and the focus effect for those entries:

```csharp
EntryHandler.Mapper.AppendToMapping("GroupedRowField", (handler, _) =>
{
    handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
    handler.PlatformView.BackgroundColor = UIKit.UIColor.Clear;
    handler.PlatformView.FocusEffect = null;
});
```

### A page scrolls although everything fits

**Symptom:** the mouse wheel scrolls a page by about the header bar's height, sometimes without a scroll bar.

**Cause:** `UIScrollView` stretches its content to its own height, and the header bar's inset comes on top, so there is always one inset's worth to scroll.

**Fix:** none needed for a `ScrollView` under Spine's header bar: on the Mac it scrolls only when its content is taller than the room under the bar, and shows its scroll bar only then. A `CollectionView` scrolls as UIKit does.

### The page title from `[NavigableRegion(Title = …)]` overwrites a localized one

**Symptom:** a page sets `Title` from the string store, but the header shows the attribute's text after navigation.

**Cause:** Spine applies the attribute's metadata to the view model when it navigates to the page, after `OnAppearingAsync`.

**Fix:** keep the localized title when it changes:

```csharp
PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(Title) && Title != _strings["Settings.Title"]) Title = _strings["Settings.Title"];
};
```

### A sheet page never raises `Loaded`

**Fix:** start work that needs the page's views from a view's `HandlerChanged`, for example a `WebView`'s, and stop it when the handler is null.

### Turning a header button's icon yourself

**Fix:** use [`PageAction.IsBusy`](page-actions.md#a-busy-action) with `SpinsWhenBusy = true`. Header buttons morph on macOS 26, and the glyph is then an `Image` over the glass rather than the button's image. The face is also reused for the next page's action, so a rotation started from outside keeps turning on Back.

### JavaScript in a `WebView` returns `null`

**Cause:** MAUI's `EvaluateJavaScriptAsync` on Apple wraps the script in `eval()`, which a page's Content-Security-Policy can forbid.

**Fix:** call `WKWebView.EvaluateJavaScriptAsync` on the handler's platform view.

### Strings: one document per culture, and "same as the system"

- `strings.xml` (neutral) and `strings.<culture>.xml` are one document per culture for the whole assembly. Use `WithCulture="false"` on the `EmbeddedResource`, or `strings.sv.xml` ends up in a satellite assembly. See [Strings](strings.md).
- A chosen culture is stored as `Spine.Culture` and applied at the next launch. For a "same as the system" choice, set the culture from the system's first language and remove that preference.
- Text that providers or services build at runtime, such as quota names or "N requests this month", does not re-resolve. Rebuild it when `ISpineStrings.Changed` fires.

### AppKit and UIKit measure in different points

**Symptom:** something placed from an AppKit measurement (a window button's frame, the title bar's height) lands too high or too far left.

**Cause:** a Catalyst app that is scaled to match iPad lays UIKit out in larger points than AppKit: a window 798 points wide in AppKit is 1037 wide in UIKit (77 %).

**Fix:** convert with the ratio of `UIWindow.Bounds` to the `NSWindow` frame. UIKit's own safe area is already in UIKit points.

### Testing on the Mac

- Screenshots of a window in the background don't show CoreAnimation animations or the desktop behind a translucent window. Check those with the window in front, or by eye.
- `open -g` launches without focus, which is what a login item does.
