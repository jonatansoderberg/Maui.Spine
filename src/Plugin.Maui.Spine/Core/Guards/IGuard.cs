namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Decides whether a navigation or a command guarded by <typeparamref name="TAttribute"/> may go on.
/// Registered with <see cref="SpineGuardServiceCollectionExtensions.AddSpineGuard{TAttribute, TGuard}"/>.
/// </summary>
/// <typeparam name="TAttribute">The attribute this guard answers for.</typeparam>
public interface IGuard<in TAttribute> where TAttribute : GuardAttribute
{
    /// <summary>
    /// Returns <see langword="true"/> to let the navigation or command go on. The guard owns any UI it
    /// needs (a prompt, a sheet) and any message on refusal. Return a completed task when nothing has
    /// to be asked. A guard that throws counts as a refusal.
    /// </summary>
    ValueTask<bool> CheckAsync(TAttribute attribute, GuardContext context, CancellationToken cancellationToken);
}

/// <summary>What a guard is asked about.</summary>
/// <param name="Trigger">What started the check.</param>
/// <param name="Page">The page about to be shown, or <see langword="null"/> for a command.</param>
/// <param name="Command">The command's member name, or <see langword="null"/> for a page.</param>
/// <param name="CanCancel">
/// <see langword="false"/> when there is nothing to stay on if the guard refuses (the root page, or the
/// first tab at launch): the guard should then keep asking, for example with a retry button, until it passes.
/// </param>
public sealed record GuardContext(GuardTrigger Trigger, Type? Page, string? Command, bool CanCancel);

/// <summary>What started a guard check.</summary>
public enum GuardTrigger
{
    /// <summary>A region page is pushed.</summary>
    Push,

    /// <summary>A sheet page is opened.</summary>
    Sheet,

    /// <summary>A tab is switched to, in code or by the user.</summary>
    Tab,

    /// <summary><c>ShowAsync</c>, which may go back to the page instead of pushing it.</summary>
    Show,

    /// <summary><c>SetRootAsync</c>.</summary>
    Root,

    /// <summary>A command Spine runs: a page action, a menu row, an action sheet row, <c>Tap.Command</c>, <c>{PageCommand}</c>.</summary>
    Command,
}
