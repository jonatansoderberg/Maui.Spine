namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The front layer of a <see cref="NavigationRegion"/>, the page in front. On Android it looks at
/// every touch on its way to the page and takes over a drag that starts at the leading edge and runs
/// rightward as the back-swipe: there a page's scroll view takes a touch as it goes down, and a
/// gesture recognizer on the layer never saw one. Elsewhere it is a plain <see cref="ContentView"/>,
/// and the region's pan recognizer handles the swipe.
/// </summary>
internal sealed class BackSwipeHost : ContentView
{
    /// <summary>Whether a drag that starts this far from the layer's leading edge, in device-independent units, may be a back-swipe.</summary>
    public Func<double, bool>? Accepts { get; set; }

    /// <summary>The swipe as it goes: its status, and how far it has moved sideways and down since it began.</summary>
    public Action<GestureStatus, double, double>? Swiped { get; set; }
}
