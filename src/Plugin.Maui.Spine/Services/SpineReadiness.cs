namespace Plugin.Maui.Spine.Services;

/// <summary>
/// When the app can show something of its own: the window has been active once, and the first root page
/// is in place. A way in from outside on a cold start (a search result, a guard's prompt) waits for it.
/// </summary>
internal sealed class SpineReadiness
{
    private readonly TaskCompletionSource _activated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _rootSet = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes on the window's first activation.</summary>
    public Task WhenActivated => _activated.Task;

    /// <summary>Completes when the first root page is in place, or failed to be.</summary>
    public Task WhenRootSet => _rootSet.Task;

    public void MarkActivated() => _activated.TrySetResult();

    public void MarkRootSet() => _rootSet.TrySetResult();
}
