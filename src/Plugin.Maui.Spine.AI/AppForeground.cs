using System.Runtime.CompilerServices;

namespace Plugin.Maui.Spine.AI;

/// <summary>Whether the app has a window in front, from the windows' <c>Stopped</c> and <c>Resumed</c>.</summary>
/// <remarks>
/// A window is followed from the first time a watch asks, which is while a page shows, so it starts as active. On Mac
/// Catalyst a minimised window raises no <c>Stopped</c> (MAUI follows the application there), so polling goes on.
/// </remarks>
internal static class AppForeground
{
    private static readonly ConditionalWeakTable<Window, State> Followed = new();
    private static TaskCompletionSource _active = Completed();

    public static Task WhenActiveAsync(CancellationToken cancellationToken)
    {
        if (Application.Current is not { } app)
            return Task.CompletedTask;

        lock (Followed)
        {
            foreach (var window in app.Windows)
                Follow(window);

            return IsActive(app) ? Task.CompletedTask : _active.Task.WaitAsync(cancellationToken);
        }
    }

    private static void Follow(Window window)
    {
        if (Followed.TryGetValue(window, out _))
            return;

        var state = new State();
        Followed.Add(window, state);
        window.Stopped += (_, _) => Changed(state, stopped: true);
        window.Resumed += (_, _) => Changed(state, stopped: false);
    }

    private static void Changed(State state, bool stopped)
    {
        lock (Followed)
        {
            state.Stopped = stopped;
            if (Application.Current is not { } app || IsActive(app))
                _active.TrySetResult();
            else if (_active.Task.IsCompleted)
                _active = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private static bool IsActive(Application app) =>
        app.Windows.Count == 0 || app.Windows.Any(w => !Followed.TryGetValue(w, out var state) || !state.Stopped);

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private sealed class State
    {
        public bool Stopped;
    }
}
