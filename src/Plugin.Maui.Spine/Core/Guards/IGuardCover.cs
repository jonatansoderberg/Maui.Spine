namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Implemented by a guard that can cover a guarded tab while it asks. Android and Windows switch tabs
/// before Spine hears of the tap, so the tab is shown under this cover until the guard passes, and stays
/// covered after a refusal. A guard without a cover sends the user back to the tab they came from.
/// </summary>
public interface IGuardCover
{
    /// <summary>
    /// Returns the view shown in place of the tab's page, or <see langword="null"/> for none.
    /// <paramref name="retry"/> runs the guard again; call it from the cover's own button.
    /// </summary>
    View? CreateCover(GuardAttribute attribute, GuardContext context, Func<Task> retry);
}
