namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Supplies one control — a toggle or a button in Control Center, on the Lock Screen and on the Action
/// button on iOS 18, and a Quick Settings tile on Android. Implementations are decorated with
/// <see cref="ControlAttribute"/>, discovered from the Spine assemblies, and constructed through DI every
/// time they run, so constructor injection works as in a view model. No C# runs in the native control:
/// it shows the state the app last stored, and a tap runs <see cref="OnActionAsync"/> in the app's process.
/// </summary>
public interface IControlProvider
{
    /// <summary>
    /// The control's state now. Called at launch, when the app moves to the background, after a tap,
    /// on <see cref="IControlService.RefreshAsync(string, CancellationToken)"/>, and on Android whenever
    /// the Quick Settings panel shows the tile.
    /// </summary>
    Task<ControlState> GetStateAsync(ControlContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called on the main thread when the control was tapped, with the app in the foreground, in the
    /// background or not running — the platform starts it in the background for the tap. The control is
    /// rebuilt with <see cref="GetStateAsync"/> when this returns.
    /// </summary>
    Task OnActionAsync(ControlAction action);
}

/// <summary>What a provider is asked to build.</summary>
/// <param name="Kind">The control kind being built.</param>
public sealed record ControlContext(string Kind);

/// <summary>A tapped control.</summary>
/// <param name="Kind">The control kind.</param>
/// <param name="IsOn">The value a toggle was switched to; <see langword="null"/> for a button.</param>
/// <param name="At">When the control was tapped; see <see cref="WidgetAction.At"/>.</param>
public sealed record ControlAction(string Kind, bool? IsOn, DateTimeOffset At);
