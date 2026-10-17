# Issue #559 — SpinePage: content built in code gets no BindingContext

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/559
**Branch:** issue/559-spinepage-content-built-in-code-gets-no-bindingcon
**Status:** Completed

## Plan
Find why content set from C# on `SpinePage<TViewModel>.Content` never got the page's view model, while XAML content did. Fix it in the core, so a page built in code binds like a XAML page. Then remove `SignInSheet`'s workaround (#519).

## Open Questions

## Changes
- `SpinePage<TViewModel>`: the private `ContentPresenter` is gone. `Content` is `ContentView.Content` again, kept as a `new` property so code compiled against earlier packages still finds it, and now `View?` like the base.
- `SignInSheet`: the code that set `Content.BindingContext` itself is gone.

## Decisions
- **The cause.** MAUI's `ContentPresenter` overrides `SetChildInheritedBindingContext` to do nothing ("we will get this set by our parent"), because it expects to sit in a `ControlTemplate` whose templated parent hands its content the context. `SpinePage` put its content in one with no template, so content set in code never got a binding context.
- **Why XAML was not affected.** XAML sets the base `ContentView.ContentProperty`, which replaced the presenter with the XAML content altogether. A harness showed it: for `LoginSheetPage`, `ContentView.Content` was the `ScrollView`; for `SignInSheet`, it was the `ContentPresenter`. So every XAML page already ran without the presenter, and removing it changes nothing for them.
- **No unit test.** `Plugin.Maui.Spine.Core.Tests` is `net10.0` and cannot reference MAUI types, so this was checked in the app with a harness.
- **Verified locally** (CI is paused), on the iPhone 17 Pro simulator:
  - The harness, without the workaround: both pages hold their content directly in `ContentView.Content`, with the view model as its context.
  - `SignInSheet` draws correctly, and "Create account" switches its step (bindings and commands work).
  - `BarcodeScannerPage`, the other page built in code, opens as before.
  - The Showcase builds for iOS with every package.
