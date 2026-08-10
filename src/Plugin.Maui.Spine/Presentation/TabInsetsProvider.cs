using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// Per-tab <see cref="ISystemInsetsProvider"/> that wraps the global provider and lets the tab
/// host override the bottom inset. Inside a native tab host the bottom safe area of a child page
/// differs from the window's: on iOS it grows to include the (translucent, floating) tab bar the
/// content must scroll under; on Android the opaque bar consumes the bottom edge entirely.
/// </summary>
internal sealed class TabInsetsProvider : ISystemInsetsProvider
{
    private readonly ISystemInsetsProvider _inner;
    private double? _bottomOverride;

    /// <inheritdoc/>
    public event Action? InsetsChanged;

    public TabInsetsProvider(ISystemInsetsProvider inner)
    {
        _inner = inner;
        _inner.InsetsChanged += () => InsetsChanged?.Invoke();
    }

    /// <inheritdoc/>
    public Thickness SystemBarInsets
    {
        get
        {
            var insets = _inner.SystemBarInsets;
            return _bottomOverride is { } bottom
                ? new Thickness(insets.Left, insets.Top, insets.Right, bottom)
                : insets;
        }
    }

    /// <summary>
    /// Overrides the bottom inset reported to this tab's region (pass <see langword="null"/>
    /// to fall back to the global value). Raises <see cref="InsetsChanged"/> on change.
    /// </summary>
    public void SetBottomOverride(double? bottom)
    {
        if (_bottomOverride == bottom)
            return;

        _bottomOverride = bottom;
        InsetsChanged?.Invoke();
    }
}
