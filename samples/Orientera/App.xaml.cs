using Orientera.Services.Notifications;
using Orientera.Services.Theming;

namespace Orientera;

public partial class App
{
    private readonly NotificationService _notifications;

    public App(NotificationService notifications)
    {
        InitializeComponent();
        ThemeManager.Attach(this);

        _notifications = notifications;
    }

    /// <summary>
    /// The plan is rebuilt on start and on every resume: what a runner needs to be told changes
    /// when the entry list, the start list or their own plans do, and none of that happens while
    /// the app is watching.
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Created += async (_, _) => await _notifications.RefreshAsync();
        window.Resumed += async (_, _) => await _notifications.RefreshAsync();

        return window;
    }
}
