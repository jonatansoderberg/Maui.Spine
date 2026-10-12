# Guards

A **guard** asks something before Spine shows a page or runs a command: an unlock, a sign-in, a confirmation. You put an attribute on the page or the command, and a guard that you register answers for it. The page and its view model are not created until every guard has said yes, so a refused page costs nothing.

The core has the mechanism and no guard of its own. Packages bring their guards: `Plugin.Maui.Spine.Biometrics` brings `[RequiresUnlock]` ([Biometrics](biometrics.md)), and `Plugin.Maui.Spine.Authentication` brings `[RequiresSignIn]` (#519). You can also write your own.

---

## Guarding a page

```csharp
[NavigableRegion(Title = "Vault"), RequiresUnlock("Open the vault")]
public partial class VaultPage { … }
```

The guards run before every way to the page:

| Way to the page | On refusal |
|---|---|
| `NavigateToAsync`, with or without a parameter | Nothing happens. The parameter is not delivered. |
| A sheet page | The sheet does not open. |
| `ShowAsync` | Nothing happens. An open sheet stays open, because the guard runs before the sheet is closed. |
| `NavigateToWithResultAsync` | The result is `Canceled`. |
| `SwitchToTabAsync`, or navigating to a tab page | The tab does not change. |
| A user's tap on a guarded tab | iOS refuses the tap and switches once the guard passes. Android and Windows have already switched when Spine hears of the tap, so the tab is shown under the guard's cover until it passes. A guard without a cover sends the user back to the previous tab. |
| `SetRootAsync` | The guard is told it cannot cancel (`GuardContext.CanCancel` is false) and should keep asking. If it refuses anyway, the app keeps its current root. |

Going back and `ReturnAsync` run no guards: a page below in the stack has already passed.

A shortcut or a search result that opens a guarded page on a cold start waits until the app is active and the root page is in place before the guard asks. A second navigation to a page whose guard is still asking is dropped, so a double tap gives one prompt.

## Guarding a command

```csharp
[PageAction(Svg = "trashcan.svg"), RequiresUnlock("Delete your account")]
[RelayCommand]
private Task DeleteAsync() => _account.DeleteAsync();
```

The guard runs wherever **Spine** runs the command:
- a page action in the header bar
- a menu row or a context menu row
- an action sheet row
- `Tap.Command`
- `{PageCommand}`

A command that is waiting on its guard ignores further taps.

> **A guard is a gate in the UI, not a lock on the code.** `Command="{Binding DeleteCommand}"` on a plain `Button` hands the command to the button, which runs it without asking. Bind with `{PageCommand Delete}` or use `Tap.Command` to get the guard. Code that must never run without a check asks for it itself, for example with `IUnlock.RequireAsync` from the biometrics package. A command on a view model that is not a page's (a row's own view model) is not guarded either.

## Writing a guard

```csharp
public sealed class ConfirmAttribute(string question) : GuardAttribute
{
    public string Question { get; } = question;
}

public sealed class ConfirmGuard : IGuard<ConfirmAttribute>
{
    public async ValueTask<bool> CheckAsync(ConfirmAttribute attribute, GuardContext context, CancellationToken cancellationToken)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } page)
            return false;

        return await page.DisplayAlertAsync("Guard", attribute.Question, "Continue", "Cancel");
    }
}

// MauiProgram
builder.Services.AddSpineGuard<ConfirmAttribute, ConfirmGuard>();
```

**What a guard gets**
- `GuardContext.Trigger`: what started the check (`Push`, `Sheet`, `Tab`, `Show`, `Root` or `Command`).
- `GuardContext.Page` or `GuardContext.Command`: the target.
- `GuardContext.CanCancel`: whether there is anything to stay on if the guard refuses.
- A cancellation token: it is cancelled when the host is swapped, for example on sign-out.

**Rules a guard must follow**
- **Owns its UI.** The guard shows any prompt and any message on refusal itself.
- **Exceptions count as refusals.** A guard that throws refuses, and the exception is logged.
- **Registration is required.** An attribute with no registered guard throws the first time it is used, naming the attribute, so a missing package reference is never a silent open door.
- **Order.** Several guards on one target run in `GuardAttribute.Order`, lowest first. Spine's own packages use 100 for sign-in and 200 for unlock.
- **Derived attributes.** An attribute that derives from a registered one uses that guard.

**A cover for tabs.** A guard can also implement `IGuardCover` and return a view to show in place of a guarded tab while it asks on Android and Windows. The view's button calls the `retry` it is given.

## The whole screen

A package that acts on the whole screen, such as the [app lock](biometrics.md#the-app-lock), has two helpers in the core:

```csharp
// Which pages are on screen, and when that changes.
SpineScreen.ShownPagesChanged += (_, _) =>
{
    foreach (var shown in SpineScreen.ShownPages)
        Debug.WriteLine($"{shown.Page.GetType().Name} in sheet: {shown.InSheet}, can go back: {shown.CanGoBack}");
};

await SpineScreen.WhenReady;                 // the window has been active and the first page is in
SpineScreen.ExcludeFromCapture = true;       // FLAG_SECURE on Android, capture exclusion on Windows

// A view over everything, the open sheet included, until it is disposed.
using var overlay = SpineOverlay.Show(new LockView(), back: () => { /* Android's back */ });
```

| Member | What it does |
|---|---|
| `SpineScreen.ShownPages` | The current page of the root region (the selected tab's), then the open sheet's. During a push or a pop, and under a lightbox, the page behind is listed too. |
| `SpineScreen.ShownPagesChanged` | Raised on the main thread when that list changes. |
| `SpineScreen.WhenReady` | Completes once the window has been active and the first root page is in place: the earliest an app can show a prompt on a cold start. |
| `SpineScreen.ExcludeFromCapture` | Keeps the app's windows out of screenshots and recordings: on Android the activity, an open sheet and Spine's overlays, on Windows the window. iOS and Mac Catalyst cannot block capture, so it does nothing there. |
| `SpineOverlay.Show(view, back)` | Shows the view over the whole window: a view on the window on Apple, a dialog of its own on Android, a popup on Windows. A sheet that opens later goes under it. The view gets the app's styles. |

## See also

- [Page actions](page-actions.md)
- [Menus](menus.md)
- The Guards page in Spine Showcase
