namespace Plugin.Maui.Spine.Core;

/// <summary>How Spine's header bar sits on a page.</summary>
public enum HeaderBarMode
{
    /// <summary>The header bar takes its own row above the content.</summary>
    Normal,

    /// <summary>
    /// The header bar floats over the content, which starts at the top of the screen behind the
    /// status bar and the bar: for pages that open on a photo, a map or a hero. The title and the
    /// actions keep a fixed colour through <c>HeaderBarForeground</c>, and
    /// <see cref="ViewModelBase.SafeAreaInsets"/> reports the height the content must keep clear
    /// (status bar plus header bar) so a list can take it with <c>SafeArea.ScrollInset="Top"</c>.
    /// </summary>
    Overlay,
}

/// <summary>The colour of the status bar's clock and icons while a page is shown.</summary>
public enum StatusBarStyle
{
    /// <summary>Follows the app theme: dark content on a light theme, light content on a dark one.</summary>
    Default,

    /// <summary>Light (white) clock and icons, for a page whose top is dark or a photo.</summary>
    LightContent,

    /// <summary>Dark (black) clock and icons, for a page whose top is light.</summary>
    DarkContent,
}
