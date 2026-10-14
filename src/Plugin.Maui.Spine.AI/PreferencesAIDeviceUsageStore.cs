using System.Text.Json.Nodes;

namespace Plugin.Maui.Spine.AI;

/// <summary>
/// On-device usage in <see cref="Preferences"/>: one entry per feature holding its last 62 days as
/// <c>{ "2026-10-12": [calls, input, output] }</c>.
/// </summary>
internal sealed class PreferencesAIDeviceUsageStore : IAIDeviceUsageStore
{
    private const string Prefix = "spine.ai.usage.";
    private const int KeptDays = 62;

    private readonly Lock _gate = new();

    public void Add(string feature, DateOnly day, AIDeviceUsage usage)
    {
        lock (_gate)
        {
            var days = Read(feature);
            var key = day.ToString("yyyy-MM-dd");
            var sum = (Parse(days[key]) ?? AIDeviceUsage.Zero).Plus(usage);
            days[key] = new JsonArray(sum.Calls, sum.InputTokens, sum.OutputTokens);

            var oldest = day.AddDays(-KeptDays).ToString("yyyy-MM-dd");
            foreach (var old in days.Select(d => d.Key).Where(k => string.CompareOrdinal(k, oldest) < 0).ToList())
                days.Remove(old);

            Preferences.Default.Set(Key(feature), days.ToJsonString());
        }
    }

    public AIDeviceUsage Get(string feature, DateOnly day)
    {
        lock (_gate)
        {
            return Parse(Read(feature)[day.ToString("yyyy-MM-dd")]) ?? AIDeviceUsage.Zero;
        }
    }

    private static string Key(string feature) => Prefix + feature.ToLowerInvariant();

    private static JsonObject Read(string feature)
    {
        try
        {
            return JsonNode.Parse(Preferences.Default.Get(Key(feature), "{}")) as JsonObject ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static AIDeviceUsage? Parse(JsonNode? node) =>
        node is JsonArray { Count: 3 } day && day[0] is { } calls && day[1] is { } input && day[2] is { } output
            ? new AIDeviceUsage(calls.GetValue<long>(), input.GetValue<long>(), output.GetValue<long>())
            : null;
}
