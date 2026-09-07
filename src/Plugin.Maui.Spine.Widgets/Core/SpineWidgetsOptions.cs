namespace Plugin.Maui.Spine.Widgets;

/// <summary>Options for <see cref="Extensions.SpineWidgetsExtensions.UseSpineWidgets"/>.</summary>
public sealed class SpineWidgetsOptions
{
    /// <summary>
    /// The App Group the app and the widget extension share on Apple platforms. Leave
    /// <see langword="null"/> to use the value the build wrote into Info.plist from the
    /// <c>SpineWidgetsAppGroup</c> property (default <c>group.&lt;ApplicationId&gt;</c>).
    /// </summary>
    public string? AppGroup { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default) every widget is rebuilt as the app moves to the
    /// background, so the home screen shows the state the user just left.
    /// </summary>
    public bool RefreshOnBackground { get; set; } = true;
}
