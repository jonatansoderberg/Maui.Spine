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
/// </remarks>
internal static class ThemeTracker
{
    private static readonly List<WeakReference<Subscription>> Subscriptions = [];

    public static int Version { get; private set; }

    public static void Track(VisualElement view, Action onChanged)
    {
        var subscription = new Subscription(view, onChanged);
        view.HandlerChanged += subscription.OnHandlerChanged;
        List(subscription);
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
