using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The look of <see cref="Shimmer"/> and of layouts under <see cref="Skeleton.IsActiveProperty"/>.
/// Set it on one control, or app-wide as a resource keyed <c>DefaultShimmerStyleOptions</c>;
/// colours left unset follow the theme.
/// </summary>
public class ShimmerStyleOptions : SpineStyleOptions<ShimmerStyleOptions>
{
    /// <summary>
    /// The fill of a placeholder that has no colour of its own: an empty <c>Border</c> or <c>BoxView</c>
    /// in a <see cref="Shimmer"/>, every block of a skeleton layout. Unset: a neutral grey for the theme.
    /// </summary>
    public Color? PlaceholderColor { get; set; }

    /// <summary>The colour of the band that sweeps across the placeholders. Unset: a lighter grey for the theme.</summary>
    public Color? WaveColor { get; set; }

    /// <summary>Peak alpha at the centre of the band's gradient, 0–1; the edges fade to 0.</summary>
    public double WaveOpacity { get; set; } = 0.3;

    /// <summary>
    /// Width of the band as a fraction of the shimmer's (or skeleton layout's) own width, 0–1; 1 is as
    /// wide as the control. The band travels from fully off the left edge to fully off the right in
    /// <see cref="WaveDuration"/>, so a wider band also keeps each spot lit for longer.
    /// </summary>
    public double WaveWidth { get; set; } = 0.22;

    /// <summary>Tilt of the band in degrees; 0 is vertical.</summary>
    public double WaveAngle { get; set; } = 15;

    /// <summary>One sweep, from off-canvas left to off-canvas right.</summary>
    public TimeSpan WaveDuration { get; set; } = TimeSpan.FromMilliseconds(1100);

    /// <summary>
    /// Corner radius of the blocks a skeleton layout draws for its views. A view inside a
    /// <c>Border</c> of its own takes that border's shape instead.
    /// </summary>
    public double CornerRadius { get; set; } = 4;

    protected override void InheritColorsFrom(ShimmerStyleOptions source)
    {
        PlaceholderColor ??= source.PlaceholderColor;
        WaveColor ??= source.WaveColor;
    }

    protected override void ApplyThemeDefaults(AppTheme theme)
    {
        var dark = theme == AppTheme.Dark;
        PlaceholderColor ??= dark ? Color.FromArgb("#2C2C2E") : Color.FromArgb("#E5E5EA");
        WaveColor ??= dark ? Color.FromArgb("#8E8E93") : Colors.White;
    }
}
