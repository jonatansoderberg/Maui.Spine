using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MauiSpineSampleApp.Pages.Keyboard;

public partial class KeyboardPageViewModel(INavigationService navigation) : SampleViewModel
{
    public ObservableCollection<string> Messages { get; } =
    [
        "Tap the field at the bottom.",
        "The list and the field end above the keyboard, and the field moves with it.",
        "Turn Keyboard avoidance off and the keyboard covers the field; KeyboardInset still reports how far.",
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; } = "";

    public string InsetText => $"KeyboardInset {KeyboardInset:0}";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(KeyboardInset))
            OnPropertyChanged(nameof(InsetText));
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        Messages.Add(Draft.Trim());
        Draft = "";
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand]
    private Task OpenSheet() => navigation.NavigateToAsync<KeyboardSheetPage>();
}

public partial class KeyboardSheetPageViewModel : KeyboardPageViewModel
{
    public KeyboardSheetPageViewModel(INavigationService navigation) : base(navigation)
    {
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
    }
}
