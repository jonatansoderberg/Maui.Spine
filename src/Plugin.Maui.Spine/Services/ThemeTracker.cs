using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// The repaint registry behind <see cref="Core.IThemeService.Track"/> and <see cref="Core.SpineTheme.Track"/>.
/// </summary>
/// <remarks>
/// The registry holds each subscription weakly; the view holds it strongly as the target of its
/// own <c>HandlerChanged</c> delegate, and the subscription holds the view and the callback. View,
/// callback and subscription form an island that is collected with the page whether or not MAUI
/// disconnected its handler. A plain static event would root every abandoned page through the
/// control that subscribed. Not a <c>ConditionalWeakTable</c>: on Mono an entry whose value
/// reaches its key is never cleared while the key's platform view is alive.
/// <para>
/// Under <c>UseSpine</c> the <see cref="ThemeService"/> and the strings setup announce changes. A
/// control used without <c>UseSpine</c> still repaints: until <see cref="TakeOver"/> is called the
/// tracker follows <see cref="Application.RequestedThemeChanged"/> and <see cref="SpineStrings.Changed"/>
/// itself, from the first subscription made once the application exists.
/// </para>
/// </remarks>
internal static class ThemeTracker
{
    private static readonly List<WeakReference<Subscription>> Subscriptions = [];

    public static int Version { get; private set; }

    private static bool _drivenBySpine;
    private static Application? _followed;

    // Application.RequestedThemeChanged is a weak event; the field roots the handler.
    private static EventHandler<AppThemeChangedEventArgs>? _themeHandler;

    public static void Track(VisualElement view, Action onChanged)
    {
        var subscription = new Subscription(view, onChanged);
        view.HandlerChanged += subscription.OnHandlerChanged;
        List(subscription);
        FollowApplication();
    }

    /// <summary>
    /// Called by <see cref="ThemeService"/> when <c>UseSpine</c> is in charge: the tracker stops
    /// following the application itself, so a change is announced once.
    /// </summary>
    public static void TakeOver()
    {
        _drivenBySpine = true;

        if (_followed is not null)
        {
            _followed.RequestedThemeChanged -= _themeHandler;
            SpineStrings.Current.Changed -= OnStandaloneChange;
            _followed = null;
            _themeHandler = null;
        }
    }

    private static void FollowApplication()
    {
        if (_drivenBySpine || _followed is not null || Application.Current is not { } app)
            return;

        _followed = app;
        _themeHandler = OnStandaloneChange;
        app.RequestedThemeChanged += _themeHandler;
        SpineStrings.Current.Changed += OnStandaloneChange;
    }

    private static void OnStandaloneChange(object? sender, EventArgs e)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => OnStandaloneChange(sender, e));
            return;
        }

        BeginChange();
        Notify();
    }

    /// <summary>Bumps the version, before the change is announced, so every listener reads the new one.</summary>
    public static void BeginChange() => Version++;

    /// <summary>Repaints every attached view; detached ones catch up on re-attach.</summary>
    public static void Notify()
    {
        for (var i = Subscriptions.Count - 1; i >= 0; i--)
        {
            if (Subscriptions[i].TryGetTarget(out var subscription))
                subscription.Repaint();
            else
                Subscriptions.RemoveAt(i);
        }
    }

    /// <summary>
    /// Drops every subscription, for a new window: views in an abandoned but not yet collected
    /// window stop repainting, and a live view lists itself again on its next handler change.
    /// </summary>
    public static void Reset()
    {
        foreach (var reference in Subscriptions)
        {
            if (reference.TryGetTarget(out var subscription))
                subscription.Listed = false;
        }

        Subscriptions.Clear();
    }

    private static void List(Subscription subscription)
    {
        subscription.Listed = true;
        Subscriptions.Add(new WeakReference<Subscription>(subscription));
    }

    private sealed class Subscription(VisualElement view, Action onChanged)
    {
        private int _paintedVersion = Version;

        public bool Listed;

        public void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (view.Handler is null)
                return;

            // A control built before the application existed starts the standalone following here.
            FollowApplication();

            if (!Listed)
                List(this);

            if (_paintedVersion != Version)
                Repaint();
        }

        public void Repaint()
        {
            if (view.Handler is null)
                return;

            _paintedVersion = Version;
            onChanged();
        }
    }
}
