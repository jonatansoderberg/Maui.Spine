using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>A haptic the platform plays: a notification, a selection tick or an impact.</summary>
public enum Haptic
{
    /// <summary>Nothing.</summary>
    None,

    /// <summary>A light tick for a changed selection: a picked row, a moved slider stop, a switched tab.</summary>
    Selection,

    /// <summary>A task finished: saved, sent, registered.</summary>
    Success,

    /// <summary>A task produced a warning.</summary>
    Warning,

    /// <summary>A task failed.</summary>
    Error,

    /// <summary>A light impact, for small objects colliding or a page turning.</summary>
    Light,

    /// <summary>A medium impact.</summary>
    Medium,

    /// <summary>A heavy impact, for a large object landing.</summary>
    Heavy,

    /// <summary>A dull, flexible impact.</summary>
    Soft,

    /// <summary>A sharp, stiff impact.</summary>
    Rigid,
}

/// <summary>The weight of an impact for <see cref="Haptics.Impact(HapticImpact)"/>.</summary>
public enum HapticImpact
{
    /// <summary>See <see cref="Haptic.Light"/>.</summary>
    Light,

    /// <summary>See <see cref="Haptic.Medium"/>.</summary>
    Medium,

    /// <summary>See <see cref="Haptic.Heavy"/>.</summary>
    Heavy,

    /// <summary>See <see cref="Haptic.Soft"/>.</summary>
    Soft,

    /// <summary>See <see cref="Haptic.Rigid"/>.</summary>
    Rigid,
}

/// <summary>
/// Semantic haptics: success, warning, error, selection and impacts, played by the platform's own
/// generators so they follow the user's system haptics setting. On a <see cref="Button"/>, an
/// <see cref="ImageButton"/> or a view with <see cref="Tap.CommandProperty"/>, <see cref="OnTapProperty"/>
/// plays one on every tap.
/// </summary>
/// <remarks>
/// iOS uses <c>UINotificationFeedbackGenerator</c>, <c>UISelectionFeedbackGenerator</c> and
/// <c>UIImpactFeedbackGenerator</c>. Android uses <c>View.PerformHapticFeedback</c>, or
/// <c>VibrationEffect</c> with <see cref="SpineOptions.AndroidPlatformOptions.HapticEngine"/>.
/// Mac Catalyst and Windows play nothing. Safe to call from any thread. <see cref="IsSupported"/> says
/// whether this device can play any.
/// </remarks>
/// <example>
/// <code>
/// Haptics.Success();
/// Haptics.Impact(HapticImpact.Light);
///
/// &lt;Button Text="Follow" Haptics.OnTap="Selection" /&gt;
/// </code>
/// </example>
public static partial class Haptics
{
    /// <summary>
    /// Attached property: the haptic a tap plays on a <see cref="Button"/>, an <see cref="ImageButton"/>
    /// or a view with <see cref="Tap.CommandProperty"/>. Ignored on other views.
    /// </summary>
    public static readonly BindableProperty OnTapProperty =
        BindableProperty.CreateAttached(
            "OnTap",
            typeof(Haptic),
            typeof(Haptics),
            Haptic.None,
            propertyChanged: OnOnTapChanged);

    /// <summary>Gets the haptic a tap on <paramref name="view"/> plays.</summary>
    public static Haptic GetOnTap(BindableObject view) => (Haptic)view.GetValue(OnTapProperty);

    /// <summary>Sets the haptic a tap on <paramref name="view"/> plays.</summary>
    public static void SetOnTap(BindableObject view, Haptic value) => view.SetValue(OnTapProperty, value);

    internal static SpineOptions Options { get; set; } = new();

    /// <summary>
    /// Whether this device can play haptics: an iPhone with a Taptic Engine (not an iPad, not the
    /// simulator; read from Core Haptics), an Android device with a vibrator, and never Mac Catalyst or
    /// Windows. Fixed for the app's lifetime, so an app can hide a haptics setting where it would do
    /// nothing. The user's own system haptics setting does not change it.
    /// </summary>
    public static bool IsSupported => _isSupported ??= ReadIsSupported();

    static bool? _isSupported;

    static bool ReadIsSupported()
    {
#if IOS
        return CoreHaptics.CHHapticEngine.GetHardwareCapabilities().SupportsHaptics;
#elif ANDROID
        return SystemVibrator() is { HasVibrator: true };
#else
        return false;
#endif
    }

    /// <summary>Plays <paramref name="haptic"/>.</summary>
    public static void Play(Haptic haptic)
    {
        if (haptic == Haptic.None)
            return;

        if (MainThread.IsMainThread)
            PlayPlatform(haptic);
        else
            MainThread.BeginInvokeOnMainThread(() => PlayPlatform(haptic));
    }

    /// <summary>Plays <see cref="Haptic.Success"/>.</summary>
    public static void Success() => Play(Haptic.Success);

    /// <summary>Plays <see cref="Haptic.Warning"/>.</summary>
    public static void Warning() => Play(Haptic.Warning);

    /// <summary>Plays <see cref="Haptic.Error"/>.</summary>
    public static void Error() => Play(Haptic.Error);

    /// <summary>Plays <see cref="Haptic.Selection"/>.</summary>
    public static void Selection() => Play(Haptic.Selection);

    /// <summary>Plays an impact of the given weight.</summary>
    public static void Impact(HapticImpact impact) => Play(impact switch
    {
        HapticImpact.Medium => Haptic.Medium,
        HapticImpact.Heavy => Haptic.Heavy,
        HapticImpact.Soft => Haptic.Soft,
        HapticImpact.Rigid => Haptic.Rigid,
        _ => Haptic.Light,
    });

    /// <summary>Wakes the generator for <paramref name="haptic"/> ahead of a tap, so it plays without delay.</summary>
    internal static void Prepare(Haptic haptic)
    {
        if (haptic != Haptic.None && MainThread.IsMainThread)
            PreparePlatform(haptic);
    }

    static void OnOnTapChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        switch (bindable)
        {
            case Button button:
                button.Pressed -= OnPressed;
                button.Clicked -= OnClicked;
                if (newValue is not Haptic.None)
                {
                    button.Pressed += OnPressed;
                    button.Clicked += OnClicked;
                }
                break;
            case ImageButton button:
                button.Pressed -= OnPressed;
                button.Clicked -= OnClicked;
                if (newValue is not Haptic.None)
                {
                    button.Pressed += OnPressed;
                    button.Clicked += OnClicked;
                }
                break;
        }
    }

    static void OnPressed(object? sender, EventArgs e)
    {
        if (sender is BindableObject view)
            Prepare(GetOnTap(view));
    }

    static void OnClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject view)
            Play(GetOnTap(view));
    }

    static partial void PlayPlatform(Haptic haptic);

    static partial void PreparePlatform(Haptic haptic);
}
