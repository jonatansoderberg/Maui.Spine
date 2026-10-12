using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Controls;

/// <summary>Runs tours, one at a time, over the whole window.</summary>
internal static class Tours
{
    static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Shows <paramref name="steps"/> and marks <paramref name="key"/> seen, unless null, when the tour is completed or skipped.</summary>
    /// <param name="key">The tour's key, or <see langword="null"/> for none.</param>
    /// <param name="steps">The steps, in order.</param>
    /// <param name="cancellationToken">Ends the tour as cancelled.</param>
    public static async Task<TourResult> RunAsync(string? key, IReadOnlyList<TourStep> steps, CancellationToken cancellationToken = default)
    {
        if (key is not null && HighlightKeys.HasSeen(key))
            return new TourResult(TourOutcome.AlreadySeen, []);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (key is not null && HighlightKeys.HasSeen(key))
                return new TourResult(TourOutcome.AlreadySeen, []);

            var result = await MainThread.InvokeOnMainThreadAsync(() => ShowAsync(steps, cancellationToken));
            if (key is not null && result.Outcome is TourOutcome.Completed or TourOutcome.Skipped)
                HighlightKeys.MarkSeen(key);

            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    static async Task<TourResult> ShowAsync(IReadOnlyList<TourStep> steps, CancellationToken cancellationToken)
    {
        var missing = new List<(int Step, string Reason)>();
        var shown = Available(steps, missing);

        SpotlightView? view = null;
        IDisposable? overlay = null;
        var outcome = TourOutcome.Completed;
        var number = 0;

        // A page pushed by a notification, a sheet opening or the user going back: the targets are gone.
        var pagesAtStart = Pages();
        void OnShownPagesChanged(object? sender, EventArgs e)
        {
            if (!Pages().SequenceEqual(pagesAtStart))
                view?.Answer(SpotlightAnswer.Cancelled);
        }

        SpineScreen.ShownPagesChanged += OnShownPagesChanged;
        using var registration = cancellationToken.Register(() => MainThread.BeginInvokeOnMainThread(() => view?.Answer(SpotlightAnswer.Cancelled)));
        try
        {
            foreach (var index in shown)
            {
                var step = steps[index];
                number++;

                if (step.Target is VisualElement element)
                    await ScrollIntoViewAsync(element);

                if (!SpineTargets.TryGetFrame(step.Target, out var frame) || !InWindow(frame))
                {
                    missing.Add((index, "not on screen"));
                    continue;
                }

                if (view is null)
                {
                    var created = new SpotlightView();
                    overlay = SpineOverlay.Show(created, back: () => created.Answer(SpotlightAnswer.Skip));
                    view = created;
                }
                else
                {
                    Haptics.Play(Haptic.Selection);
                }

                var answer = await view.ShowStepAsync(frame, CornerRadius(step.Target), step, number, shown.Count);
                if (answer == SpotlightAnswer.Skip)
                {
                    outcome = TourOutcome.Skipped;
                    break;
                }

                if (answer == SpotlightAnswer.Cancelled)
                {
                    outcome = TourOutcome.Cancelled;
                    break;
                }
            }

            if (view is null)
                outcome = TourOutcome.Cancelled;
        }
        finally
        {
            SpineScreen.ShownPagesChanged -= OnShownPagesChanged;
            if (view is not null)
                await view.FadeOutAsync();
            overlay?.Dispose();
        }

        missing.Sort();
        foreach (var (step, reason) in missing)
            System.Diagnostics.Debug.WriteLine($"[Spine] Tour step {step} ({Describe(steps[step].Target)}) was left out: {reason}.");

        return new TourResult(outcome, missing);
    }

    /// <summary>The steps whose target exists now; the others go to <paramref name="missing"/> with the reason.</summary>
    static List<int> Available(IReadOnlyList<TourStep> steps, List<(int Step, string Reason)> missing)
    {
        var available = new List<int>();
        for (var i = 0; i < steps.Count; i++)
        {
            var reason = steps[i].Target switch
            {
                VisualElement { IsLoaded: false } => "not on screen",
                VisualElement { IsVisible: false } => "hidden",
                VisualElement { Width: <= 0 } or VisualElement { Height: <= 0 } => "no size",
                VisualElement => null,
                PageAction action when !SpineTargets.TryGetFrame(action, out _) => $"no view for PageAction '{action.Text ?? action.Svg}'",
                Type type when !SpineTargets.TryGetFrame(type, out _) => $"no tab for {type.Name}",
                PageAction or Type => null,
                var other => $"{other?.GetType().Name ?? "null"} is not a target: use a VisualElement, a PageAction or a tab page's Type",
            };

            if (reason is null)
                available.Add(i);
            else
                missing.Add((i, reason));
        }

        return available;
    }

    /// <summary>Scrolls the nearest scroll view round <paramref name="element"/> so it is in sight.</summary>
    static async Task ScrollIntoViewAsync(VisualElement element)
    {
        if (SpineTargets.TryGetFrame(element, out var frame) && InWindow(frame, whole: true))
            return;

        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScrollView scrollView)
            {
                await scrollView.ScrollToAsync(element, ScrollToPosition.Center, animated: !ReducedMotion.IsOn);
                return;
            }
        }
    }

    static bool InWindow(Rect frame, bool whole = false)
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is null || window.Width <= 0)
            return true;

        return whole
            ? frame.Top >= 0 && frame.Left >= 0 && frame.Bottom <= window.Height && frame.Right <= window.Width
            : frame.Bottom > 0 && frame.Right > 0 && frame.Top < window.Height && frame.Left < window.Width;
    }

    /// <summary>
    /// The target's corner radius, for a hole of the same shape: a view's own (a Border's shape, a button's
    /// radius, else its platform view's), a header action's round button, a tab item's capsule (iOS 26) or rounding.
    /// </summary>
    static double CornerRadius(object target) => target switch
    {
        PageAction => double.MaxValue,
        Type => OperatingSystem.IsIOSVersionAtLeast(26) || OperatingSystem.IsMacCatalystVersionAtLeast(26) ? double.MaxValue : 16,
        View view => HighlightLook.CornerRadiusOf(view) ?? PlatformCornerRadius(view),
        _ => 0,
    };

    static double PlatformCornerRadius(View view)
    {
#if IOS || MACCATALYST
        if (view.Handler?.PlatformView is UIKit.UIView platformView)
            return platformView.Layer.CornerRadius;
#elif WINDOWS
        if (view.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Control control)
            return control.CornerRadius.TopLeft;
#endif
        // Android draws a button's rounding in its background, which has no radius to read.
        return view is Button or ImageButton ? 8 : 0;
    }

    static List<View> Pages() => [.. SpineScreen.ShownPages.Select(page => page.Page)];

    static string Describe(object target) => target switch
    {
        PageAction action => $"PageAction '{action.Text ?? action.Svg}'",
        Type type => type.Name,
        _ => target?.GetType().Name ?? "null",
    };
}
