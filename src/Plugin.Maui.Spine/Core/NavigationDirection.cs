namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Describes the direction of a navigation event passed to
/// <see cref="ViewModelBase.OnAppearingAsync"/> and
/// <see cref="ViewModelBase.OnDisappearingAsync"/>.
/// </summary>
public enum NavigationDirection
{
    /// <summary>No specific direction. Used when a page is set as root or first shown.</summary>
    None = 0,

    /// <summary>The navigation moved forward — a new page was pushed onto the stack.</summary>
    NavigateTo = 1,

    /// <summary>The navigation moved backward — the current page was popped from the stack.</summary>
    Back = 2
}