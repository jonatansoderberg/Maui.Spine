using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.Server.Tests;

public class PushInstallationJsonTests
{
    private static readonly PushInstallation Full = new()
    {
        Id = "8f1c2f7e",
        Platform = PushPlatform.Apple,
        Handle = "b1946ac92492d2347c6235b4d2611184",
        Environment = ApnsEnvironment.Sandbox,
        Tags = ["user:121330", "kind:results-published", "competition:59691"],
        UserId = "121330",
        AppVersion = "0.1",
        OsVersion = "26.0",
        LiveActivities = new LiveActivityTokens
        {
            PushToStart = "aa11",
            Activities = new Dictionary<string, string> { ["din-start:59691"] = "bb22" },
        },
        WidgetToken = "cc33",
        UpdatedAt = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
        ExpiresAt = new DateTimeOffset(2026, 12, 8, 10, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void A_full_installation_survives_a_round_trip()
    {
        var back = PushJson.Deserialize(PushJson.Serialize(Full));

        Assert.Equal(Full.Id, back!.Id);
        Assert.Equal(Full.Platform, back.Platform);
        Assert.Equal(Full.Handle, back.Handle);
        Assert.Equal(Full.Environment, back.Environment);
        Assert.Equal(Full.Tags, back.Tags);
        Assert.Equal(Full.UserId, back.UserId);
        Assert.Equal("aa11", back.LiveActivities!.PushToStart);
        Assert.Equal("bb22", back.LiveActivities.Activities["din-start:59691"]);
        Assert.Equal(Full.WidgetToken, back.WidgetToken);
        Assert.Equal(Full.ExpiresAt, back.ExpiresAt);
    }

    [Fact]
    public void Enums_are_written_as_names_and_absent_values_are_left_out()
    {
        var json = PushJson.Serialize(new PushInstallation
        {
            Id = "a", Platform = PushPlatform.Android, Handle = "h",
            UpdatedAt = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero),
        });

        Assert.Contains("\"platform\":\"Android\"", json);
        Assert.DoesNotContain("environment", json);
        Assert.DoesNotContain("liveActivities", json);
        Assert.DoesNotContain("expiresAt", json);
    }

    [Fact]
    public void Expiry_is_read_against_a_supplied_clock()
    {
        var now = new DateTimeOffset(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);
        Assert.False(Full.IsExpired(now));
        Assert.True(Full.IsExpired(now.AddYears(1)));
        Assert.False((Full with { ExpiresAt = null }).IsExpired(now.AddYears(10)));
    }
}
