using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Biometrics;

/// <summary>
/// Asks the user to unlock (Face ID, Touch ID, fingerprint, Windows Hello or the device passcode) before the
/// page opens or the command runs. A recent unlock counts for <see cref="SpineBiometricsOptions.UnlockValidFor"/>.
/// </summary>
/// <remarks>
/// On a command it applies wherever Spine runs the command (a page action, a menu or action sheet row,
/// <c>Tap.Command</c>, <c>{PageCommand}</c>); a plain <c>Command="{Binding …}"</c> on a button does not ask.
/// Use <see cref="Unlock.CommandProperty"/> there, or <see cref="IUnlock.RequireAsync"/> in code.
/// </remarks>
/// <example>
/// <code>
/// [NavigableRegion(Title = "Card"), RequiresUnlock("Show your card details")]
/// public partial class CardPage { … }
///
/// [PageAction(Svg = "delete.svg"), RequiresUnlock("Delete your account", Fresh = true)]
/// [RelayCommand]
/// private Task DeleteAsync() { … }
/// </code>
/// </example>
public sealed class RequiresUnlockAttribute : GuardAttribute
{
    /// <summary>Asks with <paramref name="reason"/>: text, or the key of a Spine string.</summary>
    public RequiresUnlockAttribute(string reason)
    {
        Reason = reason;
        Order = 200;
    }

    /// <summary>Why the app asks, shown in the prompt: text, or the key of a Spine string.</summary>
    public string Reason { get; }

    /// <summary>Asks every time, even right after an unlock. For something that cannot be undone.</summary>
    public bool Fresh { get; init; }

    /// <summary>
    /// On a page: covers it with the unlock screen when the app comes back after
    /// <see cref="AppLockOptions.LockAfter"/> in the background while the page is on screen. The page stays
    /// where it is, so its state is kept, and the screen has a Back button that leaves it instead.
    /// </summary>
    public bool Relock { get; init; }
}
