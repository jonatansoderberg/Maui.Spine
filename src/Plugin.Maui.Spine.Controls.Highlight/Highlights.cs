namespace Plugin.Maui.Spine.Controls;

internal sealed class Highlights : IHighlights
{
    public Task<TourResult> TourAsync(string key, params IEnumerable<TourStep> steps)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Tours.RunAsync(key, [.. steps]);
    }

    public async Task PlayAsync(View view, HighlightKind kind, HighlightRepeat repeat = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (kind is HighlightKind.None
            || (Highlight.GetKey(view) is { } key && HighlightKeys.HasSeen(key)))
        {
            return;
        }

        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnEnded(object? sender, HighlightEndedEventArgs e)
        {
            if (ReferenceEquals(e.View, view))
                ended.TrySetResult();
        }

        Highlight.Ended += OnEnded;
        await using var registration = cancellationToken.Register(() => ended.TrySetCanceled(cancellationToken));
        try
        {
            Highlight.SetRepeat(view, repeat);
            Highlight.SetKind(view, kind);
            await ended.Task;
        }
        finally
        {
            Highlight.Ended -= OnEnded;
            view.Dispatcher.Dispatch(() =>
            {
                if (Highlight.GetKind(view) == kind)
                    view.ClearValue(Highlight.KindProperty);
                view.ClearValue(Highlight.RepeatProperty);
            });
        }
    }

    public IEdgeGlow AttachEdge(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new EdgeGlow(window);
    }

    public bool HasSeen(string key) => HighlightKeys.HasSeen(key);

    public void MarkSeen(string key) => HighlightKeys.MarkSeen(key);

    public void Reset(string? key = null) => HighlightKeys.Reset(key);
}
