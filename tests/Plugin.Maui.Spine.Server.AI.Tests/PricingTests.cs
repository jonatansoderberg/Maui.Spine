using Microsoft.Extensions.AI;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

public class PricingTests
{
    private static readonly AIPrice Sonnet = new() { InputPerMillion = 3, CachedInputPerMillion = 0.3m, OutputPerMillion = 15 };

    [Fact]
    public void Cached_input_is_taken_out_of_input_and_priced_on_its_own()
    {
        var usage = new UsageDetails { InputTokenCount = 10_000, CachedInputTokenCount = 8_000, OutputTokenCount = 1_000 };

        // 2 000 × 3 + 8 000 × 0.3 + 1 000 × 15, per million.
        Assert.Equal(0.0234m, AIPricing.Cost(Sonnet, usage, 0));
    }

    [Fact]
    public void Cached_input_without_a_price_of_its_own_costs_the_same_as_input()
    {
        var price = new AIPrice { InputPerMillion = 3 };

        Assert.Equal(0.003m, AIPricing.Cost(price, new UsageDetails { InputTokenCount = 1_000, CachedInputTokenCount = 600 }, 0));
    }

    [Fact]
    public void Reasoning_is_output_unless_it_has_a_price()
    {
        var usage = new UsageDetails { OutputTokenCount = 1_000, ReasoningTokenCount = 400 };

        Assert.Equal(0.015m, AIPricing.Cost(Sonnet, usage, 0));
        Assert.Equal(0.009m + 0.0004m, AIPricing.Cost(new AIPrice { OutputPerMillion = 15, ReasoningPerMillion = 1 }, usage, 0));
    }

    [Fact]
    public void Additional_counts_are_priced_by_name()
    {
        var price = new AIPrice { InputPerMillion = 3, Additional = { ["CacheCreationInputTokens"] = 3.75m } };
        var usage = new UsageDetails { InputTokenCount = 0, AdditionalCounts = new() { ["CacheCreationInputTokens"] = 2_000, ["Unknown"] = 5 } };

        Assert.Equal(0.0075m, AIPricing.Cost(price, usage, 0));
    }

    [Fact]
    public void Images_are_priced_per_image_on_top_of_tokens()
    {
        var price = new AIPrice { PerImage = 0.06m, InputPerMillion = 5 };

        Assert.Equal(0.12m, AIPricing.Cost(price, null, 2));
        Assert.Equal(0.125m, AIPricing.Cost(price, new UsageDetails { InputTokenCount = 1_000 }, 2));
    }

    [Fact]
    public void No_price_means_no_cost()
    {
        Assert.Null(AIPricing.Cost(null, new UsageDetails { InputTokenCount = 1 }, 1));
    }
}

public class PeriodTests
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    [Fact]
    public void A_day_and_a_month_are_named_and_end_at_local_midnight()
    {
        var at = new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero); // 00:30 on 1 November in Stockholm

        var day = AIPeriod.Day(at, Stockholm);
        var month = AIPeriod.Month(at, Stockholm);

        Assert.Equal("d:2026-11-01", day.Key);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 23, 0, 0, TimeSpan.Zero), day.End);
        Assert.Equal("m:2026-11", month.Key);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero), month.End);
    }

    [Fact]
    public void The_day_the_clocks_go_back_ends_at_midnight_in_winter_time()
    {
        var day = AIPeriod.Day(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), Stockholm);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), day.End);
    }

    [Fact]
    public void Utc_is_the_default_day()
    {
        Assert.Equal("d:2026-10-12", AIPeriod.Day(new DateTimeOffset(2026, 10, 12, 23, 59, 0, TimeSpan.Zero), TimeZoneInfo.Utc).Key);
    }
}
