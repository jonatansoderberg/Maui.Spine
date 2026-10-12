namespace Plugin.Maui.Spine.Biometrics;

/// <summary>
/// Keeps the page out of screenshots, screen recordings and the app switcher while it is on screen, as far
/// as the platform allows: Android blocks capture (<c>FLAG_SECURE</c>, sheets included), Windows excludes
/// the window from capture, and iOS covers the app-switcher snapshot. iOS cannot block a screenshot.
/// </summary>
/// <example>
/// <code>
/// [NavigableRegion(Title = "Statement"), PrivacyShield, RequiresUnlock("Show the statement", Relock = true)]
/// public partial class StatementPage { … }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class PrivacyShieldAttribute : Attribute
{
    /// <summary>
    /// On iOS 17 and later, also covers the app while the screen is recorded, mirrored or shared and the
    /// page is on screen.
    /// </summary>
    public bool CoverWhileCaptured { get; init; }
}
