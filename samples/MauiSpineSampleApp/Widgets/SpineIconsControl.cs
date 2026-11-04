using Plugin.Maui.Spine.Common;

namespace MauiSpineSampleApp.Widgets;

/// <summary>
/// A button in Control Center and Quick Settings that shows Spine's SVG icons, the next one on every tap. iOS draws
/// only symbols in a control, so on iOS each is a custom symbol in the extension's asset catalog
/// (SpineIcons.xcassets, which Plugin.Maui.Spine.Widgets builds into the extension): stroked icons outlined first, filled ones
/// as they are (#743).
/// </summary>
[Control(Kind)]
public sealed class SpineIconsControl : IControlProvider
{
    public const string Kind = "spine-icons";

    private const string IndexKey = "spine-icons-control.index";
    private static readonly string[] Icons = ["ArrowUpRight", "Bell", "More", "Info"];

    public Task<ControlState> GetStateAsync(ControlContext context, CancellationToken cancellationToken)
    {
        var icon = Icons[Preferences.Default.Get(IndexKey, 0) % Icons.Length];
        return Task.FromResult(ControlState.Button("Spine icons", icon, icon));
    }

    public Task OnActionAsync(ControlAction action)
    {
        Preferences.Default.Set(IndexKey, (Preferences.Default.Get(IndexKey, 0) + 1) % Icons.Length);
        return Task.CompletedTask;
    }
}
