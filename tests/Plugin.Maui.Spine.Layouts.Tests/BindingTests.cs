using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>A Live Activity bound to a view model: one update per burst, from the model as it is then.</summary>
public class BindingTests
{
    readonly FakeTimeProvider _time = new();
    readonly List<LiveActivityLayout> _sent = [];

    LiveActivity Activity(Func<LiveActivityLayout, Task>? update = null) =>
        new("a1", "score", (_, layout, _) => update?.Invoke(layout) ?? Record(layout), _ => Task.CompletedTask, (_, _) => Task.FromResult<string?>(null));

    Task Record(LiveActivityLayout layout)
    {
        _sent.Add(layout);
        return Task.CompletedTask;
    }

    LiveActivityBindingOptions Options => new() { Time = _time };

    static string Minimal(LiveActivityLayout layout) => ((TextNode)layout.Minimal!).Text;

    [Fact]
    public void A_burst_of_changes_is_one_update_with_the_last_values()
    {
        var model = new ScoreModel();
        using var binding = ScoreLayout.Bind(Activity(), model, options: Options);

        model.Game.HomeGoals = 1;
        model.Game.HomeGoals = 2;
        model.Game.AwayGoals = 1;
        _time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(_sent);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("2–1", Minimal(Assert.Single(_sent)));
        Assert.Equal(1, binding.Updates);
    }

    [Fact]
    public void A_change_the_layout_does_not_show_sends_nothing()
    {
        var model = new ScoreModel();
        using var binding = ScoreLayout.Bind(Activity(), model, options: Options);
        model.Home = "Owls";
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(_sent);

        model.Home = "Owls";
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(_sent);
    }

    [Fact]
    public void Lists_and_replaced_objects_are_followed()
    {
        var model = new ScoreModel();
        using var binding = ScoreLayout.Bind(Activity(), model, options: Options);

        model.Goals.Add("1–0 Ek 4'");
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(_sent);

        var old = model.Game;
        model.Game = new Game { HomeGoals = 3 };
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("3–0", Minimal(_sent[^1]));

        old.AwayGoals = 5;
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, _sent.Count);

        model.Game.AwayGoals = 2;
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("3–2", Minimal(_sent[^1]));
    }

    [Fact]
    public async Task Flush_sends_at_once_and_dispose_stops()
    {
        var model = new ScoreModel();
        var binding = ScoreLayout.Bind(Activity(), model, options: Options);
        model.Game.HomeGoals = 1;
        await binding.FlushAsync();
        Assert.Single(_sent);

        model.Game.HomeGoals = 2;
        binding.Dispose();
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(_sent);
    }

    [Fact]
    public void An_ended_activity_stops_the_binding()
    {
        var model = new ScoreModel();
        var activity = Activity();
        using var binding = ScoreLayout.Bind(activity, model, options: Options);
        activity.IsEnded = true;
        model.Game.HomeGoals = 1;
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_failed_update_is_reported_and_the_next_change_goes_out()
    {
        var model = new ScoreModel();
        var fail = true;
        using var binding = ScoreLayout.Bind(Activity(layout => fail ? throw new InvalidOperationException("budget") : Record(layout)), model, options: Options);
        Exception? failed = null;
        binding.Failed += (_, e) => failed = e;

        model.Game.HomeGoals = 1;
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("budget", failed?.Message);

        fail = false;
        model.Game.HomeGoals = 2;
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("2–0", Minimal(Assert.Single(_sent)));
    }

    [Fact]
    public async Task Start_draws_the_model_and_binds()
    {
        var model = new ScoreModel { Home = "Lynx" };
        var service = new Activities(this);
        var binding = await ScoreLayout.StartAsync(service, "score", model, staleAt: m => DateTimeOffset.UnixEpoch, options: Options);

        Assert.NotNull(binding);
        Assert.Equal("Lynx 0–0 Bears", ((TextNode)((VStackNode)service.Started!.LockScreen!).Children[0]).Text);
        model.Home = "Lynx";
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(_sent);
    }

    [Fact]
    public void Only_a_model_that_announces_its_changes_gets_Bind()
    {
        Assert.NotNull(typeof(ScoreLayout).GetMethod("Bind"));
        Assert.Null(typeof(GlucoseLayout).GetMethod("Bind"));
    }

    sealed class Activities(BindingTests tests) : ILiveActivityService
    {
        public LiveActivityLayout? Started { get; private set; }
        public bool IsSupported => true;
        public bool AreActivitiesEnabled => true;
        public IReadOnlyList<LiveActivity> Active => [];
        public event Action? ActivitiesChanged { add { } remove { } }
        public event Action<LiveActivity>? ActivityEnded { add { } remove { } }

        public Task<LiveActivity?> StartAsync(string kind, LiveActivityLayout layout, DateTimeOffset? staleAt = null, string? channel = null)
        {
            Started = layout;
            return Task.FromResult<LiveActivity?>(tests.Activity());
        }

        public Task EndAllAsync() => Task.CompletedTask;
        public Task<string?> GetPushToStartTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}
