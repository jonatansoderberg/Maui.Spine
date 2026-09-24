using System.Globalization;
using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Pages.Strings;

public partial class StringsPageViewModel(ISpineStrings _strings) : ViewModelBase
{
    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = _strings.Culture.TwoLetterISOLanguageName == "sv" ? "sv" : "en";

    [ObservableProperty]
    public partial int Apples { get; set; } = 1;

    [ObservableProperty]
    public partial string UserName { get; set; } = "Jonatan";

    [ObservableProperty]
    public partial DateTime Now { get; set; } = DateTime.Now;

    // The same text from C#: inject ISpineStrings, or read SpineStrings.Current where there is no DI.
    public string FromCode => _strings.Get("Strings.Apples", Apples);

    [ObservableProperty]
    public partial string Missing { get; set; } = "";

    partial void OnSelectedLanguageChanged(string value) => _strings.Culture = CultureInfo.GetCultureInfo(value);

    partial void OnApplesChanged(int value) => OnPropertyChanged(nameof(FromCode));

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        _strings.Changed += OnStringsChanged;
        Missing = _strings["Strings.NoSuchKey"];
        return base.OnAppearingAsync(navigationDirection);
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _strings.Changed -= OnStringsChanged;
        return base.OnDisappearingAsync(navigationDirection);
    }

    private void OnStringsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(FromCode));

    [RelayCommand] private void More() => Apples++;
    [RelayCommand] private void Fewer() => Apples = Math.Max(0, Apples - 1);
}
