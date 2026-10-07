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
    private readonly List<VisualElement> _hidden = [];

    // Every view a flight has hidden, how many flights hide it, and its opacity before the first
    // did. Two flights can overlap on a view (a drag let go while a pop starts), and the second
    // must not take the first one's 0 for the view's own opacity and leave it hidden for good.
    private static readonly Dictionary<VisualElement, (int Count, double Opacity)> HiddenViews = [];

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
            await SettleAsync(front, Transition.TaggedIn(arriving).Values.SelectMany(views => views).OfType<IZoomFocus>().FirstOrDefault());

        // Looked up again: the page may have built views while it settled.
        var targets = Transition.TaggedIn(arriving);
        if (Create(container, front) is not { } flight)
            return null;

        // The page in the front layer, the one that grows or shrinks when its own tag is set.
        if (((push ? arriving : leaving) as PagePresenter)?.Content is { } page && Transition.GetTag(page) is { Length: > 0 } pageTag)
        {
            // The page's own view with the same tag, if it has one, is what lines up with the other
            // page's view: the page shrinks into it as that view, not as a miniature of itself.
            var focus = (push ? targets : sources).TryGetValue(pageTag, out var own)
                ? own.FirstOrDefault(view => !ReferenceEquals(view, page))
                : null;

            // Without its view on screen the page moves as usual: a picture of the whole page
            // flying into a stranger's place would not be a zoom.
            if ((push ? sources : targets).TryGetValue(pageTag, out var views) && flight.AddZoom(views, push, focus))
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
    /// <remarks>
    /// A focus that shows a picture of its own, such as a lightbox's image, is waited for as well,
    /// for a moment: until the picture has arrived, it has no size to line up with.
    /// </remarks>
    private static async Task SettleAsync(View layer, IZoomFocus? focus)
    {
        layer.Opacity = 0;

        try
        {
            await NextLayoutAsync(layer);

            if (focus is not null)
                await Task.WhenAny(focus.WhenReadyAsync(), Task.Delay(FocusWait));
        }
        finally
        {
            layer.Opacity = 1;
        }
    }

    /// <summary>How long a push waits for a focus's picture to arrive before it zooms without it, in milliseconds.</summary>
    private const int FocusWait = 300;

    /// <summary>
    /// Waits until <paramref name="layer"/> has been laid out with the page it was just given:
    /// one turn of the main queue on iOS, where the layout itself is synchronous; the next layout
    /// pass on Android, which runs ahead of the frame it draws.
    /// </summary>
    private static partial Task NextLayoutAsync(View layer);

    private void Hide(params VisualElement[] views)
    {
        foreach (var view in views)
        {
            HiddenViews[view] = HiddenViews.TryGetValue(view, out var hidden)
                ? (hidden.Count + 1, hidden.Opacity)
                : (1, view.Opacity);

            _hidden.Add(view);
            view.Opacity = 0;
        }
    }

    /// <summary>Shows the views again, unless another flight still hides them, and takes the pictures away.</summary>
    public void Dispose()
    {
        foreach (var view in _hidden)
        {
            if (!HiddenViews.TryGetValue(view, out var hidden))
                continue;

            if (hidden.Count > 1)
            {
                HiddenViews[view] = (hidden.Count - 1, hidden.Opacity);
                continue;
            }

            HiddenViews.Remove(view);
            view.Opacity = hidden.Opacity;
        }

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
    /// <paramref name="focus"/>, a view on the page itself, is the part of the page that lines up
    /// with that view; without one, the page's middle does.
    /// </summary>
    private partial bool AddZoom(List<VisualElement> views, bool push, VisualElement? focus);

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

    /// <summary>
    /// Puts the front layer's page where a lightbox's drag down has it: shrunk by
    /// <paramref name="progress"/> about the point <paramref name="anchor"/> (in the page's
    /// coordinates) where the finger came down, which stays under the finger as it moves by
    /// <paramref name="x"/> and <paramref name="y"/>.
    /// </summary>
    public partial void Carry(Point anchor, double x, double y, double progress);

    /// <summary>Brings the page back to its full size after a back-swipe that was let go of.</summary>
    public partial Task RestoreAsync(uint length);

    private partial void RemovePictures();

#if !IOS && !MACCATALYST && !ANDROID
    private static partial Task NextLayoutAsync(View layer) => Task.CompletedTask;
    private static partial SharedElementFlight? Create(View container, View front) => null;
    private partial void Add(List<VisualElement> sources, List<VisualElement> targets) { }
    private partial bool AddZoom(List<VisualElement> views, bool push, VisualElement? focus) => false;
    public partial bool IsZoom => false;
    public partial Task FlyAsync(uint length, Easing easing) => Task.CompletedTask;
    public partial Task ZoomAsync(bool push, uint length) => Task.CompletedTask;
    public partial void Follow(double x, double y, double progress) { }
    public partial void Carry(Point anchor, double x, double y, double progress) { }
    public partial Task RestoreAsync(uint length) => Task.CompletedTask;
    private partial void RemovePictures() { }
#endif
}

/// <summary>
/// A view on a zoom page whose focus is a part of it that has a native view of its own, such as the
/// image a <see cref="Lightbox"/> shows, rather than the view itself. The page then lines that part
/// up with the other page's view, and is cut to that view's shape within it: a square thumbnail
/// grows into the whole image around it.
/// </summary>
internal interface IZoomFocus
{
#if IOS || MACCATALYST
    /// <summary>The native view that shows the focus, or <see langword="null"/> when there is none yet.</summary>
    UIKit.UIView? FocusView { get; }
#elif ANDROID
    /// <summary>Where the focus is drawn, in <paramref name="ancestor"/>'s pixels, or <see langword="null"/> when there is none yet.</summary>
    Android.Graphics.RectF? FocusIn(Android.Views.View ancestor);
#endif

    /// <summary>Completes once the focus has its picture and its place.</summary>
    Task WhenReadyAsync();
}
