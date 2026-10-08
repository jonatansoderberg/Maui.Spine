using Android;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.Content;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

public static partial class Haptics
{
    static Vibrator? _vibrator;
    static bool _vibratorChecked;

    static partial void PlayPlatform(Haptic haptic)
    {
        if (Options.Android.HapticEngine == AndroidHapticEngine.Vibrator && Vibrate(haptic))
            return;

        Platform.CurrentActivity?.Window?.DecorView?.PerformHapticFeedback(Feedback(haptic));
    }

    // The view engine reaches only the platform's prebaked effects (texture tick, tick, click, heavy
    // click, double click), so neighbouring impacts share one. TEXT_HANDLE_MOVE is left out: the
    // vibrator service ignores it outside a text handle drag.
    static FeedbackConstants Feedback(Haptic haptic) => haptic switch
    {
        Haptic.Success when OperatingSystem.IsAndroidVersionAtLeast(30) => FeedbackConstants.Confirm,
        Haptic.Error when OperatingSystem.IsAndroidVersionAtLeast(30) => FeedbackConstants.Reject,
        Haptic.Success or Haptic.Medium or Haptic.Rigid => FeedbackConstants.VirtualKey,
        Haptic.Warning or Haptic.Error or Haptic.Heavy => FeedbackConstants.LongPress,
        Haptic.Selection => FeedbackConstants.ClockTick,
        _ when OperatingSystem.IsAndroidVersionAtLeast(23) => FeedbackConstants.ContextClick,
        _ => FeedbackConstants.KeyboardTap,
    };

    /// <summary>Plays <paramref name="haptic"/> on the vibrator; <see langword="false"/> when it cannot.</summary>
    static bool Vibrate(Haptic haptic)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) || DeviceVibrator() is not { } vibrator)
            return false;

        var effect = Composed(vibrator, haptic) ?? VibrationEffect.CreatePredefined(haptic switch
        {
            Haptic.Success => VibrationEffect.EffectDoubleClick,
            Haptic.Warning or Haptic.Error or Haptic.Heavy => VibrationEffect.EffectHeavyClick,
            Haptic.Medium or Haptic.Rigid => VibrationEffect.EffectClick,
            _ => VibrationEffect.EffectTick,
        });

        // Touch usage puts the effect under the system's touch-feedback setting and intensity.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            vibrator.Vibrate(effect, VibrationAttributes.CreateForUsage((int)VibrationAttributesUsageType.Touch));
        else
            vibrator.Vibrate(effect);

        return true;
    }

    /// <summary>A composed pattern on API 30+, or <see langword="null"/> when the vibrator lacks the primitives.</summary>
    static VibrationEffect? Composed(Vibrator vibrator, Haptic haptic)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return null;

        (VibrationEffectCompositionPrimitive Primitive, float Scale, int Delay)[] steps = haptic switch
        {
            Haptic.Success => [(VibrationEffectCompositionPrimitive.Click, 0.6f, 0), (VibrationEffectCompositionPrimitive.Click, 1f, 80)],
            Haptic.Warning => [(VibrationEffectCompositionPrimitive.Click, 1f, 0), (VibrationEffectCompositionPrimitive.Click, 0.6f, 100)],
            Haptic.Error => [(VibrationEffectCompositionPrimitive.Click, 1f, 0), (VibrationEffectCompositionPrimitive.Click, 1f, 60), (VibrationEffectCompositionPrimitive.Click, 1f, 60)],
            Haptic.Selection => [(VibrationEffectCompositionPrimitive.Tick, 0.6f, 0)],
            Haptic.Light => [(VibrationEffectCompositionPrimitive.Click, 0.4f, 0)],
            Haptic.Medium => [(VibrationEffectCompositionPrimitive.Click, 0.7f, 0)],
            Haptic.Heavy => [(VibrationEffectCompositionPrimitive.Click, 1f, 0)],
            Haptic.Soft => [(VibrationEffectCompositionPrimitive.Tick, 0.5f, 0)],
            _ => [(VibrationEffectCompositionPrimitive.Tick, 1f, 0)],
        };

        if (!vibrator.AreAllPrimitivesSupported(steps.Select(s => (int)s.Primitive).Distinct().ToArray()))
            return null;

        var composition = VibrationEffect.StartComposition();
        foreach (var (primitive, scale, delay) in steps)
            composition.AddPrimitive((int)primitive, scale, delay);

        return composition.Compose();
    }

    /// <summary>
    /// The device vibrator, when the app may use it. Missing permission or hardware falls back to
    /// <see cref="AndroidHapticEngine.View"/> with one warning in the log.
    /// </summary>
    static Vibrator? DeviceVibrator()
    {
        if (_vibratorChecked)
            return _vibrator;

        _vibratorChecked = true;
        var context = Platform.AppContext;

        if (ContextCompat.CheckSelfPermission(context, Manifest.Permission.Vibrate) != Permission.Granted)
        {
            Android.Util.Log.Warn("Spine", "HapticEngine.Vibrator needs <uses-permission android:name=\"android.permission.VIBRATE\" /> in AndroidManifest.xml; playing haptics through the view instead.");
            return null;
        }

        if (SystemVibrator() is not { HasVibrator: true } vibrator)
        {
            Android.Util.Log.Warn("Spine", "HapticEngine.Vibrator: this device has no vibrator; playing haptics through the view instead.");
            return null;
        }

        return _vibrator = vibrator;
    }

    /// <summary>The system's default vibrator; asking whether it has hardware needs no permission.</summary>
    static Vibrator? SystemVibrator()
    {
        var context = Platform.AppContext;
        return OperatingSystem.IsAndroidVersionAtLeast(31)
            ? (context.GetSystemService(Context.VibratorManagerService) as VibratorManager)?.DefaultVibrator
#pragma warning disable CA1422 // The pre-31 way to reach the vibrator.
            : context.GetSystemService(Context.VibratorService) as Vibrator;
#pragma warning restore CA1422
    }
}
