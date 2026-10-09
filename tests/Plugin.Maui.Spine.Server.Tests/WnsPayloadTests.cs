using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

/// <summary>
/// What the Windows app reads back from what the server sends. The app half cannot run here, so these
/// pin the format both halves go through.
/// </summary>
public class WnsPayloadTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_app_reads_the_toasts_launch_argument_back_into_the_message_the_server_built()
    {
        var envelope = PushPayloads.Wns(new PushNotification
        {
            Title = "Resultat; klara",
            Body = "100% = klart",
            Route = "/results?id=1;x=%3B",
            Channel = "results",
            CollapseId = "results-59691",
            Category = "entry",
            Data = new Dictionary<string, string> { ["competition"] = "59691", ["odd;key=x"] = "" },
        }, Now);

        var data = WnsPayload.ReadArguments(XElement.Parse(envelope.Json).Attribute("launch")!.Value);

        Assert.Equal(PushKeys.Kinds.Alert, data[PushKeys.Kind]);
        Assert.Equal("Resultat; klara", data[PushKeys.Title]);
        Assert.Equal("100% = klart", data[PushKeys.Body]);
        Assert.Equal("/results?id=1;x=%3B", data[PushKeys.Route]);
        Assert.Equal("results", data[PushKeys.Channel]);
        Assert.Equal("results-59691", data[PushKeys.Collapse]);
        Assert.Equal("entry", data[PushKeys.Category]);
        Assert.Equal("59691", data["competition"]);
        Assert.Equal("", data["odd;key=x"]);
    }

    [Fact]
    public void A_button_repeats_the_toasts_arguments_and_adds_its_own_id()
    {
        var launch = WnsPayload.WriteArguments(new Dictionary<string, string> { [PushKeys.Route] = "a;b" });
        var button = $"{launch};{WnsPayload.WriteArguments([new(WnsPayload.Action, "enter=me")])}";

        var data = WnsPayload.ReadArguments(button);

        Assert.Equal("a;b", data[PushKeys.Route]);
        Assert.Equal("enter=me", data[WnsPayload.Action]);
    }

    [Fact]
    public void A_toast_whose_data_names_a_button_is_refused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => PushPayloads.Wns(new PushNotification
        {
            Title = "T",
            Body = "B",
            Data = new Dictionary<string, string> { [WnsPayload.Action] = "delete" },
        }, Now));

        Assert.Contains(WnsPayload.Action, refused.Message);
    }

    [Fact]
    public void A_silent_message_whose_data_names_a_button_is_refused()
    {
        var refused = Assert.Throws<InvalidOperationException>(() =>
            PushPayloads.WnsSilent(new Dictionary<string, string> { [PushKeys.Kind] = PushKeys.Kinds.Alert, [WnsPayload.Action] = "delete" }));

        Assert.Contains(WnsPayload.Action, refused.Message);
    }

    [Fact]
    public void The_collapse_id_becomes_the_tag_the_app_gives_its_own_toasts()
    {
        var envelope = PushPayloads.Wns(new PushNotification
        {
            Title = "T",
            Body = "B",
            CollapseId = "results-59691-a-collapse-id-longer-than-sixteen-characters",
        }, Now);

        Assert.Equal(WnsPayload.Tag("results-59691-a-collapse-id-longer-than-sixteen-characters"), envelope.WnsTag);
        Assert.Equal(WnsPayload.TagLength, envelope.WnsTag!.Length);
        Assert.Null(PushPayloads.Wns(new PushNotification { Title = "T", Body = "B" }, Now).WnsTag);
    }

    [Fact]
    public void A_tag_is_stable_and_tells_ids_apart()
    {
        Assert.Equal(WnsPayload.Tag("a"), WnsPayload.Tag("a"));
        Assert.NotEqual(WnsPayload.Tag("a"), WnsPayload.Tag("b"));
        Assert.Equal(WnsPayload.TagLength, WnsPayload.Tag("").Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("%")]
    [InlineData("%25")]
    [InlineData("%3B%3D")]
    [InlineData(";;==;")]
    [InlineData("åäö 🙂")]
    public void Any_value_survives_the_round_trip(string value)
    {
        var data = new Dictionary<string, string> { [value + "k"] = value, ["next"] = value };

        Assert.Equal(data, WnsPayload.ReadArguments(WnsPayload.WriteArguments(data)));
    }

    [Fact]
    public void An_empty_argument_reads_as_no_data()
    {
        Assert.Empty(WnsPayload.ReadArguments(null));
        Assert.Empty(WnsPayload.ReadArguments(""));
    }

    [Fact]
    public void The_app_reads_the_raw_body_back_into_the_data_the_server_sent()
    {
        var envelope = PushPayloads.WnsSilent(new Dictionary<string, string>
        {
            [PushKeys.Task] = "refresh",
            ["quote"] = "\"; =\n",
        });

        var data = WnsPayload.ReadRaw(Encoding.UTF8.GetBytes(envelope.Json));

        Assert.Equal(PushKeys.Kinds.Silent, data[PushKeys.Kind]);
        Assert.Equal("refresh", data[PushKeys.Task]);
        Assert.Equal("\"; =\n", data["quote"]);
    }

    [Fact]
    public void A_hand_written_raw_body_keeps_values_that_are_not_strings_as_json()
    {
        var data = WnsPayload.ReadRaw("""{"spine.kind":"silent","count":3,"nested":{"a":true}}"""u8.ToArray());

        Assert.Equal("3", data["count"]);
        Assert.Equal("""{"a":true}""", data["nested"]);
    }

    [Fact]
    public void A_raw_body_that_is_not_an_object_is_refused_with_what_it_was()
    {
        var refused = Assert.Throws<JsonException>(() => WnsPayload.ReadRaw("[1,2]"u8.ToArray()));

        Assert.Contains("Array", refused.Message);
    }

    [Fact]
    public void A_channel_uri_survives_the_registration_unchanged()
    {
        // The escaped token is what WNS hands out; Uri.ToString() would unescape it, which is why the app
        // sends Uri.OriginalString.
        const string channel = "https://wns2-db5p.notify.windows.com/?token=AwYAAAB%2fQAhY%2bk%3d";

        var installation = PushJson.Deserialize(PushJson.Serialize(new PushInstallation
        {
            Id = "i",
            Platform = PushPlatform.Windows,
            Handle = channel,
        }))!;

        Assert.Equal(PushPlatform.Windows, installation.Platform);
        Assert.Equal(channel, installation.Handle);
        Assert.Null(installation.Environment);
    }
}
