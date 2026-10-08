using Plugin.Maui.Spine.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.BackgroundTasks.Services;

/// <summary>What is stored about one task between launches.</summary>
internal sealed record BackgroundTaskState
{
    public DateTimeOffset? LastStarted { get; init; }
    public DateTimeOffset? LastEnded { get; init; }
    public DateTimeOffset? LastCompleted { get; init; }
    public BackgroundTaskOutcome LastOutcome { get; init; }
    public BackgroundTaskTrigger? LastTrigger { get; init; }
    public string? LastError { get; init; }

    /// <summary>The stored document: every task's state by name.</summary>
    internal static string Serialize(IReadOnlyDictionary<string, BackgroundTaskState> states) =>
        JsonSerializer.Serialize(new Dictionary<string, BackgroundTaskState>(states, StringComparer.Ordinal), BackgroundTaskJsonContext.Default.DictionaryStringBackgroundTaskState);

    /// <summary>
    /// Reads what <see cref="Serialize"/> wrote. A document that no longer parses gives an empty
    /// schedule, which only means every task is due once.
    /// </summary>
    internal static Dictionary<string, BackgroundTaskState> Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new(StringComparer.Ordinal);
        try
        {
            var states = JsonSerializer.Deserialize(json, BackgroundTaskJsonContext.Default.DictionaryStringBackgroundTaskState);
            return states is null ? new(StringComparer.Ordinal) : new(states, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Dictionary<string, BackgroundTaskState>))]
internal sealed partial class BackgroundTaskJsonContext : JsonSerializerContext;
