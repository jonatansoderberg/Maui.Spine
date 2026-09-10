using System.Text;
using System.Text.Json;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class PushPayloadsTests
{
    private const string Bundle = "com.companyname.orientera";
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly PushNotification Notification = new()
    {
        Title = "Resultat klara",
        Body = "Gävle OK Medeldistans",
        Route = "competition/59691",
        Channel = "results",
        CollapseId = "results-59691",
        Data = new Dictionary<string, string> { ["competition"] = "59691" },
    };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void An_alert_becomes_an_aps_alert_on_apns()
    {
        var envelope = PushPayloads.Apns(Notification, Bundle, Now);
        var aps = Parse(envelope.Json).GetProperty("aps");

        Assert.Equal("alert", envelope.ApnsPushType);
        Assert.Equal(Bundle, envelope.ApnsTopic);
        Assert.Equal("Resultat klara", aps.GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("Gävle OK Medeldistans", aps.GetProperty("alert").GetProperty("body").GetString());
        Assert.Equal("results", aps.GetProperty("thread-id").GetString());
        Assert.Equal("active", aps.GetProperty("interruption-level").GetString());
    }

    [Fact]
    public void The_same_alert_becomes_data_only_on_fcm()
    {
        var envelope = PushPayloads.Fcm(Notification);
        var message = Parse(envelope.Json);

        Assert.False(message.TryGetProperty("notification", out _));

        var data = message.GetProperty("data");
        Assert.Equal("alert", data.GetProperty(PushKeys.Kind).GetString());
        Assert.Equal("Resultat klara", data.GetProperty(PushKeys.Title).GetString());
        Assert.Equal("Gävle OK Medeldistans", data.GetProperty(PushKeys.Body).GetString());
        Assert.Equal("high", message.GetProperty("android").GetProperty("priority").GetString());
    }

    [Fact]
    public void Both_platforms_carry_the_same_spine_keys()
    {
        var apns = Parse(PushPayloads.Apns(Notification, Bundle, Now).Json);
        var fcm = Parse(PushPayloads.Fcm(Notification).Json).GetProperty("data");

        foreach (var key in new[] { PushKeys.Kind, PushKeys.Route, PushKeys.Channel, PushKeys.Collapse, "competition" })
        {
            Assert.Equal(apns.GetProperty(key).GetString(), fcm.GetProperty(key).GetString());
        }
    }

    [Theory]
    [InlineData(PushPriority.High, 10)]
    [InlineData(PushPriority.Normal, 5)]
    public void Priority_maps_to_apns_numbering(PushPriority priority, int expected)
    {
        var envelope = PushPayloads.Apns(Notification with { Priority = priority }, Bundle, Now);
        Assert.Equal(expected, envelope.Priority);
    }

    [Theory]
    [InlineData(PushInterruption.Passive, "passive")]
    [InlineData(PushInterruption.Active, "active")]
    [InlineData(PushInterruption.TimeSensitive, "time-sensitive")]
    public void Interruption_maps_to_the_apns_level(PushInterruption interruption, string expected)
    {
        var json = PushPayloads.Apns(Notification with { Interruption = interruption }, Bundle, Now).Json;
        Assert.Equal(expected, Parse(json).GetProperty("aps").GetProperty("interruption-level").GetString());
    }

    [Fact]
    public void Time_to_live_becomes_an_absolute_expiration_on_apns()
    {
        var envelope = PushPayloads.Apns(Notification with { TimeToLive = TimeSpan.FromMinutes(30) }, Bundle, Now);
        Assert.Equal(Now.AddMinutes(30), envelope.Expiration);
    }

    [Fact]
    public void A_silent_push_is_content_available_at_priority_five()
    {
        var envelope = PushPayloads.ApnsSilent(new Dictionary<string, string> { ["sync"] = "results" }, Bundle);
        var aps = Parse(envelope.Json).GetProperty("aps");

        Assert.Equal("background", envelope.ApnsPushType);
        Assert.Equal(5, envelope.Priority);
        Assert.Equal(1, aps.GetProperty("content-available").GetInt32());
        Assert.False(aps.TryGetProperty("alert", out _));
        Assert.Equal("silent", Parse(envelope.Json).GetProperty(PushKeys.Kind).GetString());
    }

    [Fact]
    public void A_silent_fcm_message_is_normal_priority()
    {
        var envelope = PushPayloads.FcmSilent(new Dictionary<string, string>());
        Assert.Equal("normal", Parse(envelope.Json).GetProperty("android").GetProperty("priority").GetString());
    }

    [Fact]
    public void A_live_activity_update_carries_the_layout_as_content_state()
    {
        var layout = new LiveActivityLayout { LockScreen = W.Text("Startar om 30 min") };
        var options = new LiveActivityOptions { StaleAt = Now.AddMinutes(10) };

        var envelope = PushPayloads.ApnsLiveActivity(
            "din-start:59691", layout, LiveActivityEvent.Update, alert: null, options, Bundle, Now);

        var aps = Parse(envelope.Json).GetProperty("aps");

        Assert.Equal("liveactivity", envelope.ApnsPushType);
        Assert.Equal($"{Bundle}.push-type.liveactivity", envelope.ApnsTopic);
        Assert.Equal(5, envelope.Priority);
        Assert.Equal("update", aps.GetProperty("event").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(), aps.GetProperty("timestamp").GetInt64());
        Assert.Equal(Now.AddMinutes(10).ToUnixTimeSeconds(), aps.GetProperty("stale-date").GetInt64());
        // content-state carries the layout as a string under "json", the shape ActivityKit decodes
        // into SpineActivityAttributes.ContentState. Inlining the layout object here is what made
        // server-driven activities render as a placeholder ring.
        var state = aps.GetProperty("content-state");
        Assert.Equal(JsonValueKind.String, state.GetProperty("json").ValueKind);
        Assert.Equal("Startar om 30 min",
            Parse(state.GetProperty("json").GetString()!).GetProperty("lockScreen").GetProperty("text").GetString());
    }

    [Fact]
    public void Starting_by_push_carries_the_alert_text()
    {
        var envelope = PushPayloads.ApnsLiveActivity(
            "din-start:59691", new LiveActivityLayout(), LiveActivityEvent.Start,
            new PushAlert("Din start", "Startar om 30 min"), options: null, Bundle, Now);

        var aps = Parse(envelope.Json).GetProperty("aps");

        Assert.Equal("start", aps.GetProperty("event").GetString());
        Assert.Equal("Din start", aps.GetProperty("alert").GetProperty("title").GetString());
    }

    [Fact]
    public void Ending_an_activity_can_say_when_it_should_disappear()
    {
        var envelope = PushPayloads.ApnsLiveActivity(
            "k", new LiveActivityLayout(), LiveActivityEvent.End, alert: null,
            new LiveActivityOptions { DismissAt = Now.AddMinutes(5) }, Bundle, Now);

        var aps = Parse(envelope.Json).GetProperty("aps");

        Assert.Equal("end", aps.GetProperty("event").GetString());
        Assert.Equal(Now.AddMinutes(5).ToUnixTimeSeconds(), aps.GetProperty("dismissal-date").GetInt64());
    }

    [Fact]
    public void A_live_activity_defaults_to_priority_five_because_ten_costs_budget()
    {
        var normal = PushPayloads.ApnsLiveActivity("k", new LiveActivityLayout(), LiveActivityEvent.Update, null, null, Bundle, Now);
        var urgent = PushPayloads.ApnsLiveActivity("k", new LiveActivityLayout(), LiveActivityEvent.Update, null,
            new LiveActivityOptions { Priority = PushPriority.High }, Bundle, Now);

        Assert.Equal(5, normal.Priority);
        Assert.Equal(10, urgent.Priority);
    }

    [Fact]
    public void Android_gets_the_layout_as_data_at_high_priority()
    {
        var layout = new LiveActivityLayout { LockScreen = W.Text("x") };
        var data = Parse(PushPayloads.FcmLiveActivity("din-start:1", layout, LiveActivityEvent.Update).Json)
            .GetProperty("data");

        Assert.Equal("liveactivity", data.GetProperty(PushKeys.Kind).GetString());
        Assert.Equal("din-start:1", data.GetProperty(PushKeys.Activity).GetString());
        Assert.Equal(layout.ToJson(), data.GetProperty(PushKeys.Layout).GetString());
    }

    [Fact]
    public void The_android_live_update_carries_the_stale_time_so_the_app_can_dim_it()
    {
        var envelope = PushPayloads.FcmLiveActivity(
            "din-start:1", new LiveActivityLayout(), LiveActivityEvent.Update,
            new LiveActivityOptions { StaleAt = Now.AddMinutes(10) });

        var data = Parse(envelope.Json).GetProperty("data");

        Assert.Equal(Now.AddMinutes(10).ToUnixTimeSeconds().ToString(), data.GetProperty("spine.stale").GetString());
    }

    [Fact]
    public void A_widget_refresh_is_a_silent_push_on_apple_and_a_data_message_on_android()
    {
        var apple = PushPayloads.ApnsWidgetRefresh("next-start", Bundle);
        var android = PushPayloads.FcmWidgetRefresh("next-start");

        Assert.Equal("background", apple.ApnsPushType);
        Assert.Equal("widget", Parse(apple.Json).GetProperty(PushKeys.Kind).GetString());
        Assert.Equal("next-start", Parse(apple.Json).GetProperty(PushKeys.Widget).GetString());
        Assert.Equal("next-start", Parse(android.Json).GetProperty("data").GetProperty(PushKeys.Widget).GetString());
    }

    [Fact]
    public void A_widget_refresh_without_a_kind_means_all_of_them()
    {
        Assert.False(Parse(PushPayloads.ApnsWidgetRefresh(null, Bundle).Json).TryGetProperty(PushKeys.Widget, out _));
    }

    [Fact]
    public void The_platform_hooks_run_after_spine_has_built_the_payload()
    {
        var apns = PushPayloads.Apns(Notification with { Apple = p => p.Sound = "alarm.caf" }, Bundle, Now);
        var fcm = PushPayloads.Fcm(Notification with { Android = m => m.Data["extra"] = "1" });

        Assert.Equal("alarm.caf", Parse(apns.Json).GetProperty("aps").GetProperty("sound").GetString());
        Assert.Equal("1", Parse(fcm.Json).GetProperty("data").GetProperty("extra").GetString());
    }

    [Fact]
    public void Orienteras_live_activity_fits_the_apns_limit_with_room_to_spare()
    {
        var brand = WidgetColor.FromHex("#1B5E3F");
        var start = new DateTimeOffset(2026, 9, 12, 10, 24, 0, TimeSpan.FromHours(2));
        var timer = W.Timer(start).Bold().Color(brand);
        const string name = "Gävle OK Medeldistans, Sverigelistan";

        var layout = new LiveActivityLayout
        {
            LockScreen = W.VStack(4, W.HStack(8,
                W.Icon("fish", brand),
                W.VStack(2, W.Text(name).Headline().Bold(), W.Text("Din start 10:24").Caption().Secondary()),
                W.Spacer(),
                timer.Title())),
            ExpandedLeading = W.Icon("fish", brand),
            ExpandedTrailing = timer.Headline(),
            ExpandedCenter = W.Text(name).Headline().Bold(),
            ExpandedBottom = W.Text("Din start 10:24 · Hemlingby friluftsområde, Gävle").Caption().Secondary(),
            CompactLeading = W.Icon("fish", brand),
            CompactTrailing = timer.Caption(),
            Minimal = W.Icon("fish", brand),
        };

        var envelope = PushPayloads.ApnsLiveActivity("din-start:59691", layout, LiveActivityEvent.Update, null, null, Bundle, Now);
        var size = Encoding.UTF8.GetByteCount(envelope.Json);

        Assert.InRange(size, 1, PushPayloads.ApnsPayloadLimit);

        // Three quarters, not half. ActivityKit's ContentState holds the layout as a single string,
        // so every quote in it is escaped — about a third of the payload is that escaping alone.
        // This layout measures ~2.4 kB of the 4 kB budget; the guard is here to catch one that grows
        // past what the escaping leaves room for, not to argue with the encoding.
        Assert.True(size < PushPayloads.ApnsPayloadLimit * 3 / 4,
            $"The layout is {size} bytes; over three quarters of the {PushPayloads.ApnsPayloadLimit}-byte budget means it is time to consider a compact form.");
    }

    [Fact]
    public void A_payload_over_the_limit_is_refused_with_its_size()
    {
        var huge = new LiveActivityLayout { LockScreen = W.Text(new string('x', 5000)) };

        var error = Assert.Throws<InvalidOperationException>(() =>
            PushPayloads.ApnsLiveActivity("k", huge, LiveActivityEvent.Update, null, null, Bundle, Now));

        Assert.Contains("4096", error.Message);
    }

    [Fact]
    public void A_category_travels_as_aps_category_and_as_a_spine_key()
    {
        var notification = Notification with { Category = "entry" };

        var apns = Parse(PushPayloads.Apns(notification, Bundle, Now).Json);
        var fcm = Parse(PushPayloads.Fcm(notification).Json).GetProperty("data");

        Assert.Equal("entry", apns.GetProperty("aps").GetProperty("category").GetString());
        Assert.Equal("entry", apns.GetProperty(PushKeys.Category).GetString());
        Assert.Equal("entry", fcm.GetProperty(PushKeys.Category).GetString());
    }

    [Fact]
    public void An_image_makes_the_apns_payload_mutable_and_travels_as_a_spine_key_on_both()
    {
        var notification = Notification with { Image = new Uri("https://example.com/map.png") };

        var apns = Parse(PushPayloads.Apns(notification, Bundle, Now).Json);
        var fcm = Parse(PushPayloads.Fcm(notification).Json).GetProperty("data");

        Assert.Equal(1, apns.GetProperty("aps").GetProperty("mutable-content").GetInt32());
        Assert.Equal("https://example.com/map.png", apns.GetProperty(PushKeys.Image).GetString());
        Assert.Equal("https://example.com/map.png", fcm.GetProperty(PushKeys.Image).GetString());
    }

    [Fact]
    public void Without_an_image_the_apns_payload_is_not_mutable()
    {
        var aps = Parse(PushPayloads.Apns(Notification, Bundle, Now).Json).GetProperty("aps");

        Assert.False(aps.TryGetProperty("mutable-content", out _));
        Assert.False(aps.TryGetProperty("category", out _));
    }

    [Fact]
    public void An_image_that_is_not_https_is_refused_rather_than_dropped_on_the_device()
    {
        var notification = Notification with { Image = new Uri("http://example.com/map.png") };

        Assert.Throws<InvalidOperationException>(() => PushPayloads.Apns(notification, Bundle, Now));
        Assert.Throws<InvalidOperationException>(() => PushPayloads.Fcm(notification));
    }

    [Fact]
    public void The_widget_push_uses_the_widgets_type_and_topic_and_says_content_changed()
    {
        var envelope = PushPayloads.ApnsWidgetPush(Bundle);
        var aps = Parse(envelope.Json).GetProperty("aps");

        Assert.Equal("widgets", envelope.ApnsPushType);
        Assert.Equal($"{Bundle}.push-type.widgets", envelope.ApnsTopic);
        Assert.True(aps.GetProperty("content-changed").GetBoolean());
        Assert.False(aps.TryGetProperty("content-available", out _));
    }
}
