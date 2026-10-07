#if IOS || MACCATALYST
using CoreGraphics;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineAnimation
{
    /// <remarks>
    /// MAUI applies a changed <c>TranslationX</c> or <c>Opacity</c> to the native view at once, so
    /// setting it inside the animator's block hands the change to Core Animation as a UIKit
    /// property animation would. The view model keeps the final value throughout, as it does after
    /// MAUI's own animations.
    /// </remarks>
    private static partial Task? AnimateOnPlatform(VisualElement view, uint length, Easing? easing, Action apply)
    {
        if (view.Handler?.PlatformView is not UIView || CurveOf(easing ?? Easing.Linear) is not { } curve)
            return null;

        // A MAUI animation still running on the same view would overwrite the values every frame.
        view.CancelAnimations();

        var done = new TaskCompletionSource();
        var animator = new UIViewPropertyAnimator(length / 1000.0, new UICubicTimingParameters(curve.Item1, curve.Item2));
        animator.AddAnimations(apply);
        animator.AddCompletion(_ => done.TrySetResult());
        animator.StartAnimation();
        return done.Task;
    }

    /// <summary>
    /// The cubic Bézier control points of MAUI's built-in easings (the curves on easings.net, which
    /// MAUI's formulas follow); <see langword="null"/> for an easing that is not one of them.
    /// </summary>
    internal static (CGPoint, CGPoint)? CurveOf(Easing easing)
    {
        static (CGPoint, CGPoint) Curve(double x1, double y1, double x2, double y2) => (new(x1, y1), new(x2, y2));

        if (easing == Easing.Linear) return Curve(0, 0, 1, 1);
        if (easing == Easing.CubicIn) return Curve(0.32, 0, 0.67, 0);
        if (easing == Easing.CubicOut) return Curve(0.33, 1, 0.68, 1);
        if (easing == Easing.CubicInOut) return Curve(0.65, 0, 0.35, 1);
        if (easing == Easing.SinIn) return Curve(0.12, 0, 0.39, 0);
        if (easing == Easing.SinOut) return Curve(0.61, 1, 0.88, 1);
        if (easing == Easing.SinInOut) return Curve(0.37, 0, 0.63, 1);
        return null;
    }
}
#endif
