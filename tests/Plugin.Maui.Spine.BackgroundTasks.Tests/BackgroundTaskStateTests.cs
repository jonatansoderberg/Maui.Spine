using Plugin.Maui.Spine.BackgroundTasks.Services;
using Plugin.Maui.Spine.Common;
using Xunit;

namespace Plugin.Maui.Spine.BackgroundTasks.Tests;

/// <summary>The schedule document kept in Preferences between launches.</summary>
public class BackgroundTaskStateTests
{
    [Fact]
    public void Round_trips_every_field()
    {
        var at = new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.FromHours(2));
        var state = new BackgroundTaskState
        {
            LastStarted = at,
            LastEnded = at.AddSeconds(4),
            LastCompleted = at.AddMinutes(-30),
            LastOutcome = BackgroundTaskOutcome.Failed,
            LastTrigger = BackgroundTaskTrigger.CatchUp,
            LastError = "HttpRequestException: 502 on /standings",
        };

        var read = BackgroundTaskState.Deserialize(BackgroundTaskState.Serialize(new Dictionary<string, BackgroundTaskState> { ["standings"] = state }));

        Assert.Equal(state, read["standings"]);
    }

    [Fact]
    public void Enums_are_stored_by_name()
    {
        var json = BackgroundTaskState.Serialize(new Dictionary<string, BackgroundTaskState>
        {
            ["a"] = new() { LastOutcome = BackgroundTaskOutcome.Completed, LastTrigger = BackgroundTaskTrigger.Scheduled },
        });

        Assert.Contains("\"Completed\"", json);
        Assert.Contains("\"Scheduled\"", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    public void A_missing_or_broken_document_is_an_empty_schedule(string? json) =>
        Assert.Empty(BackgroundTaskState.Deserialize(json));

    [Fact]
    public void Names_are_case_sensitive()
    {
        var read = BackgroundTaskState.Deserialize(BackgroundTaskState.Serialize(new Dictionary<string, BackgroundTaskState> { ["Sync"] = new() }));

        Assert.True(read.ContainsKey("Sync"));
        Assert.False(read.ContainsKey("sync"));
    }
}
