# Issue #322 — {spine:PageCommand} markup extension for bindings inside templates

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/322
**Branch:** issue/322-pagecommand-markup-extension
**Status:** Completed
**Stage:** 1 of the app-review plan (#331)

## Plan

### Gap
Inside a `DataTemplate` the binding context is the item, so reaching a command on the page's view model means `{Binding Source={RelativeSource AncestorType={x:Type ScrollView}}, Path=BindingContext.OpenLiveCommand}` with an ancestor type that differs per page (Orientera has 29 of these across 11 files, and a comment noting the walk "resolved to nothing once" when the ancestor changed).

### Design
Two markup extensions in `Plugin.Maui.Spine.Extensions`, available unprefixed through Spine's global xmlns like `Glass.Style`:

```xml
<Button Command="{PageCommand OpenLive}" CommandParameter="{Binding .}" />
<Label Text="{PageBinding Title}" />
```

- `PageCommandExtension`: `{PageCommand OpenLive}` → `Binding("BindingContext.OpenLiveCommand", source: RelativeBindingSource(FindAncestor, typeof(INavigable)))`. The `Command` suffix is added when missing.
- `PageBindingExtension`: `{PageBinding Path, Mode=…, Converter=…, StringFormat=…}` for any member of the page view model.
- The ancestor is found by `INavigable`, the interface every `SpinePage<TViewModel>` implements, so there is no need for a non-generic base class. MAUI's `FindAncestor` matches with `IsAssignableFrom`, which is verified at runtime in the sample before this is committed to; the fallback would be a non-generic `SpinePage` base.

### Steps
1. `Extensions/PageBindingExtension.cs` with both extensions.
2. Sample: `Pages/PageBinding/PageBindingPage` from the start page: a `CollectionView` whose item template uses `{PageCommand Pick}` with the item as parameter and `{PageBinding Picked}` for a header label; a nested `Grid` inside a `Border` inside the template to show the walk passes any depth.
3. Docs: section in `docs/wiki/page-pattern.md` ("Binding to the page from a template"), `/spine-page` skill line.
4. Build iOS and Android, verify the tap reaches the page command and the header label updates.

## Open Questions

None.

## Changes

- `Extensions/PageBindingExtension.cs`: `PageBindingExtension` (`{PageBinding Path}` with `Mode`, `Converter`, `ConverterParameter`, `StringFormat`) and `PageCommandExtension` (`{PageCommand Name}`, `Command` suffix added when missing). Both produce a `Binding` on `BindingContext.<path>` with a `RelativeBindingSource(FindAncestor, typeof(INavigable))`.
- Sample: `Pages/PageBinding/PageBindingPage` from the start page: a fruit list whose rows use `{PageCommand Pick}` and `{PageBinding Picks}` from inside a `Border` → `Grid` → `VerticalStackLayout`, with the template's markup printed on the page. The start page's list is now an index of sample pages (`SampleIndex`, one line per page with its opener) instead of thirty filler rows; the `Item` model gained an `Open` delegate.
- Docs: "Binding to the page from a template" in `page-pattern.md`; `/spine-page` skill.
- Verified on iPhone 17 (iOS 26.4) and the Pixel 10 Pro emulator: a tap in a row reaches the page command with the item as parameter, and every row's `PageBinding` label updates.

## Decisions

- `FindAncestor` with the `INavigable` interface resolves in MAUI 10 (verified in the sample on both platforms), so no non-generic `SpinePage` base was needed.
- The extension produces an ordinary `Binding` with a `RelativeBindingSource` rather than resolving the page at parse time: inside a `DataTemplate` the parent chain does not exist yet when the extension runs, and the relative source resolves lazily when the template is realized.
