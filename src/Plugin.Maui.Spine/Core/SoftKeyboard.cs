namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Where the on-screen keyboard is, as the platform reports it: its top edge in screen
/// coordinates (device-independent units), or <see langword="null"/> while it is down. Each
/// <see cref="Presentation.NavigationRegion"/> turns that into how far it covers its own frame.
/// </summary>
internal static partial class SoftKeyboard
{
    private static Action? _changed;

    /// <summary>The keyboard's top edge in screen coordinates, or <see langword="null"/> while it is down.</summary>
    public static double? Top { get; private set; }

    /// <summary>How long the keyboard takes to reach <see cref="Top"/>; zero while it follows a gesture frame by frame.</summary>
    public static TimeSpan Duration { get; private set; }

    /// <summary>Raised on the main thread whenever <see cref="Top"/> changes.</summary>
    public static event Action? Changed
    {
        add
        {
            Observe();
            _changed += value;
        }
        remove => _changed -= value;
    }

    internal static void Report(double? top, TimeSpan duration)
    {
        if (top == Top && duration == Duration)
            return;

        Top = top;
        Duration = duration;
        _changed?.Invoke();
    }

    /// <summary>Starts listening to the platform's keyboard notifications, once.</summary>
    static partial void Observe();
}
