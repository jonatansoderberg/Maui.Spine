using System.Collections.ObjectModel;
using Orientera.Presentation;
using Orientera.Services.Notifications;

namespace Orientera.Features.Profile;

public sealed partial class NotificationRow : ObservableObject
{
    public required NotificationKind Kind { get; init; }
    public required string Label { get; init; }
    public required string Explanation { get; init; }

    /// <summary>
    /// Raised when the user moves the switch. The sheet owns what happens next — asking the
    /// system for permission, and putting the switch back if the answer is no.
    /// </summary>
    internal Action<NotificationRow, bool>? Requested { get; set; }

    /// <summary>Set while the sheet writes a value back, so the round trip does not repeat.</summary>
    private bool _settling;

    [ObservableProperty] public partial bool IsEnabled { get; set; }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_settling)
            Requested?.Invoke(this, value);
    }

    /// <summary>Writes the state without asking for it again — used to revert a denied switch.</summary>
    internal void Settle(bool value)
    {
        _settling = true;
        IsEnabled = value;
        _settling = false;
    }

    /// <summary>
    /// A switch reads its own on/off state aloud, so the description says what the setting is
    /// for and nothing about where it stands.
    /// </summary>
    public string Accessibility => $"{Label}. {Explanation}";
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

    private string _restingStatus = string.Empty;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Rows.Clear();

        var current = _preferences.Current;

        // Only the types that have data behind them. A switch that cannot be turned on is a
        // promise the app is not yet able to keep.
        foreach (var kind in NotificationKinds.Available)
        {
            var row = new NotificationRow
            {
                Kind = kind,
                Label = NotificationKinds.Label(kind),
                Explanation = NotificationKinds.Explanation(kind),
            };

            // Settle before subscribing: the stored value is not a request from the user.
            row.Settle(current.IsEnabled(kind));
            row.Requested = OnRequested;

            Rows.Add(row);
        }

        _restingStatus = _scheduler.IsSupported
            ? "Notiserna ligger i telefonen och behöver ingen inloggning."
            : "Den här plattformen kan inte schemalägga notiser.";

        StatusText = _restingStatus;

        return Task.CompletedTask;
    }

    // The switch has already moved by the time this runs; the work is to make it true, or to
    // move it back.
    private void OnRequested(NotificationRow row, bool wanted) => _ = ApplyAsync(row, wanted);

    private async Task ApplyAsync(NotificationRow row, bool wanted)
    {
        if (wanted && !await _scheduler.RequestPermissionAsync())
        {
            // Denied at the OS level. The switch goes back rather than sitting on, claiming a
            // state the app cannot hold.
            row.Settle(false);
            StatusText = "Notiser är avstängda för Orientera i systeminställningarna. Slå på dem där först.";
            return;
        }

        _preferences.Save(_preferences.Current.With(row.Kind, wanted));
        StatusText = _restingStatus;

        await _notifications.RefreshAsync();
    }
}
