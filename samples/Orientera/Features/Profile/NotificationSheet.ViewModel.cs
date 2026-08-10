using System.Collections.ObjectModel;
using Orientera.Presentation;
using Orientera.Services.Notifications;

namespace Orientera.Features.Profile;

public sealed partial class NotificationRow : ObservableObject
{
    public required NotificationKind Kind { get; init; }
    public required string Label { get; init; }
    public required string Explanation { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(Accessibility))]
    public partial bool IsEnabled { get; set; }

    public string StateText => IsEnabled ? "På" : "Av";

    public string Accessibility => $"{Label}, {(IsEnabled ? "på" : "av")}";
}

/// <summary>
/// Opt-in per type, as the requirements put it. Nothing is on until the user turns it on, and
/// the first thing turned on is what asks for permission — asking before there is anything to
/// notify about is how an app gets denied for good.
/// </summary>
public partial class NotificationSheetViewModel(
    NotificationPreferencesStore _preferences,
    INotificationScheduler _scheduler,
    NotificationService _notifications) : OrienteraViewModel
{
    public ObservableCollection<NotificationRow> Rows { get; } = [];

    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Rows.Clear();

        var current = _preferences.Current;

        // Only the types that have data behind them. A switch that cannot be turned on is a
        // promise the app is not yet able to keep.
        foreach (var kind in NotificationKinds.Available)
        {
            Rows.Add(new NotificationRow
            {
                Kind = kind,
                Label = NotificationKinds.Label(kind),
                Explanation = NotificationKinds.Explanation(kind),
                IsEnabled = current.IsEnabled(kind),
            });
        }

        StatusText = _scheduler.IsSupported
            ? "Notiserna ligger i telefonen och behöver ingen inloggning."
            : "Den här plattformen kan inte schemalägga notiser.";

        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Toggle(NotificationRow row)
    {
        bool wanted = !row.IsEnabled;

        if (wanted && !await _scheduler.RequestPermissionAsync())
        {
            // Denied at the OS level. Say so rather than leaving a control that claims to be on
            // and does nothing.
            StatusText = "Notiser är avstängda för Orientera i systeminställningarna.";
            return;
        }

        row.IsEnabled = wanted;

        _preferences.Save(_preferences.Current.With(row.Kind, wanted));

        await _notifications.RefreshAsync();
    }
}
