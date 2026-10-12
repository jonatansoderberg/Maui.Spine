using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Plugin.Maui.Spine.Server.AI.Tests;

public class AIFeaturesTests
{
    [Fact]
    public async Task A_configured_feature_answers_through_its_provider()
    {
        using var host = new TestHost(TestHost.Settings());

        var answer = await host.Story().GetResponseAsync("Skriv något.");

        Assert.Equal(AIFeatureState.On, host.Features.StateOf("Story"));
        Assert.Equal("Hej då.", answer.Text);
        Assert.Equal(1, host.Chat.Calls);
    }

    [Fact]
    public async Task The_features_defaults_reach_the_provider_and_a_calls_own_values_win()
    {
        using var host = new TestHost(TestHost.Settings(
            ("Ai:Features:Story:MaxOutputTokens", "800"),
            ("Ai:Features:Story:Temperature", "0.2"),
            ("Ai:Features:Story:Reasoning", "Low")));

        await host.Story().GetResponseAsync("Skriv något.");
        Assert.Equal(TestHost.Model, host.Chat.LastOptions?.ModelId);
        Assert.Equal(800, host.Chat.LastOptions?.MaxOutputTokens);
        Assert.Equal(0.2f, host.Chat.LastOptions?.Temperature);
        Assert.Equal(ReasoningEffort.Low, host.Chat.LastOptions?.Reasoning?.Effort);

        await host.Story().GetResponseAsync("Skriv något.", new ChatOptions { MaxOutputTokens = 50 });
        Assert.Equal(50, host.Chat.LastOptions?.MaxOutputTokens);
    }

    [Theory]
    [InlineData("Ai:Features:Story:Enabled", "false", AIFeatureState.Disabled)]
    [InlineData("Ai:Features:Story:Provider", "nobody", AIFeatureState.NotConfigured)]
    [InlineData("Ai:Features:Story:Model", "", AIFeatureState.NotConfigured)]
    [InlineData("Ai:Providers:fake:ApiKey", "", AIFeatureState.NotConfigured)]
    public void A_feature_that_cannot_run_is_off_with_a_reason(string key, string value, AIFeatureState expected)
    {
        using var host = new TestHost(TestHost.Settings((key, value)));

        Assert.Equal(expected, host.Features.StateOf("Story"));
        Assert.Null(host.Features.Chat("Story", AICaller.System));
    }

    [Fact]
    public void An_unknown_feature_is_not_configured()
    {
        using var host = new TestHost(TestHost.Settings());

        Assert.Equal(AIFeatureState.NotConfigured, host.Features.StateOf("Nothing"));
        Assert.Null(host.Features.Chat("Nothing", AICaller.System));
    }

    [Fact]
    public void A_provider_whose_factory_throws_turns_its_features_off()
    {
        using var host = new TestHost(
            TestHost.Settings(("Ai:Features:Story:Provider", "broken")),
            ai => ai.Chat("broken", (_, _) => throw new InvalidOperationException("no key")));

        Assert.Equal(AIFeatureState.NotConfigured, host.Features.StateOf("Story"));
    }

    [Fact]
    public void Feature_names_are_case_insensitive()
    {
        using var host = new TestHost(TestHost.Settings());

        Assert.NotNull(host.Features.Chat("story", AICaller.System));
    }

    [Fact]
    public async Task A_model_change_in_configuration_takes_effect_without_a_restart()
    {
        using var host = new TestHost(TestHost.Settings());
        await host.Story().GetResponseAsync("Skriv något.");
        Assert.Equal(TestHost.Model, host.Chat.LastOptions?.ModelId);

        host.Configuration["Ai:Features:Story:Model"] = "fake-2";
        host.Configuration.Reload();

        await host.Story().GetResponseAsync("Skriv något.");
        Assert.Equal("fake-2", host.Chat.LastOptions?.ModelId);
    }

    [Fact]
    public async Task A_keyed_client_calls_as_the_system_and_says_why_when_off()
    {
        using var host = new TestHost(TestHost.Settings());
        var story = host.Services.GetRequiredKeyedService<IChatClient>("Story");

        await story.GetResponseAsync("Skriv något.");
        Assert.Equal("system", Assert.Single(host.Store.Records).Caller);

        host.Configuration["Ai:Features:Story:Enabled"] = "false";
        host.Configuration.Reload();

        var off = await Assert.ThrowsAsync<AIFeatureOffException>(() => story.GetResponseAsync("Skriv något."));
        Assert.Equal(AIOffReason.Disabled, off.Reason);
    }

    [Fact]
    public async Task An_image_feature_gets_its_size_and_count_and_is_priced_per_image()
    {
        using var host = new TestHost(TestHost.Settings(
            ("Ai:Features:Arena:Kind", "Image"),
            ("Ai:Features:Arena:Provider", "painter"),
            ("Ai:Features:Arena:Model", "paint-1"),
            ("Ai:Features:Arena:Image:Size", "1536x1024"),
            ("Ai:Features:Arena:Image:Count", "1"),
            ("Ai:Prices:painter:paint-1:PerImage", "0.06")));

        var arena = host.Services.GetRequiredKeyedService<IImageGenerator>("Arena");
        await arena.GenerateImagesAsync("En skog.");

        Assert.Equal(new System.Drawing.Size(1536, 1024), host.Image.LastOptions?.ImageSize);
        Assert.Equal(1, host.Image.LastOptions?.Count);

        var record = Assert.Single(host.Store.Records);
        Assert.Equal(1, record.Images);
        Assert.Equal(0.06m, record.Cost);
        Assert.True(record.Complete);
    }

    [Fact]
    public async Task A_cached_answer_does_not_reach_the_provider_and_costs_nothing()
    {
        using var host = new TestHost(
            TestHost.Settings(("Ai:Features:Story:Cache:Ttl", "00:10:00")),
            services: s => s.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()))));

        await host.Story(AICaller.Installation("a")).GetResponseAsync("Samma fråga.");
        var second = await host.Story(AICaller.Installation("b")).GetResponseAsync("Samma fråga.");

        Assert.Equal("Hej då.", second.Text);
        Assert.Equal(1, host.Chat.Calls);
        Assert.Single(host.Store.Records);
    }

    [Fact]
    public void Disposing_a_callers_client_leaves_the_feature_working()
    {
        using var host = new TestHost(TestHost.Settings());

        host.Story().Dispose();

        Assert.NotNull(host.Features.Chat("Story", AICaller.System));
    }
}
