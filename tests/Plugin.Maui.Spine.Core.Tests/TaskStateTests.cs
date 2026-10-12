using Plugin.Maui.Spine.Core;
using Xunit;

namespace Plugin.Maui.Spine.Core.Tests;

/// <summary>The status, result and error a <see cref="TaskState{T}"/> reports through a load.</summary>
public class TaskStateTests
{
    /// <summary>A load that finishes when the test says so, through <see cref="Pending"/>.</summary>
    private sealed class Gate<T>
    {
        public TaskCompletionSource<T> Pending { get; private set; } = null!;

        public Task<T> Load(CancellationToken ct)
        {
            var tcs = Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }
    }

    [Fact]
    public void Starts_not_started_and_loading()
    {
        var state = new TaskState<int>(_ => Task.FromResult(1));

        Assert.Equal(TaskStateStatus.NotStarted, state.Status);
        Assert.True(state.IsLoading);
        Assert.False(state.HasResult);
        Assert.True(state.NeedsLoad);
    }

    [Fact]
    public async Task A_failure_that_means_nothing_to_show_is_empty()
    {
        var state = new TaskState<string>(_ => throw new Plugin.Maui.Spine.AI.AIFeatureOffException("Story", Plugin.Maui.Spine.AI.AIOffReason.Budget));

        await state.LoadAsync();

        Assert.Equal(TaskStateStatus.Empty, state.Status);
        Assert.IsType<Plugin.Maui.Spine.AI.AIFeatureOffException>(state.Error);
        Assert.False(state.HasResult);
    }

    [Fact]
    public async Task A_failure_that_means_nothing_to_show_keeps_a_result_on_screen()
    {
        var fail = false;
        var state = new TaskState<string>(_ => fail
            ? throw new Plugin.Maui.Spine.AI.AIFeatureOffException("Story", Plugin.Maui.Spine.AI.AIOffReason.Budget)
            : Task.FromResult("story"));

        await state.LoadAsync();
        fail = true;
        await state.LoadAsync();

        Assert.Equal(TaskStateStatus.Success, state.Status);
        Assert.Equal("story", state.Result);
        Assert.True(state.HasError);
    }

    [Fact]
    public async Task Success_sets_the_result()
    {
        var state = new TaskState<string>(_ => Task.FromResult("done"));

        await state.LoadAsync();

        Assert.Equal(TaskStateStatus.Success, state.Status);
        Assert.Equal("done", state.Result);
        Assert.Equal("done", state.Value);
        Assert.True(state.HasResult);
        Assert.False(state.IsLoading);
        Assert.False(state.NeedsLoad);
    }

    [Fact]
    public async Task IsEmpty_gives_empty()
    {
        var state = new TaskState<int[]>(_ => Task.FromResult(Array.Empty<int>()), isEmpty: a => a.Length == 0);

        await state.LoadAsync();

        Assert.Equal(TaskStateStatus.Empty, state.Status);
        Assert.True(state.HasResult);
    }

    [Fact]
    public async Task Loading_then_refreshing()
    {
        var gate = new Gate<int>();
        var state = new TaskState<int>(gate.Load);

        var first = state.LoadAsync();
        Assert.Equal(TaskStateStatus.Loading, state.Status);
        Assert.True(state.IsLoading);
        gate.Pending.SetResult(1);
        await first;

        var second = state.LoadAsync();
        Assert.Equal(TaskStateStatus.Refreshing, state.Status);
        Assert.True(state.IsRefreshing);
        Assert.False(state.IsLoading);
        Assert.Equal(1, state.Value);
        gate.Pending.SetResult(2);
        await second;

        Assert.Equal(TaskStateStatus.Success, state.Status);
        Assert.Equal(2, state.Result);
    }

    [Fact]
    public async Task Failure_without_a_result_is_an_error()
    {
        var state = new TaskState<int>(_ => throw new HttpRequestException("503 from /competitions"));

        await state.LoadAsync();

        Assert.Equal(TaskStateStatus.Error, state.Status);
        Assert.Equal("503 from /competitions", state.ErrorMessage);
        Assert.True(state.HasError);
        Assert.True(state.NeedsLoad);
    }

    [Fact]
    public async Task Failed_refresh_keeps_the_result()
    {
        var fail = false;
        var state = new TaskState<int>(_ => fail ? throw new InvalidOperationException("offline") : Task.FromResult(7));

        await state.LoadAsync();
        fail = true;
        await state.LoadAsync();

        Assert.Equal(TaskStateStatus.Success, state.Status);
        Assert.Equal(7, state.Result);
        Assert.Equal("offline", state.ErrorMessage);
        Assert.False(state.NeedsLoad);

        fail = false;
        await state.LoadAsync();
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task Retry_after_an_error_loads_from_scratch()
    {
        var fail = true;
        var state = new TaskState<int>(_ => fail ? throw new InvalidOperationException() : Task.FromResult(3));

        await state.LoadAsync();
        fail = false;
        await state.LoadCommand.ExecuteAsync(null);

        Assert.Equal(TaskStateStatus.Success, state.Status);
        Assert.Null(state.Error);
    }

    [Fact]
    public async Task The_last_load_wins()
    {
        var gate = new Gate<int>();
        var state = new TaskState<int>(gate.Load);

        var first = state.LoadAsync();
        var firstLoad = gate.Pending;
        var second = state.LoadAsync();
        var secondLoad = gate.Pending;

        secondLoad.SetResult(2);
        await second;
        firstLoad.TrySetResult(1);
        await first;

        Assert.Equal(2, state.Result);
        Assert.Equal(TaskStateStatus.Success, state.Status);
    }

    [Fact]
    public async Task Cancelling_the_first_load_goes_back_to_not_started()
    {
        var gate = new Gate<int>();
        var state = new TaskState<int>(gate.Load);
        using var cts = new CancellationTokenSource();

        var load = state.LoadAsync(cts.Token);
        cts.Cancel();
        await load;

        Assert.Equal(TaskStateStatus.NotStarted, state.Status);
        Assert.Null(state.Error);
        Assert.True(state.NeedsLoad);
    }

    [Fact]
    public async Task Cancelling_a_refresh_goes_back_to_the_result()
    {
        var gate = new Gate<int[]>();
        var state = new TaskState<int[]>(gate.Load, isEmpty: a => a.Length == 0);

        var first = state.LoadAsync();
        gate.Pending.SetResult([]);
        await first;

        using var cts = new CancellationTokenSource();
        var refresh = state.LoadAsync(cts.Token);
        cts.Cancel();
        await refresh;

        Assert.Equal(TaskStateStatus.Empty, state.Status);
        Assert.False(state.NeedsLoad);
    }

    [Fact]
    public async Task A_load_on_a_cancelled_lifetime_does_nothing()
    {
        var calls = 0;
        var state = new TaskState<int>(_ => Task.FromResult(++calls)) { Lifetime = () => new CancellationToken(canceled: true) };

        await state.LoadAsync();

        Assert.Equal(0, calls);
        Assert.Equal(TaskStateStatus.NotStarted, state.Status);
    }

    [Fact]
    public async Task Lifetime_is_the_default_token()
    {
        var gate = new Gate<int>();
        using var page = new CancellationTokenSource();
        var state = new TaskState<int>(gate.Load) { Lifetime = () => page.Token };

        var load = state.LoadAsync();
        page.Cancel();
        await load;

        Assert.Equal(TaskStateStatus.NotStarted, state.Status);
    }

    [Fact]
    public async Task Placeholder_until_the_first_result()
    {
        var made = 0;
        var gate = new Gate<string[]>();
        var state = new TaskState<string[]>(gate.Load, placeholder: () => { made++; return ["", "", ""]; });

        Assert.True(state.HasPlaceholder);
        Assert.Equal(3, state.Value!.Length);

        var load = state.LoadAsync();
        Assert.Equal(3, state.Value!.Length);
        gate.Pending.SetResult(["Ada"]);
        await load;

        Assert.Equal(["Ada"], state.Value);
        Assert.Equal(1, made);
    }

    [Fact]
    public async Task Reset_forgets_the_result_and_loads_from_scratch()
    {
        var gate = new Gate<string[]>();
        var state = new TaskState<string[]>(gate.Load, placeholder: () => [""]);

        var first = state.LoadAsync();
        gate.Pending.SetResult(["Ada"]);
        await first;

        state.Reset();
        Assert.Equal(TaskStateStatus.NotStarted, state.Status);
        Assert.False(state.HasResult);
        Assert.Equal([""], state.Value);

        var second = state.LoadAsync();
        Assert.Equal(TaskStateStatus.Loading, state.Status);
        gate.Pending.SetResult(["Grace"]);
        await second;
        Assert.Equal(["Grace"], state.Value);
    }

    [Fact]
    public async Task Reset_drops_a_running_load()
    {
        var gate = new Gate<int>();
        var state = new TaskState<int>(gate.Load);

        var load = state.LoadAsync();
        var running = gate.Pending;
        state.Reset();
        running.TrySetResult(1);
        await load;

        Assert.Equal(TaskStateStatus.NotStarted, state.Status);
        Assert.False(state.HasResult);
    }

    [Fact]
    public void No_placeholder_means_no_value()
    {
        var state = new TaskState<string>(_ => Task.FromResult("x"));

        Assert.False(state.HasPlaceholder);
        Assert.Null(state.Value);
    }

    [Fact]
    public async Task Notifies_the_properties_a_view_binds()
    {
        var state = new TaskState<int>(_ => Task.FromResult(5));
        var changed = new List<string?>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await state.LoadAsync();

        Assert.Contains(nameof(TaskState.Status), changed);
        Assert.Contains(nameof(TaskState.IsLoading), changed);
        Assert.Contains(nameof(TaskState.IsRefreshing), changed);
        Assert.Contains(nameof(TaskState.HasResult), changed);
        Assert.Contains(nameof(TaskState<int>.Value), changed);
        Assert.Contains(nameof(TaskState<int>.Result), changed);
    }
}
