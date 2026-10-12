using System.Net.Sockets;
using Azure.Data.Tables;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

/// <summary>
/// A fact that runs only when a storage emulator answers. Totals, ETag retries and key escaping are Table Storage
/// behaviour, so these run against Azurite and skip when it is not up.
/// </summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (!AzureTableAIUsageStoreTests.EmulatorIsUp)
            Skip = "No storage emulator on 127.0.0.1:10002. Start Azurite (azurite-table) or an Aspire AppHost with storage.";
    }
}

public sealed class AzureTableAIUsageStoreTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("SPINE_TEST_STORAGE") ?? "UseDevelopmentStorage=true";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly AIPeriod Day = AIPeriod.Day(Now, TimeZoneInfo.Utc);
    private static readonly AIPeriod Month = AIPeriod.Month(Now, TimeZoneInfo.Utc);

    private readonly string _prefix = $"SpineAITest{Guid.NewGuid():N}"[..24];
    private AzureTableAIUsageStore _store = null!;

    internal static bool EmulatorIsUp { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            using var socket = new TcpClient();
            return socket.ConnectAsync("127.0.0.1", 10002).Wait(TimeSpan.FromSeconds(2)) && socket.Connected;
        }
        catch
        {
            return false;
        }
    }

    public Task InitializeAsync()
    {
        _store = new AzureTableAIUsageStore(ConnectionString, _prefix);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (!EmulatorIsUp) return;
        await new TableClient(ConnectionString, $"{_prefix}Usage").DeleteAsync();
        await new TableClient(ConnectionString, $"{_prefix}Totals").DeleteAsync();
    }

    private static AIUsageRecord Record(string caller = "installation:a", decimal? cost = 0.006m, DateTimeOffset? at = null, string feature = "Story") =>
        new(at ?? Now, feature, "fake", "fake-1", caller, 1000, 400, 200, 0, 0, cost, true, TimeSpan.FromSeconds(1.5), null);

    [AzuriteFact]
    public async Task A_record_round_trips_and_counts_toward_every_scope()
    {
        await _store.RecordAsync(Record(), [Day, Month]);
        await _store.RecordAsync(Record(caller: "user:anna", cost: null), [Day, Month]);

        Assert.Equal(new AIUsageTotals(0.006m, 2), await _store.GetTotalsAsync(AIUsageScope.App, Day));
        Assert.Equal(new AIUsageTotals(0.006m, 2), await _store.GetTotalsAsync(AIUsageScope.ForFeature("Story"), Month));
        Assert.Equal(new AIUsageTotals(0, 1), await _store.GetTotalsAsync(new AIUsageScope("Story", "user:anna"), Day));
        Assert.Equal(AIUsageTotals.Zero, await _store.GetTotalsAsync(AIUsageScope.ForFeature("Other"), Day));

        var records = new List<AIUsageRecord>();
        await foreach (var r in _store.QueryAsync(Now.AddHours(-1), Now.AddHours(1))) records.Add(r);

        Assert.Equal(2, records.Count);
        Assert.Contains(Record(), records);
        Assert.Contains(records, r => r.Cost is null);
    }

    [AzuriteFact]
    public async Task A_query_spans_days_and_filters_by_feature()
    {
        await _store.RecordAsync(Record(at: Now.AddDays(-2)), [Day]);
        await _store.RecordAsync(Record(at: Now, feature: "Other"), [Day]);
        await _store.RecordAsync(Record(at: Now.AddDays(1)), [Day]);

        var records = new List<AIUsageRecord>();
        await foreach (var r in _store.QueryAsync(Now.AddDays(-3), Now.AddHours(1), "Story")) records.Add(r);

        Assert.Equal(Now.AddDays(-2), Assert.Single(records).At);
    }

    [AzuriteFact]
    public async Task Concurrent_calls_from_several_instances_lose_no_increment()
    {
        var instances = Enumerable.Range(0, 4).Select(_ => new AzureTableAIUsageStore(ConnectionString, _prefix)).ToList();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            instances[i % instances.Count].RecordAsync(Record(caller: $"installation:{i % 3}", cost: 0.001m), [Day, Month])));

        Assert.Equal(new AIUsageTotals(0.040m, 40), await _store.GetTotalsAsync(AIUsageScope.App, Day));
        Assert.Equal(new AIUsageTotals(0.040m, 40), await _store.GetTotalsAsync(AIUsageScope.ForFeature("Story"), Month));
        Assert.Equal(14, (await _store.GetTotalsAsync(new AIUsageScope("Story", "installation:0"), Day)).Calls);
    }

    [AzuriteFact]
    public async Task An_alert_is_marked_once()
    {
        Assert.True(await _store.TryMarkAlertAsync(AIUsageScope.ForFeature("Story"), Day, AIBudgetLevel.Soft));
        Assert.False(await new AzureTableAIUsageStore(ConnectionString, _prefix).TryMarkAlertAsync(AIUsageScope.ForFeature("Story"), Day, AIBudgetLevel.Soft));
        Assert.True(await _store.TryMarkAlertAsync(AIUsageScope.ForFeature("Story"), Day, AIBudgetLevel.Hard));
        Assert.True(await _store.TryMarkAlertAsync(AIUsageScope.ForFeature("Story"), Month, AIBudgetLevel.Soft));
    }

    [AzuriteFact]
    public async Task Caller_ids_with_characters_table_keys_refuse_are_kept_apart()
    {
        await _store.RecordAsync(Record(caller: "user:https://login/abc#1?x"), [Day]);
        await _store.RecordAsync(Record(caller: "user:https:%2F%2Flogin%2Fabc%231%3Fx"), [Day]);

        Assert.Equal(1, (await _store.GetTotalsAsync(new AIUsageScope("Story", "user:https://login/abc#1?x"), Day)).Calls);
        Assert.Equal(1, (await _store.GetTotalsAsync(new AIUsageScope("Story", "user:https:%2F%2Flogin%2Fabc%231%3Fx"), Day)).Calls);
    }
}

public class TableKeyTests
{
    [Theory]
    [InlineData("Story|installation:abc", "Story|installation:abc")]
    [InlineData("Story|user:a/b", "Story|user:a%2Fb")]
    [InlineData("100%", "100%25")]
    [InlineData("a\tb#c?d\\e", "a%09b%23c%3Fd%5Ce")]
    public void Keys_escape_what_table_storage_refuses(string value, string expected)
    {
        Assert.Equal(expected, AzureTableAIUsageStore.Key(value));
    }
}
