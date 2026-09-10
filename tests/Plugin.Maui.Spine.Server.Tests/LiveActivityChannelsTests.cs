using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class LiveActivityChannelsTests
{
    [Fact]
    public void An_apns_channel_id_is_written_as_a_topic_fcm_accepts() =>
        Assert.Equal("spine-la-ab-c_d", LiveActivityChannels.Topic("ab+c/d=="));

    [Fact]
    public void A_name_no_topic_can_carry_is_refused() =>
        Assert.Throws<ArgumentException>(() => LiveActivityChannels.Topic("tävling 1"));
}
