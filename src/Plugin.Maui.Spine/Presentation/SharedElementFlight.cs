using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The views a push or a pop carries from the page leaving to the page arriving: each pair of
/// views with the same <see cref="Transition.TagProperty"/> flies, as a picture, from where it is
/// on the one page to where it rests on the other, over the pages while they move. The views
/// themselves are hidden until the flight lands.
/// </summary>
/// <remarks>
/// A tag on a page itself makes a zoom instead: the page grows out of the view with its tag on the
/// page under it, and shrinks back into it when it leaves (<see cref="IsZoom"/>).
/// </remarks>
internal sealed partial class SharedElementFlight : IDisposable
{
    private readonly List<(VisualElement View, double Opacity)> _hidden = [];

    /// <summary>
    /// The flight between <paramref name="leaving"/> and <paramref name="arriving"/>, or
    /// <see langword="null"/> when no tag of the one is on screen on the other. Call it with the
    /// layers at rest, before the transition moves them: the places are measured there.
    /// </summary>
    /// <param name="container">The view that holds the region's layers; the pictures fly in it.</param>
    /// <param name="front">The front layer, which the pictures fly above.</param>
    /// <param name="leaving">The page whose tagged views the pictures leave from.</param>
    /// <param name="arriving">The page whose tagged views the pictures land on.</param>
    /// <param name="push">Whether <paramref name="arriving"/> is in <paramref name="front"/>, arriving on a push.</param>
    public static async Task<SharedElementFlight?> FindAsync(View container, View front, Element leaving, Element arriving, bool push)
    {
        if (ReducedMotion.IsOn)
            return null;

        var sources = Transition.TaggedIn(leaving);
        if (sources.Count == 0 || !Transition.TaggedIn(arriving).Keys.Any(sources.ContainsKey))
            return null;

        if (push)
            await SettleAsync(front);

        // Looked up again: the page may have built views while it settled.
        var targets = Transition.TaggedIn(arriving);
        if (Create(container, front) is not { } flight)
            return null;

        // The page in the front layer, the one that grows or shrinks when its own tag is set.
        if (((push ? arriving : leaving) as PagePresenter)?.Content is { } page && Transition.GetTag(page) is { Length: > 0 } pageTag)
        {
            // Without its view on screen the page moves as usual: a picture of the whole page
            // flying into a stranger's place would not be a zoom.
            if ((push ? sources : targets).TryGetValue(pageTag, out var views) && flight.AddZoom(views, push))
                return flight;

            flight.Dispose();
            return null;
        }

        foreach (var (tag, targetViews) in targets)
        {
            if (sources.TryGetValue(tag, out var sourceViews))
                flight.Add(sourceViews, targetViews);
        }

        if (flight._hidden.Count == 0)
        {
            flight.Dispose();
            return null;
        }

        return flight;
    }

    /// <summary>
    /// Lets the page arriving finish what it does once it is in the window: its lists take their
    /// insets from the header bar in their <see cref="VisualElement.Loaded"/>, which MAUI raises
    /// from the main queue a moment later, and that moves every view in them. Its layer is hidden
    /// meanwhile, so if a frame is drawn, it is the page still showing.
    /// </summary>
    private static async Task SettleAsync(View layer)
    {
        layer.Opacity = 0;

        try
        {
            await Task.Yield();
        }
        finally
        {
            layer.Opacity = 1;
        }
    }

    private void Hide(params VisualElement[] views)
    {
        foreach (var view in views)
        {
            _hidden.Add((view, view.Opacity));
            view.Opacity = 0;
        }
    }

    /// <summary>Shows the views again and takes the pictures away.</summary>
    public void Dispose()
    {
        foreach (var (view, opacity) in _hidden)
            view.Opacity = opacity;

        RemovePictures();
        _hidden.Clear();
    }

    /// <summary>
    /// The corner radius of a <see cref="Border"/> drawn as a rounded rectangle (its top-left
    /// corner), or <see langword="null"/> when the view's shape is not known here.
    /// </summary>
    private static double? CornerRadiusOf(VisualElement view) =>
        view is Border { StrokeShape: Microsoft.Maui.Controls.Shapes.RoundRectangle shape } ? shape.CornerRadius.TopLeft : null;

    /// <summary>The solid colour <paramref name="view"/> fills its shape with, if it has one.</summary>
    private static Color? FillOf(VisualElement view) =>
        view.Background is SolidColorBrush { Color: { } color } ? color : view.BackgroundColor;

    private static partial SharedElementFlight? Create(View container, View front);

    /// <summary>Pairs the first of <paramref name="sources"/> on screen with the first of <paramref name="targets"/> that has a place.</summary>
    private partial void Add(List<VisualElement> sources, List<VisualElement> targets);

    /// <summary>
    /// Makes this flight a zoom between the front layer's page and the first of
    /// <paramref name="views"/> on screen, on the page under it; <see langword="false"/> when none is.
    /// </summary>
    private partial bool AddZoom(List<VisualElement> views, bool push);

    /// <summary>Whether the front layer's page grows out of a view, or shrinks into one, rather than pictures flying.</summary>
    public partial bool IsZoom { get; }

    /// <summary>Flies every picture to its place.</summary>
    public partial Task FlyAsync(uint length, Easing easing);

    /// <summary>Grows the front layer's page out of its view (<paramref name="push"/>), or shrinks it back into it.</summary>
    public partial Task ZoomAsync(bool push, uint length);

    /// <summary>
    /// Puts the front layer's page where a back-swipe has it, <paramref name="progress"/> of the way
    /// across: shrunk, and moved by <paramref name="x"/> and <paramref name="y"/> with the finger.
    /// </summary>
    public partial void Follow(double x, double y, double progress);

    /// <summary>Brings the page back to its full size after a back-swipe that was let go of.</summary>
    public partial Task RestoreAsync(uint length);

    private partial void RemovePictures();

#if !IOS && !MACCATALYST
    private static partial SharedElementFlight? Create(View container, View front) => null;
    private partial void Add(List<VisualElement> sources, List<VisualElement> targets) { }
    private partial bool AddZoom(List<VisualElement> views, bool push) => false;
    public partial bool IsZoom => false;
    public partial Task FlyAsync(uint length, Easing easing) => Task.CompletedTask;
    public partial Task ZoomAsync(bool push, uint length) => Task.CompletedTask;
    public partial void Follow(double x, double y, double progress) { }
    public partial Task RestoreAsync(uint length) => Task.CompletedTask;
    private partial void RemovePictures() { }
#endif
}
