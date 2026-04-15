namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Cross-platform contract that provides measured system bar insets (status bar,
/// navigation bar, display cutouts) in device-independent pixels.
/// Spine registers a platform-specific singleton implementation automatically.
/// </summary>
/// <remarks>
/// On Android the insets are measured from <c>WindowInsetsCompat.Type.SystemBars()</c>
/// after edge-to-edge is enabled. On other platforms the insets are always
/// <see cref="Thickness.Zero"/> because the system bars are either handled natively or
/// do not require manual compensation.
/// </remarks>
public interface ISystemInsetsProvider
{
    /// <summary>
    /// The measured system bar insets in device-independent pixels.
    /// <list type="bullet">
    ///   <item><description><see cref="Thickness.Top"/> — status bar / display cutout height.</description></item>
    ///   <item><description><see cref="Thickness.Bottom"/> — navigation / gesture bar height.</description></item>
    ///   <item><description><see cref="Thickness.Left"/> / <see cref="Thickness.Right"/> — display cutout side insets.</description></item>
    /// </list>
    /// Returns <see cref="Thickness.Zero"/> until the first measurement completes
    /// and on platforms that do not require inset management.
    /// </summary>
    Thickness SystemBarInsets { get; }

    /// <summary>
    /// Raised when <see cref="SystemBarInsets"/> changes (e.g. first measurement on
    /// Android or after a configuration change such as screen rotation).
    /// </summary>
    event Action? InsetsChanged;
}
