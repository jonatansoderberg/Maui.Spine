using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Plugin.Maui.Spine.Server.AI;
using Xunit;
using ClientOptions = Plugin.Maui.Spine.AI.SpineAIOptions;

namespace Plugin.Maui.Spine.AI.Tests;

/// <summary>A model that calls <see cref="Calls"/> when it is offered and the user spoke last, and otherwise reads back the results.</summary>
internal sealed class CallingModel : IChatClient
{
    private int _calls;

    public string Calls { get; set; } = "show_time";
    public List<ChatOptions?> Options { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Next(messages, options))));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, Next(messages, options));
    }

    private List<AIContent> Next(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var list = messages.ToList();
        Options.Add(options);

        if (list[^1].Role == ChatRole.User && options?.Tools?.Any(t => t.Name == Calls) == true)
            return [new FunctionCallContent($"call_{Interlocked.Increment(ref _calls)}", Calls, new Dictionary<string, object?>())];

        var results = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => r.Result is JsonElement { ValueKind: JsonValueKind.String } s ? s.GetString() : r.Result?.ToString());
        return [new TextContent("Svar: " + string.Join(" | ", results))];
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>The app's functions against a real Spine.Server.AI host: device calls handed back, server calls left alone, the order per chat.</summary>
public sealed class FunctionClientTests : IAsyncLifetime
{
    private readonly CallingModel _model = new();
    private readonly List<SpineAIFunctionSide> _ran = [];
    private WebApplication _app = null!;
    private SpineAIRuntime _runtime = null!;
    private ServiceProvider _services = null!;

    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Ai:Features:Assistant:Provider"] = "fake",
        ["Ai:Features:Assistant:Model"] = "fake-1",
        ["Ai:Features:Assistant:Expose"] = "true",
        ["Ai:Features:Assistant:Functions:0"] = "show_time",
        ["Ai:Features:Assistant:Functions:1"] = "get_my_bookings",
    };

    private SpineAIFunction ShowTime => SpineAIFunction.Create("show_time", "Says the time on the phone.", (SpineAIFunctionContext context) =>
    {
        _ran.Add(context.RunsOn);
        return context.RunsOn == SpineAIFunctionSide.Device ? "14:05 on the phone" : "on the server";
    }, SpineAIFunctionSide.Server);

    private SpineAIFunction Bookings => SpineAIFunction.Create("get_my_bookings", "Lists the bookings.", (SpineAIFunctionContext context) =>
    {
        _ran.Add(context.RunsOn);
        return "two bookings";
    }, SpineAIFunctionSide.Server);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.Zero)));
        builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
        {
            ai.Chat("fake", (_, _) => _model);
            ai.UseInMemoryUsageStore();
            ai.Functions.Add(ShowTime).AddDevice(ShowTime).Add(Bookings);
        });

        _app = builder.Build();
        _app.MapSpineAI("/ai");
        await _app.StartAsync();

        var options = new ClientOptions { Backend = new Uri("http://localhost/ai"), HttpHandler = _app.GetTestServer().CreateHandler() };
        options.Functions.Add(ShowTime);
        _runtime = new SpineAIRuntime(options, _ => Task.FromResult<string?>("phone-1"), new InMemoryAIDeviceUsageStore());
        _services = new ServiceCollection().BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _app.DisposeAsync();
    }

    private IChatClient Feature(Action<AIFeatureChain> configure)
    {
        var chain = new AIFeatureChain();
        configure(chain);
        return new SpineFeatureChatClient("Assistant", chain, _runtime, _services);
    }

    [Fact]
    public async Task By_default_a_function_both_sides_have_runs_where_it_prefers()
    {
        var answer = await Feature(f => f.Server()).GetResponseAsync([new(ChatRole.User, "Vad är klockan?")]);

        Assert.Equal([SpineAIFunctionSide.Server], _ran);
        Assert.Contains("on the server", answer.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_chat_can_run_it_on_the_device_and_the_server_takes_the_result(bool stream)
    {
        var client = Feature(f => f.Server());
        var options = new ChatOptions().Prefer("show_time", SpineAIFunctionSide.Device);
        ChatMessage[] question = [new(ChatRole.User, "Vad är klockan?")];

        var text = stream
            ? string.Concat(await client.GetStreamingResponseAsync(question, options).Select(u => u.Text).ToListAsync())
            : (await client.GetResponseAsync(question, options)).Text;

        Assert.Equal([SpineAIFunctionSide.Device], _ran);
        Assert.Equal("Svar: 14:05 on the phone", text);
    }

    [Fact]
    public async Task A_call_the_server_ran_is_not_run_again_in_the_app()
    {
        _model.Calls = "get_my_bookings";

        var answer = await Feature(f => f.Server()).GetResponseAsync([new(ChatRole.User, "Mina bokningar?")]);

        Assert.Equal([SpineAIFunctionSide.Server], _ran);
        Assert.Equal("Svar: two bookings", answer.Messages[^1].Text);
        Assert.True(answer.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Single().InformationalOnly);
    }

    [Fact]
    public async Task An_on_device_model_gets_the_apps_functions_and_they_run_with_the_device_context()
    {
        var device = new CallingModel();
        SpineAIFunctionContext? seen = null;
        _runtime.Options.Functions.Add("show_time", "Says the time.", (SpineAIFunctionContext context) => { seen = context; return "14:05"; });

        var answer = await Feature(f => f.OnDevice(_ => device)).GetResponseAsync([new(ChatRole.User, "Klockan?")], new ChatOptions().Prefer("show_time", SpineAIFunctionSide.Server));

        Assert.Equal("Svar: 14:05", answer.Text);
        Assert.Equal(SpineAIFunctionSide.Device, seen?.RunsOn);
        Assert.Equal(AICaller.Installation("phone-1"), seen?.Caller);
        Assert.Equal("Assistant", seen?.Feature);
        Assert.False(device.Options[0]!.AdditionalProperties?.ContainsKey(SpineAIFunctionOrder.PreferKey) ?? false);
        Assert.Empty(_model.Options);
    }

    [Fact]
    public void The_body_names_the_apps_functions_and_the_side_the_chat_wants()
    {
        var body = SpineServerChatClient.Body([new(ChatRole.User, "Hej")], null, [("show_time", SpineAIFunctionSide.Device)]);

        using var json = JsonDocument.Parse(body);
        var offer = json.RootElement.GetProperty("functions")[0];
        Assert.Equal(("show_time", "device"), (offer.GetProperty("name").GetString(), offer.GetProperty("prefer").GetString()));
    }
}
