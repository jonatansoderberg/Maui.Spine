#if MACCATALYST

using UIKit;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// On the Mac a page's ScrollView under the header bar scrolls only when its content is taller than the room under
/// the bar. UIScrollView stretches its content to its own height and the bar's inset comes on top, so the mouse wheel
/// would otherwise move a page that fits by the inset, without a scroll bar.
/// </summary>
internal static class ScrollFit
{
    const double Slack = 2;

    static readonly BindableProperty AttachedProperty =
        BindableProperty.CreateAttached("ScrollFitAttached", typeof(bool), typeof(ScrollFit), false);

    public static void Attach(ScrollView scroll)
    {
        if ((bool)scroll.GetValue(AttachedProperty))
            return;
        scroll.SetValue(AttachedProperty, true);

        void Watch(View? content)
        {
            if (content is null) return;
            content.SizeChanged += (_, _) => Update(scroll);
            content.MeasureInvalidated += (_, _) => scroll.Dispatcher.Dispatch(() => Update(scroll));
        }

        scroll.SizeChanged += (_, _) => Update(scroll);
        scroll.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ScrollView.Content))
            {
                Watch(scroll.Content);
                Update(scroll);
            }
        };
        Watch(scroll.Content);
        Update(scroll);
    }

    public static void Update(ScrollView scroll)
    {
        if (scroll.Content is not { } content || scroll.Width <= 0 || scroll.Height <= 0
            || scroll.Handler?.PlatformView is not UIScrollView view)
            return;

        var inset = view.AdjustedContentInset;
        var visible = (double)(view.Bounds.Height - inset.Top - inset.Bottom);
        var natural = ((IView)content).Measure(scroll.Width, double.PositiveInfinity).Height;
        var overflows = natural > visible + Slack;
        view.ScrollEnabled = overflows;
        view.ShowsVerticalScrollIndicator = overflows;
        if (!overflows && view.ContentOffset.Y > -inset.Top)
            view.SetContentOffset(new CoreGraphics.CGPoint(0, -inset.Top), false);
    }
}

#endif
