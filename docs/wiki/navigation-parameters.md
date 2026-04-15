# Navigation Parameters

Pass typed data to a page when navigating to it. Spine delivers the parameter to the ViewModel before `OnAppearingAsync` is called.

---

## Overview

Passing a parameter requires changes on both the **calling side** and the **receiving side**:

1. The target page declares it accepts a parameter by implementing `INavigableWithParameter<TParam>`.
2. The target ViewModel receives the parameter by implementing `IReceivesNavigationParameter<TParam>`.
3. The caller uses the `NavigateToAsync<TPage, TParam>(param)` overload.

---

## Step 1 — Define the parameter type

Any type works. A `record` is a clean choice for immutable data:

```csharp
public sealed record PersonData(string Name, string Email, int Age);
```

---

## Step 2 — Declare the page as accepting a parameter

Implement `INavigableWithParameter<TParam>` on the page code-behind:

```csharp
namespace MyApp.Pages;

[NavigableRegion(Title = "Person Detail")]
public partial class PersonDetailPage : INavigableWithParameter<PersonData>
{
    public PersonDetailPage() => InitializeComponent();
}
```

---

## Step 3 — Receive the parameter in the ViewModel

Implement `IReceivesNavigationParameter<TParam>` on the ViewModel. The `OnNavigationParameterAsync` method is called before `OnAppearingAsync`:

```csharp
namespace MyApp.Pages;

public partial class PersonDetailPageViewModel : ViewModelBase,
    IReceivesNavigationParameter<PersonData>
{
    [ObservableProperty] public partial string? Name { get; set; }
    [ObservableProperty] public partial string? Email { get; set; }
    [ObservableProperty] public partial int Age { get; set; }

    public Task OnNavigationParameterAsync(PersonData param)
    {
        Name = param.Name;
        Email = param.Email;
        Age = param.Age;
        return Task.CompletedTask;
    }
}
```

---

## Step 4 — Navigate with the parameter

Use the two-type-argument overload of `NavigateToAsync`:

```csharp
[RelayCommand]
private async Task ShowPersonDetail() =>
    await _navigation.NavigateToAsync<PersonDetailPage, PersonData>(
        new PersonData("Alice Smith", "alice@example.com", 30));
```

---

## Parameters and sheets

The same pattern works for sheet pages. Just apply `[NavigableSheet]` on the page and pass the same parameter in the navigation call:

```csharp
[NavigableSheet(Title = "Edit Person")]
public partial class EditPersonSheet : INavigableWithParameter<PersonData>
{
    public EditPersonSheet() => InitializeComponent();
}
```

```csharp
await _navigation.NavigateToAsync<EditPersonSheet, PersonData>(person);
```

---

## Call order

When navigating with a parameter, Spine calls the ViewModel methods in this order:

1. `OnNavigationParameterAsync(param)` — populate ViewModel state from the parameter
2. `OnAppearingAsync(NavigationDirection.NavigateTo)` — standard appearing lifecycle hook
