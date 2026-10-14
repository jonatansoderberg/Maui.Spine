using System.Net;
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

/// <summary>The app's client against a real Spine.Server.AI host, so the wire contract is tested from both ends.</summary>
public sealed class ServerClientTests : IAsyncLifetime
{
    private readonly FakeChatClient _model = new();
    private readonly InMemoryAIUsageStore _serverUsage = new();
    private readonly List<(string? Installation, string? Authorization)> _seen = [];
    private WebApplication _app = null!;
    private SpineAIRuntime _runtime = null!;

    private static readonly Dictionary<string, string?> Settings = new()
    {
        ["Ai:Features:Assistant:Provider"] = "fake",
        ["Ai:Features:Assistant:Model"] = "fake-1",
        ["Ai:Features:Assistant:Expose"] = "true",
        ["Ai:Features:Assistant:MaxTurns"] = "2",
        ["Ai:Features:Assistant:RequestsPerMinute:Installation"] = "4",
        ["Ai:Features:Spent:Provider"] = "fake",
        ["Ai:Features:Spent:Model"] = "fake-1",
        ["Ai:Features:Spent:Expose"] = "true",
        ["Ai:Features:Spent:Budget:DayHard"] = "0",
        ["Ai:Features:Members:Provider"] = "fake",
        ["Ai:Features:Members:Model"] = "fake-1",
        ["Ai:Features:Members:Expose"] = "true",
        ["Ai:Features:Members:Callers"] = "SignedIn",
        ["Ai:Features:Off:Provider"] = "fake",
        ["Ai:Features:Off:Model"] = "fake-1",
        ["Ai:Features:Off:Expose"] = "true",
        ["Ai:Features:Off:Enabled"] = "false",
        ["Ai:Prices:fake:fake-1:InputPerMillion"] = "3",
    };

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Settings);
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero)));
        builder.Services.AddSpineAI(builder.Configuration.GetSection("Ai"), ai =>
        {
            ai.Chat("fake", (_, _) => _model);
            ai.UseUsageStore(_ => _serverUsage);
            ai.Identify = request =>
            {
                _seen.Add((request.Headers["Spine-Installation"].ToString(), request.Headers.Authorization.ToString()));
                return ValueTask.FromResult(AICaller.Installation(request.Headers["Spine-Installation"].ToString()));
            };
        });

        _app = builder.Build();
        _app.MapSpineAI("/ai");
        await _app.StartAsync();

        _runtime = Runtime(new Uri("http://localhost/ai"));
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private SpineAIRuntime Runtime(Uri? backend, Func<CancellationToken, Task<string?>>? authorization = null)
    {
        var options = new ClientOptions { Backend = backend, HttpHandler = _app.GetTestServer().CreateHandler(), AuthorizationHeader = authorization };
        return new SpineAIRuntime(options, _ => Task.FromResult<string?>("phone-1"), new InMemoryAIDeviceUsageStore());
    }

    private IChatClient Server(string feature, SpineAIRuntime? runtime = null) => new SpineServerChatClient(feature, runtime ?? _runtime);

    [Fact]
    public async Task An_answer_comes_back_as_a_chat_response_with_its_usage()
    {
        var answer = await Server("Assistant").GetResponseAsync("Hej!");

        Assert.Equal("Hej då.", answer.Text);
        Assert.Equal(1000, answer.Usage?.InputTokenCount);
        Assert.Equal("Hej!", Assert.Single(_model.LastMessages).Text);
    }

    [Fact]
    public async Task A_stream_comes_back_update_by_update_with_its_usage_last()
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in Server("Assistant").GetStreamingResponseAsync("Hej!"))
            updates.Add(update);

        Assert.Equal("Hej då.", string.Concat(updates.Select(u => u.Text)));
        Assert.IsType<UsageContent>(Assert.Single(updates[^1].Contents));
    }

    [Fact]
    public async Task Each_update_arrives_before_the_next_is_made()
    {
        _model.Hold = new TaskCompletionSource();

        await using var updates = Server("Assistant").GetStreamingResponseAsync("Hej!").GetAsyncEnumerator();
        var first = updates.MoveNextAsync().AsTask();

        Assert.Same(first, await Task.WhenAny(first, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal("Hej", updates.Current.Text);

        _model.Hold.SetResult();
        while (await updates.MoveNextAsync()) { }
    }

    [Fact]
    public async Task The_server_sees_the_installation_and_the_apps_authorization()
    {
        var runtime = Runtime(new Uri("http://localhost/ai/"), _ => Task.FromResult<string?>("Bearer abc"));

        await Server("Assistant", runtime).GetResponseAsync("Hej!");

        Assert.Equal(("phone-1", "Bearer abc"), Assert.Single(_seen));
    }

    [Theory]
    [InlineData("Spent", AIOffReason.Budget)]
    [InlineData("Off", AIOffReason.Disabled)]
    [InlineData("Nothing", AIOffReason.NotConfigured)]
    [InlineData("Members", AIOffReason.SignInRequired)]
    public async Task A_refusal_is_the_off_exception_with_its_reason(string feature, AIOffReason reason)
    {
        var plain = await Assert.ThrowsAsync<AIFeatureOffException>(() => Server(feature).GetResponseAsync("Hej!"));
        var streamed = await Assert.ThrowsAsync<AIFeatureOffException>(async () =>
        {
            await foreach (var _ in Server(feature).GetStreamingResponseAsync("Hej!")) { }
        });

        Assert.Equal(reason, plain.Reason);
        Assert.Equal(reason, streamed.Reason);
        Assert.Equal(feature, plain.Feature);
    }

    [Fact]
    public async Task Too_many_calls_are_the_rate_limited_exception_with_a_retry_time()
    {
        for (var i = 0; i < 4; i++) await Server("Assistant").GetResponseAsync("Hej!");

        var limited = await Assert.ThrowsAsync<AIRateLimitedException>(() => Server("Assistant").GetResponseAsync("Hej!"));

        Assert.True(limited.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task Too_long_a_conversation_is_the_too_large_exception()
    {
        var large = await Assert.ThrowsAsync<AIRequestTooLargeException>(() => Server("Assistant").GetResponseAsync(
            [new(ChatRole.User, "1"), new(ChatRole.Assistant, "a"), new(ChatRole.User, "2"), new(ChatRole.Assistant, "b"), new(ChatRole.User, "3")]));

        Assert.Equal("turns", large.Limit);
        Assert.Equal(2, large.Max);
    }

    [Fact]
    public async Task A_wrong_backend_is_not_mistaken_for_a_feature_that_is_off()
    {
        var wrong = Runtime(new Uri("http://localhost/api"));

        var failed = await Assert.ThrowsAsync<HttpRequestException>(() => Server("Assistant", wrong).GetResponseAsync("Hej!"));

        Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);
        Assert.Contains("http://localhost/api/features/Assistant", failed.Message);
    }

    [Fact]
    public async Task A_feature_can_run_on_a_server_feature_with_another_name()
    {
        _runtime.Options.Feature("Helper", f => f.Server("Assistant"));

        var answer = await _runtime.Feature("Helper", new ServiceCollection().BuildServiceProvider())!.GetResponseAsync("Hej!");

        Assert.Equal("Hej då.", answer.Text);
        Assert.Equal(AIAnsweredBy.Server, answer.AnsweredBy());
    }

    [Fact]
    public async Task No_backend_says_which_setting_is_missing()
    {
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => Server("Assistant", Runtime(null)).GetResponseAsync("Hej!"));

        Assert.Contains("SpineAIOptions.Backend", failed.Message);
    }

    [Fact]
    public async Task A_provider_that_fails_before_answering_is_a_502()
    {
        _model.ThrowAtOnce = new InvalidOperationException("upstream");

        var failed = await Assert.ThrowsAsync<HttpRequestException>(() => Server("Assistant").GetResponseAsync("Hej!"));

        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
    }

    [Fact]
    public async Task A_stream_that_fails_midway_ends_in_an_exception_after_the_text_so_far()
    {
        _model.ThrowAfterFirst = new InvalidOperationException("cut");
        var text = "";

        var failed = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var update in Server("Assistant").GetStreamingResponseAsync("Hej!")) text += update.Text;
        });

        Assert.Equal("Hej", text);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.Contains("The model call failed.", failed.Message);
    }

    [Fact]
    public async Task Cancelling_a_stream_is_a_cancellation_whatever_the_handler_throws()
    {
        // A handler that answers a cancelled read the way iOS's NSUrlSessionHandler does.
        var runtime = new SpineAIRuntime(
            new ClientOptions { Backend = new Uri("http://localhost/ai"), HttpHandler = new TimesOutWhenCancelled(_app.GetTestServer().CreateHandler()) },
            _ => Task.FromResult<string?>("phone-1"), new InMemoryAIDeviceUsageStore());
        _model.Hold = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Server("Assistant", runtime).GetStreamingResponseAsync("Hej!", cancellationToken: cts.Token))
                cts.Cancel();
        });

        Assert.Equal(cts.Token, cancelled.CancellationToken);
    }

    private sealed class TimesOutWhenCancelled(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.Content = new TimeoutContent(await response.Content.ReadAsStreamAsync(cancellationToken));
            return response;
        }
    }

    private sealed class TimeoutContent(Stream inner) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => inner.CopyToAsync(stream);
        protected override bool TryComputeLength(out long length) { length = -1; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new TimeoutStream(inner));

        private sealed class TimeoutStream(Stream inner) : Stream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                try { return await inner.ReadAsync(buffer, cancellationToken); }
                catch (OperationCanceledException) { throw new TimeoutException("The request timed out."); }
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    [Fact]
    public async Task Leaving_a_stream_early_ends_it_on_the_server_too()
    {
        _model.Hold = new TaskCompletionSource();

        await foreach (var _ in Server("Assistant").GetStreamingResponseAsync("Hej!"))
            break;

        // The server records the stream when its enumeration ends, which the closed connection causes.
        for (var i = 0; i < 50 && _serverUsage.Records.Count == 0; i++) await Task.Delay(100);

        var record = Assert.Single(_serverUsage.Records);
        Assert.False(record.Complete);
    }
}
