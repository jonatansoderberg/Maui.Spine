# Navigation Results

A page can return a typed result to its caller. The caller `await`s the navigation and inspects the `NavigationResult<TResult>` when the page closes — whether via an explicit confirmation or by being dismissed.

---

## Overview

Getting a result requires changes on both sides:

1. The target page declares it produces a result by implementing `INavigableWithResult<TResult>`.
2. The target ViewModel calls `INavigationService.ReturnAsync(result)` to deliver the result.
3. The caller uses `NavigateToWithResultAsync<TPage, TResult>()` and checks the returned `NavigationResult<TResult>`.

---

## Step 1 — Define the result type

```csharp
public sealed record FullscreenSheetResult(string Message);
```

---

## Step 2 — Declare the page as returning a result

Implement `INavigableWithResult<TResult>` on the page code-behind:

```csharp
namespace MyApp.Pages;

[NavigableSheet(
    Title = "Confirm Action",
    BackgroundPageOverlay = BackgroundPageOverlay.Dimmed,
    AllowedDetents = [SheetDetent.FullScreen])]
public partial class ConfirmPage : INavigableWithResult<ConfirmPageResult>
{
    public ConfirmPage() => InitializeComponent();
}
```

---

## Step 3 — Return the result from the ViewModel

Call `ReturnAsync` to deliver the result and close the page. Call `BackAsync` (or let the user dismiss) for a "canceled" outcome:

```csharp
namespace MyApp.Pages;

public partial class ConfirmPageViewModel(INavigationService _navigation) : ViewModelBase
{
    [RelayCommand]
    private async Task Confirm() =>
        await _navigation.ReturnAsync(new ConfirmPageResult("Confirmed!"));

    [RelayCommand]
    private async Task Cancel() =>
        await _navigation.BackAsync();
}
```

---

## Step 4 — Await the result in the caller

```csharp
[RelayCommand]
private async Task ShowConfirmWithResult()
{
    var result = await _navigation.NavigateToWithResultAsync<ConfirmPage, ConfirmPageResult>();

    if (result is { IsSuccess: true, Value: not null and var value })
    {
        // Page returned an explicit result
        StatusText = $"Result: {value.Message}";
    }
    else
    {
        // Page was dismissed without calling ReturnAsync
        StatusText = "Canceled";
    }
}
```

---

## `NavigationResult<TResult>` properties

| Property | Type | Description |
|---|---|---|
| `IsSuccess` | `bool` | `true` when `ReturnAsync` was called; `false` when dismissed |
| `Value` | `TResult?` | The result value when `IsSuccess` is `true`; `default` otherwise |

---

## Combining parameters and results

A page can accept both a navigation parameter and return a result by implementing both interfaces:

```csharp
[NavigableSheet(Title = "Edit Item")]
public partial class EditItemSheet :
    INavigableWithParameter<Item>,
    INavigableWithResult<Item>
{
    public EditItemSheet() => InitializeComponent();
}
```

```csharp
public partial class EditItemSheetViewModel : ViewModelBase,
    IReceivesNavigationParameter<Item>
{
    [ObservableProperty] public partial string? Name { get; set; }

    public Task OnNavigationParameterAsync(Item param)
    {
        Name = param.Name;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Save() =>
        await _navigation.ReturnAsync(new Item(Name!));
}
```

Navigate from the caller:

```csharp
// NavigateToAsync with parameter + result requires both interfaces on the page
var result = await _navigation.NavigateToWithResultAsync<EditItemSheet, Item>();
```

> Currently `NavigateToWithResultAsync` does not accept a parameter directly. Pass the parameter separately using `NavigateToAsync<TPage, TParam>` first if your use case requires it, or combine the call by implementing a custom ViewModel method.
