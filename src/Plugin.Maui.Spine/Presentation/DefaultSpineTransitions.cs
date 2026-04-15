namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// The default <see cref="ISpineTransitions"/> implementation.
/// Provides platform-tuned horizontal slide animations for push/pop navigation and interactive back-swipe gestures.
/// Register a custom <see cref="ISpineTransitions"/> in DI after calling <c>UseSpine</c> to override these defaults.
/// </summary>
public class DefaultSpineTransitions : ISpineTransitions
{
    /// <summary>Animation duration in milliseconds, tuned per platform.</summary>
    protected virtual uint Duration => GetPlatformDuration();

    /// <summary>Easing function applied to all transitions.</summary>
    protected virtual Easing Ease => Easing.CubicOut;

    private static uint GetPlatformDuration()
    {
        // Align transition timing with typical platform navigation durations
        return DeviceInfo.Platform switch
        {
            var p when p == DevicePlatform.iOS || p == DevicePlatform.MacCatalyst => 300,
            var p when p == DevicePlatform.Android => 300,
            var p when p == DevicePlatform.WinUI => 250,
            _ => 300
        };
    }

    /// <summary>Returns the width of the given view, falling back to the current window width.</summary>
    protected static double GetWidth(View view)
    {
        if (view?.Width > 0)
            return view.Width;

        return Application.Current?.Windows[0]?.Page?.Width ?? 400;
    }

    /// <inheritdoc/>
    public virtual Task AnimateSetRootAsync(View view)
    {
        view.Opacity = 1;
        view.IsVisible = true;
        return Task.CompletedTask;
    }

    // PUSH (forward)

    /// <inheritdoc/>
    public virtual async Task AnimateNavigateToShowAsync(View view)
    {
        var width = GetWidth(view);

        view.TranslationX = width;
        view.Opacity = 1;
        view.IsVisible = true;

        await view.TranslateToAsync(0, 0, Duration, Ease);
    }

    /// <inheritdoc/>
    public virtual Task AnimateNavigateToHideAsync(View view)
    {
        view.IsVisible = false;
        return Task.CompletedTask;
    }

    // POP (back)

    /// <inheritdoc/>
    public virtual async Task AnimateBackShowAsync(View view)
    {
        var width = GetWidth(view);

        view.TranslationX = -width * 0.25;
        view.Opacity = 1;
        view.IsVisible = true;

        await view.TranslateToAsync(0, 0, Duration, Ease);
    }

    /// <inheritdoc/>
    public virtual Task AnimateBackHideAsync(View view)
    {
        view.IsVisible = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task ResetHiddenViewAsync(View view)
    {
        view.Opacity = 1;
        view.IsVisible = true;
        return Task.CompletedTask;
    }

    // Interactive back-swipe terminal animations

        /// <inheritdoc/>
        public virtual Task AnimateInteractiveBackCompleteAsync(View front, View back, double progress)
        {
            var width = GetWidth(front);
            var remaining = width - progress;

            var frontTask = front.TranslateToAsync(progress + remaining, 0, Duration, Ease);
            var backTask = back.TranslateToAsync(0, 0, Duration, Ease);

            return Task.WhenAll(frontTask, backTask);
        }

        /// <inheritdoc/>
        public virtual Task AnimateInteractiveBackCancelAsync(View front, View back)
        {
            var width = GetWidth(front);

            var frontTask = front.TranslateToAsync(0, 0, Duration, Ease);
            var backTask = back.TranslateToAsync(-width * 0.25, 0, Duration, Ease);

            return Task.WhenAll(frontTask, backTask);
        }

        /// <inheritdoc/>
        public virtual uint InteractiveGestureDuration => Duration;

        /// <inheritdoc/>
        public virtual Easing InteractiveGestureEasing => Ease;
    }
