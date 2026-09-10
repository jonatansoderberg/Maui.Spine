using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class PushSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingTransport(PushPlatform platform, PushStatus status = PushStatus.Sent) : IPushBroadcastTransport
    {
        public PushPlatform Platform => platform;

        public List<(string Channel, PushEnvelope Envelope)> Broadcasts { get; } = [];

        public Task<PushDelivery> BroadcastAsync(string channel, PushEnvelope message, CancellationToken cancellationToken = default)
        {
            Broadcasts.Add((channel, message));
            return Task.FromResult(new PushDelivery(channel, platform, status));
        }

        public List<PushInstallation> Reached { get; } = [];

        public PushEnvelope? Envelope { get; private set; }

        public Task<IReadOnlyList<PushDelivery>> SendAsync(
            IReadOnlyList<PushInstallation> installations, PushEnvelope message, CancellationToken cancellationToken = default)
        {
            Reached.AddRange(installations);
            Envelope = message;

            IReadOnlyList<PushDelivery> deliveries = installations
                .Select(i => new PushDelivery(i.Id, platform, status, status == PushStatus.Sent ? null : "token dead"))
                .ToList();

            return Task.FromResult(deliveries);
        }
    }

    private static PushInstallation Installation(string id, PushPlatform platform, params string[] tags) => new()
    {
        Id = id, Platform = platform, Handle = $"handle-{id}", Tags = tags, UpdatedAt = Now,
    };

    /// <summary>An installation that is running the activity, so Apple has a token to address.</summary>
    private static PushInstallation Running(PushInstallation installation, string kind) => installation with
    {
        LiveActivities = new LiveActivityTokens
        {
            PushToStart = $"start-{installation.Id}",
            Activities = new Dictionary<string, string> { [kind] = $"activity-{installation.Id}" },
        },
    };

    private static SpinePushOptions Options() => new SpinePushOptions()
        .Apple(a =>
        {
            a.TeamId = "TEAM123456";
            a.KeyId = "KEY1234567";
            a.PrivateKey = "-----BEGIN PRIVATE KEY-----\nx\n-----END PRIVATE KEY-----";
            a.BundleId = "com.companyname.orientera";
        })
        .UseInMemoryStore();

    private static readonly PushNotification Notification = new() { Title = "T", Body = "B" };

    private static (PushSender Sender, InMemoryPushInstallationStore Store, RecordingTransport Apple, RecordingTransport Android)
        NewSender(PushStatus appleStatus = PushStatus.Sent)
    {
        var store = new InMemoryPushInstallationStore(new FakeTimeProvider(Now));
        var apple = new RecordingTransport(PushPlatform.Apple, appleStatus);
        var android = new RecordingTransport(PushPlatform.Android);
        var sender = new PushSender(store, [apple, android], Options(), new FakeTimeProvider(Now));
        return (sender, store, apple, android);
    }

    [Fact]
    public async Task One_call_reaches_both_platforms_with_the_payload_each_expects()
    {
        var (sender, store, apple, android) = NewSender();
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple, "t"));
        await store.UpsertAsync(Installation("droid", PushPlatform.Android, "t"));

        var result = await sender.SendAsync(PushTarget.Tags("t"), Notification);

        Assert.Equal(2, result.Sent);
        Assert.Equal("alert", apple.Envelope!.ApnsPushType);
        Assert.Null(android.Envelope!.ApnsPushType);
        Assert.Equal(["ios"], apple.Reached.Select(i => i.Id));
        Assert.Equal(["droid"], android.Reached.Select(i => i.Id));
    }

    [Fact]
    public async Task A_target_that_matches_nothing_is_an_empty_result()
    {
        var (sender, _, apple, _) = NewSender();

        var result = await sender.SendAsync(PushTarget.Tags("nobody"), Notification);

        Assert.Empty(result.Deliveries);
        Assert.Empty(apple.Reached);
    }

    [Fact]
    public async Task An_installation_target_reaches_exactly_that_one()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Installation("a", PushPlatform.Apple, "t"));
        await store.UpsertAsync(Installation("b", PushPlatform.Apple, "t"));

        await sender.SendAsync(PushTarget.Installation("b"), Notification);

        Assert.Equal(["b"], apple.Reached.Select(i => i.Id));
    }

    [Fact]
    public async Task A_user_target_is_the_user_tag()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Installation("mine", PushPlatform.Apple, "user:121330"));
        await store.UpsertAsync(Installation("theirs", PushPlatform.Apple, "user:999"));

        await sender.SendAsync(PushTarget.User("121330"), Notification);

        Assert.Equal(["mine"], apple.Reached.Select(i => i.Id));
    }

    [Fact]
    public async Task All_reaches_every_registration()
    {
        var (sender, store, apple, android) = NewSender();
        await store.UpsertAsync(Installation("a", PushPlatform.Apple));
        await store.UpsertAsync(Installation("b", PushPlatform.Android, "t"));

        var result = await sender.SendAsync(PushTarget.All, Notification);

        Assert.Equal(2, result.Sent);
    }

    [Fact]
    public async Task A_dead_token_is_reported_invalid_and_the_registration_is_removed()
    {
        var (sender, store, _, _) = NewSender(PushStatus.Invalid);
        await store.UpsertAsync(Installation("dead", PushPlatform.Apple, "t"));

        var result = await sender.SendAsync(PushTarget.Tags("t"), Notification);

        Assert.Equal(1, result.Invalid);
        Assert.Equal("token dead", result.Deliveries[0].Reason);
        Assert.Null(await store.GetAsync("dead"));
    }

    [Fact]
    public async Task A_platform_without_a_transport_is_skipped_rather_than_failed()
    {
        var store = new InMemoryPushInstallationStore(new FakeTimeProvider(Now));
        var apple = new RecordingTransport(PushPlatform.Apple);
        var sender = new PushSender(store, [apple], Options(), new FakeTimeProvider(Now));

        await store.UpsertAsync(Installation("ios", PushPlatform.Apple, "t"));
        await store.UpsertAsync(Installation("droid", PushPlatform.Android, "t"));

        var result = await sender.SendAsync(PushTarget.Tags("t"), Notification);

        Assert.Single(result.Deliveries);
        Assert.Equal("ios", result.Deliveries[0].InstallationId);
    }

    [Fact]
    public async Task A_live_activity_update_goes_out_on_both_platforms()
    {
        var (sender, store, apple, android) = NewSender();
        await store.UpsertAsync(Running(Installation("ios", PushPlatform.Apple, "user:1"), "din-start:59691"));
        await store.UpsertAsync(Installation("droid", PushPlatform.Android, "user:1"));

        var layout = new LiveActivityLayout { LockScreen = W.Text("Ute på banan") };
        await sender.UpdateLiveActivityAsync(PushTarget.User("1"), "din-start:59691", layout);

        Assert.Equal("liveactivity", apple.Envelope!.ApnsPushType);
        Assert.Contains("din-start:59691", apple.Envelope.Json);
        Assert.Contains("din-start:59691", android.Envelope!.Json);
    }

    [Fact]
    public async Task A_live_activity_update_is_addressed_to_the_activitys_own_token()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Running(Installation("ios", PushPlatform.Apple, "user:1"), "din-start:59691"));

        await sender.UpdateLiveActivityAsync(
            PushTarget.User("1"), "din-start:59691", new LiveActivityLayout { LockScreen = W.Text("x") });

        Assert.Equal("activity-ios", Assert.Single(apple.Reached).Handle);
    }

    [Fact]
    public async Task A_live_activity_start_is_addressed_to_the_push_to_start_token()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Running(Installation("ios", PushPlatform.Apple, "user:1"), "din-start:59691"));

        await sender.StartLiveActivityAsync(
            PushTarget.User("1"), "din-start:59691",
            new LiveActivityLayout { LockScreen = W.Text("x") }, new PushAlert { Title = "t" });

        Assert.Equal("start-ios", Assert.Single(apple.Reached).Handle);
    }

    /// <summary>
    /// The device token is not accepted on the liveactivity topic, so sending it there earns
    /// DeviceTokenNotForTopic. Saying so beats letting Apple say it in a way that reads like a dead
    /// registration.
    /// </summary>
    [Fact]
    public async Task A_live_activity_for_an_installation_without_a_token_is_reported_not_sent()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple, "user:1"));

        var result = await sender.UpdateLiveActivityAsync(
            PushTarget.User("1"), "din-start:59691", new LiveActivityLayout { LockScreen = W.Text("x") });

        Assert.Null(apple.Envelope);
        var delivery = Assert.Single(result.Deliveries);
        Assert.Equal(PushStatus.Failed, delivery.Status);
        Assert.Equal("NoLiveActivityToken", delivery.Reason);
    }

    [Fact]
    public async Task A_widget_refresh_is_silent_on_apple()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple, "t"));

        await sender.RefreshWidgetsAsync(PushTarget.Tags("t"), "next-start");

        Assert.Equal("background", apple.Envelope!.ApnsPushType);
    }

    [Fact]
    public async Task A_silent_push_carries_the_callers_data()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple, "t"));

        await sender.SendSilentAsync(PushTarget.Tags("t"), new Dictionary<string, string> { ["sync"] = "results" });

        Assert.Contains("\"sync\":\"results\"", apple.Envelope!.Json);
    }

    [Fact]
    public void The_result_summarises_what_happened()
    {
        var result = new PushResult
        {
            Deliveries =
            [
                new("a", PushPlatform.Apple, PushStatus.Sent),
                new("b", PushPlatform.Apple, PushStatus.Invalid),
                new("c", PushPlatform.Android, PushStatus.Throttled),
                new("d", PushPlatform.Android, PushStatus.Failed, "boom"),
            ],
        };

        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Invalid);
        Assert.Equal(1, result.Throttled);
        Assert.Equal(1, result.Failed);
        Assert.False(result.AllSent);
    }

    [Fact]
    public void Results_from_several_platforms_merge()
    {
        var merged = PushResult.Merge([
            new PushResult { Deliveries = [new("a", PushPlatform.Apple, PushStatus.Sent)] },
            new PushResult { Deliveries = [new("b", PushPlatform.Android, PushStatus.Sent)] },
        ]);

        Assert.Equal(2, merged.Sent);
        Assert.True(merged.AllSent);
    }

    [Fact]
    public async Task A_widget_token_gets_the_widgets_push_addressed_to_that_token()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple) with { WidgetToken = "widget-ios" });

        var result = await sender.RefreshWidgetsAsync(PushTarget.Installation("ios"), "sample");

        Assert.Equal(1, result.Sent);
        Assert.Equal("widget-ios", Assert.Single(apple.Reached).Handle);
        Assert.Equal("widgets", apple.Envelope!.ApnsPushType);
        Assert.Equal("com.companyname.orientera.push-type.widgets", apple.Envelope.ApnsTopic);
    }

    [Fact]
    public async Task Without_a_widget_token_the_app_is_woken_by_a_silent_push_as_before()
    {
        var (sender, store, apple, _) = NewSender();
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple));

        await sender.RefreshWidgetsAsync(PushTarget.Installation("ios"), "sample");

        Assert.Equal("handle-ios", Assert.Single(apple.Reached).Handle);
        Assert.Equal("background", apple.Envelope!.ApnsPushType);
    }

    [Fact]
    public async Task A_dead_widget_token_does_not_remove_the_installation()
    {
        var (sender, store, _, _) = NewSender(PushStatus.Invalid);
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple) with { WidgetToken = "widget-ios" });

        var result = await sender.RefreshWidgetsAsync(PushTarget.Installation("ios"));

        Assert.Equal(1, result.Invalid);
        Assert.NotNull(await store.GetAsync("ios"));
    }

    [Fact]
    public async Task A_dead_device_token_on_the_silent_road_still_removes_the_installation()
    {
        var (sender, store, _, _) = NewSender(PushStatus.Invalid);
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple));

        await sender.RefreshWidgetsAsync(PushTarget.Installation("ios"));

        Assert.Null(await store.GetAsync("ios"));
    }

    [Fact]
    public async Task A_broadcast_goes_to_the_channel_on_both_platforms_without_asking_the_register()
    {
        var (sender, store, apple, android) = NewSender();
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple));

        var result = await sender.BroadcastLiveActivityAsync(
            "Y2hhbm5lbA==", "tavling:1", new LiveActivityLayout { LockScreen = W.Text("Ledare 12:04") },
            environment: ApnsEnvironment.Sandbox);

        Assert.Empty(apple.Reached);
        Assert.Equal(["Y2hhbm5lbA==", "Y2hhbm5lbA=="], result.Deliveries.Select(d => d.InstallationId));

        var (channel, envelope) = Assert.Single(apple.Broadcasts);
        Assert.Equal("Y2hhbm5lbA==", channel);
        Assert.Equal("liveactivity", envelope.ApnsPushType);
        Assert.Equal(ApnsEnvironment.Sandbox, envelope.ApnsEnvironment);

        var data = FcmMessageReader.Read(Assert.Single(android.Broadcasts).Envelope.Json).Data;
        Assert.Equal("Y2hhbm5lbA==", data[PushKeys.ActivityChannel]);
    }

    [Fact]
    public async Task A_broadcast_cannot_start_an_activity()
    {
        var (sender, _, _, _) = NewSender();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sender.BroadcastLiveActivityAsync("c", "k", new LiveActivityLayout(), LiveActivityEvent.Start));
    }
}
