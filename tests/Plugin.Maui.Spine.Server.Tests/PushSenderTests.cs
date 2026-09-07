using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class PushSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingTransport(PushPlatform platform, PushStatus status = PushStatus.Sent) : IPushTransport
    {
        public PushPlatform Platform => platform;

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
        await store.UpsertAsync(Installation("ios", PushPlatform.Apple, "user:1"));
        await store.UpsertAsync(Installation("droid", PushPlatform.Android, "user:1"));

        var layout = new LiveActivityLayout { LockScreen = W.Text("Ute på banan") };
        await sender.UpdateLiveActivityAsync(PushTarget.User("1"), "din-start:59691", layout);

        Assert.Equal("liveactivity", apple.Envelope!.ApnsPushType);
        Assert.Contains("din-start:59691", apple.Envelope.Json);
        Assert.Contains("din-start:59691", android.Envelope!.Json);
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
}
