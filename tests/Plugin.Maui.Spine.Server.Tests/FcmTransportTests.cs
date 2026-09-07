using FirebaseAdmin.Messaging;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Server;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class FcmTransportTests
{
    private static readonly PushNotification Notification = new()
    {
        Title = "Resultat klara",
        Body = "Gävle OK",
        Route = "competition/59691",
        CollapseId = "results-59691",
        TimeToLive = TimeSpan.FromMinutes(30),
    };

    [Fact]
    public void The_message_is_data_only_with_the_spine_keys()
    {
        var message = FcmTransport.BuildMessage(PushPayloads.Fcm(Notification), ["tok1"]);

        Assert.Null(message.Notification);
        Assert.Equal("alert", message.Data[PushKeys.Kind]);
        Assert.Equal("Resultat klara", message.Data[PushKeys.Title]);
        Assert.Equal("competition/59691", message.Data[PushKeys.Route]);
    }

    [Fact]
    public void The_android_config_carries_priority_ttl_and_collapse_key()
    {
        var message = FcmTransport.BuildMessage(PushPayloads.Fcm(Notification), ["tok1"]);

        Assert.Equal(Priority.High, message.Android.Priority);
        Assert.Equal(TimeSpan.FromMinutes(30), message.Android.TimeToLive);
        Assert.Equal("results-59691", message.Android.CollapseKey);
    }

    [Fact]
    public void A_silent_message_goes_at_normal_priority()
    {
        var envelope = PushPayloads.FcmSilent(new Dictionary<string, string> { ["sync"] = "results" });
        var message = FcmTransport.BuildMessage(envelope, ["tok1"]);

        Assert.Equal(Priority.Normal, message.Android.Priority);
        Assert.Equal("results", message.Data["sync"]);
    }

    [Fact]
    public void The_tokens_land_where_the_sdk_expands_them_into_per_device_messages()
    {
        var message = FcmTransport.BuildMessage(PushPayloads.Fcm(Notification), ["tok1", "tok2"]);

        // Tokens is deprecated in favour of Fids, but only Tokens is expanded; see the comment in
        // FcmTransport. This test fails the day the SDK changes that, which is exactly when it should.
#pragma warning disable CS0618
        Assert.Equal(["tok1", "tok2"], message.Tokens);
#pragma warning restore CS0618
    }

    [Fact]
    public void A_batch_is_five_hundred_tokens()
    {
        Assert.Equal(500, FcmTransport.BatchSize);
    }

    [Theory]
    [InlineData(MessagingErrorCode.Unregistered, PushStatus.Invalid)]
    [InlineData(MessagingErrorCode.SenderIdMismatch, PushStatus.Invalid)]
    [InlineData(MessagingErrorCode.InvalidArgument, PushStatus.Invalid)]
    [InlineData(MessagingErrorCode.QuotaExceeded, PushStatus.Throttled)]
    [InlineData(MessagingErrorCode.Unavailable, PushStatus.Throttled)]
    [InlineData(MessagingErrorCode.Internal, PushStatus.Failed)]
    [InlineData(MessagingErrorCode.ThirdPartyAuthError, PushStatus.Failed)]
    public void Firebases_error_codes_map_to_a_status(MessagingErrorCode code, PushStatus expected)
    {
        Assert.Equal(expected, FcmTransport.StatusFor(code));
    }

    [Fact]
    public void An_error_without_a_messaging_code_is_a_plain_failure()
    {
        Assert.Equal(PushStatus.Failed, FcmTransport.StatusFor(null));
    }

    [Fact]
    public void The_live_activity_layout_survives_the_round_trip_into_the_sdk_message()
    {
        var layout = new LiveActivityLayout { LockScreen = W.Text("Ute på banan") };
        var envelope = PushPayloads.FcmLiveActivity("din-start:59691", layout, LiveActivityEvent.Update);

        var message = FcmTransport.BuildMessage(envelope, ["tok1"]);

        Assert.Equal(layout.ToJson(), message.Data[PushKeys.Layout]);
        Assert.Equal("update", message.Data["spine.event"]);
    }
}
