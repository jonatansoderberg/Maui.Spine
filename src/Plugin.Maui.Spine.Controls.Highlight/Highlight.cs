using System.ComponentModel;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Draws attention to any view: a moving rim while a model works on it (<see cref="HighlightKind.Edge"/>),
/// rings that say "tap here" (<see cref="HighlightKind.Pulse"/>), a light sweep, a running point of light
/// or a breathing halo. Each kind is drawn by the platform's compositor above the view's content, so it
/// costs no layout pass and works on a <see cref="Button"/> as well as on a <see cref="Border"/>.
/// </summary>
/// <remarks>
/// <para>
/// Halos (Pulse, Breathe and Edge's glow) draw up to <see cref="OutsetProperty"/> outside the view; a parent
/// that clips its children cuts them. Under Reduce Motion each kind is still: a rim, a ring or a halo that
/// fades in and out. A highlight pauses while its view is off screen or the app is in the background.
/// </para>
/// <para>
/// The attached properties need no registration. <see cref="IHighlights"/> (play from code, keys) needs
/// <c>UseHighlights()</c>, which <c>UseSpine()</c> calls for an app that references the package.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;!-- plays until tapped, once per install --&gt;
/// &lt;Button Text="Add" Highlight.Kind="Pulse" Highlight.Repeat="UntilTapped" Highlight.Key="onboarding.add" /&gt;
///
/// &lt;!-- bound to the model's busy state --&gt;
/// &lt;Border Highlight.Kind="Edge" Highlight.IsActive="{Binding IsSummarising}" Highlight.Tip="Summarising" /&gt;
/// </code>
/// </example>
public static class Highlight
{
    /// <summary>Attached property: how the view is highlighted. <see cref="HighlightKind.None"/> removes it.</summary>
    public static readonly BindableProperty KindProperty =
        BindableProperty.CreateAttached("Kind", typeof(HighlightKind), typeof(Highlight), HighlightKind.None,
            propertyChanged: OnKindChanged);

    /// <summary>
    /// Attached property: how often it plays. Unset, Edge, Trace and Breathe run until turned off, and Pulse
    /// and Sheen play once. In XAML: <c>Once</c>, <c>Forever</c>, <c>UntilTapped</c> or a number.
    /// </summary>
    public static readonly BindableProperty RepeatProperty =
        BindableProperty.CreateAttached("Repeat", typeof(HighlightRepeat), typeof(Highlight), default(HighlightRepeat),
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Restart());

    /// <summary>
    /// Attached property: whether it plays, <see langword="true"/> by default. Bind it to a busy flag; each
    /// time it turns true the highlight starts again.
    /// </summary>
    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.CreateAttached("IsActive", typeof(bool), typeof(Highlight), true,
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Restart());

    /// <summary>
    /// Attached property: a key that makes the highlight play only until it has been seen once on this
    /// install. It counts as seen when the highlight ends by itself or by a tap, not when the page goes
    /// away while it plays. <see cref="IHighlights.Reset"/> forgets it.
    /// </summary>
    public static readonly BindableProperty KeyProperty =
        BindableProperty.CreateAttached("Key", typeof(string), typeof(Highlight), null,
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Restart());

    /// <summary>
    /// Attached property: a short text for screen readers, announced when the highlight starts. While Edge,
    /// Trace or Breathe runs it is also the view's semantic hint.
    /// </summary>
    public static readonly BindableProperty TipProperty =
        BindableProperty.CreateAttached("Tip", typeof(string), typeof(Highlight), null,
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Refresh());

    /// <summary>
    /// Attached property: a view shown beside the tip of a <see cref="HighlightKind.Spotlight"/> hint, such as an avatar
    /// presenter; an <see cref="Plugin.Maui.Spine.Core.ITipPresenter"/> is given the tip and the view's frame.
    /// </summary>
    public static readonly BindableProperty PresenterProperty =
        BindableProperty.CreateAttached("Presenter", typeof(View), typeof(Highlight), null);

    /// <summary>Attached property: 0–1, 1 by default. The rim's width, the opacity and the glow scale with it.</summary>
    public static readonly BindableProperty IntensityProperty =
        BindableProperty.CreateAttached("Intensity", typeof(double), typeof(Highlight), 1d,
            coerceValue: static (_, value) => Math.Clamp((double)value, 0, 1),
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Refresh());

    /// <summary>
    /// Attached property: the colours. Unset, Edge uses a spectrum and the other kinds the app's accent. In
    /// XAML a comma-separated list: <c>Highlight.Colors="#5B8CFF, #FF6FA8"</c>.
    /// </summary>
    public static readonly BindableProperty ColorsProperty =
        BindableProperty.CreateAttached("Colors", typeof(IReadOnlyList<Color>), typeof(Highlight), null,
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Refresh());

    /// <summary>Attached property: the room, in points, that halos may use outside the view. 12 by default.</summary>
    public static readonly BindableProperty OutsetProperty =
        BindableProperty.CreateAttached("Outset", typeof(double), typeof(Highlight), 12d,
            coerceValue: static (_, value) => Math.Max(0, (double)value),
            propertyChanged: static (bindable, _, _) => GetState(bindable)?.Refresh());

    static readonly BindableProperty StateProperty =
        BindableProperty.CreateAttached("State", typeof(HighlightState), typeof(Highlight), null);

    /// <summary>Raised when a highlight ends by itself or by a tap; not when it is cleared or turned off.</summary>
    public static event EventHandler<HighlightEndedEventArgs>? Ended;

    public static HighlightKind GetKind(BindableObject view) => (HighlightKind)view.GetValue(KindProperty);

    public static void SetKind(BindableObject view, HighlightKind value) => view.SetValue(KindProperty, value);

    public static HighlightRepeat GetRepeat(BindableObject view) => (HighlightRepeat)view.GetValue(RepeatProperty);

    public static void SetRepeat(BindableObject view, HighlightRepeat value) => view.SetValue(RepeatProperty, value);

    public static bool GetIsActive(BindableObject view) => (bool)view.GetValue(IsActiveProperty);

    public static void SetIsActive(BindableObject view, bool value) => view.SetValue(IsActiveProperty, value);

    public static string? GetKey(BindableObject view) => (string?)view.GetValue(KeyProperty);

    public static void SetKey(BindableObject view, string? value) => view.SetValue(KeyProperty, value);

    public static View? GetPresenter(BindableObject view) => (View?)view.GetValue(PresenterProperty);

    public static void SetPresenter(BindableObject view, View? value) => view.SetValue(PresenterProperty, value);

    public static string? GetTip(BindableObject view) => (string?)view.GetValue(TipProperty);

    public static void SetTip(BindableObject view, string? value) => view.SetValue(TipProperty, value);

    public static double GetIntensity(BindableObject view) => (double)view.GetValue(IntensityProperty);

    public static void SetIntensity(BindableObject view, double value) => view.SetValue(IntensityProperty, value);

    [TypeConverter(typeof(HighlightColorsConverter))]
    public static IReadOnlyList<Color>? GetColors(BindableObject view) => (IReadOnlyList<Color>?)view.GetValue(ColorsProperty);

    public static void SetColors(BindableObject view, IReadOnlyList<Color>? value) => view.SetValue(ColorsProperty, value);

    public static double GetOutset(BindableObject view) => (double)view.GetValue(OutsetProperty);

    public static void SetOutset(BindableObject view, double value) => view.SetValue(OutsetProperty, value);

    internal static HighlightState? GetState(BindableObject view) => (HighlightState?)view.GetValue(StateProperty);

    internal static void RaiseEnded(View view, HighlightKind kind, bool byTap) =>
        Ended?.Invoke(view, new HighlightEndedEventArgs(view, kind, byTap));

    static void OnKindChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not View view)
            return;

        var state = GetState(view);
        if (newValue is HighlightKind.None)
        {
            if (state is not null)
            {
                state.Dispose();
                view.ClearValue(StateProperty);
            }

            return;
        }

        if (state is null)
        {
            view.SetValue(StateProperty, new HighlightState(view));
            return;
        }

        state.Restart();
    }
}

/// <summary>Which highlight ended, and whether a tap ended it.</summary>
public sealed class HighlightEndedEventArgs(View view, HighlightKind kind, bool byTap) : EventArgs
{
    public View View { get; } = view;

    public HighlightKind Kind { get; } = kind;

    /// <summary>A tap on the view ended it (<see cref="HighlightRepeat.UntilTapped"/>).</summary>
    public bool ByTap { get; } = byTap;
}

/// <summary>Converts a comma-separated list of colours, such as <c>"#5B8CFF, Orange"</c>, for <see cref="Highlight.ColorsProperty"/>.</summary>
public sealed class HighlightColorsConverter : TypeConverter
{
    static readonly Microsoft.Maui.Graphics.Converters.ColorTypeConverter ColorConverter = new();

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value) =>
        value is string text
            ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => (Color)ColorConverter.ConvertFromInvariantString(part)!)
                .ToArray()
            : base.ConvertFrom(context, culture, value);
}
