using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Plugin.Maui.Spine.Voice.LiveTests;

/// <summary>
/// Every session kind against the real models, as the app runs it: the user asks for the time, which only the app's
/// function knows, then says goodbye, and the session ends on its own. One class, so the sessions run one at a time.
/// </summary>
/// <remarks>
/// Run with the keys in the environment, never in a file:
/// <c>OPENAI_API_KEY=… ANTHROPIC_API_KEY=… dotnet test tests/Plugin.Maui.Spine.Voice.LiveTests</c>.
/// </remarks>
public sealed class SessionKindTests(ITestOutputHelper output) : IAsyncLifetime
{
    // OpenAI's speech has taken 33 s for its first audio (2026-10-27): the limits allow for a slow provider.
    private static readonly TimeSpan Answer = TimeSpan.FromSeconds(90);

    private LiveServer _server = null!;
    private int _appClock;
    private readonly Lock _ui = new();

    public async Task InitializeAsync() => _server = await LiveServer.StartAsync();

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private SpineAIFunction AppClock => SpineAIFunction.Create("show_time", "Says what time it is on the user's phone.", () =>
    {
        Interlocked.Increment(ref _appClock);
        return "14:37";
    });

    [LiveFact(LiveFactAttribute.OpenAI)]
    public async Task Typed_chat_runs_the_apps_function_and_ends_on_goodbye()
    {
        var options = new SpineAIOptions { Backend = _server.Prefix };
        options.Functions.Add(AppClock);
        var runtime = new SpineAIRuntime(options, _ => Task.FromResult<string?>("live-tests"), new InMemoryAIDeviceUsageStore());
        var chain = new AIFeatureChain();
        chain.Server();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var chat = new SpineFeatureChatClient("Assistant", chain, runtime, services);

        // What ChatView.EndsOnIntent offers each answer.
        var ended = false;
        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(() =>
            {
                ended = true;
                return SpineAIFunctions.SayGoodbye;
            }, SpineAIFunctions.EndConversationName, SpineAIFunctions.EndConversationDescription)],
        };
        List<ChatMessage> conversation = [new(ChatRole.User, "Vad är klockan?")];

        var time = await chat.GetResponseAsync(conversation, chatOptions);
        Assert.True(_appClock > 0, $"the app's clock was not asked; the answer was: {time.Text}");
        Assert.False(ended);

        conversation.AddRange(time.Messages);
        conversation.Add(new(ChatRole.User, "Tack, hej då!"));
        var goodbye = await chat.GetResponseAsync(conversation, chatOptions);
        Assert.True(ended, $"the chat did not end; the answer was: {goodbye.Text}");
    }

    [LiveFact(LiveFactAttribute.OpenAI)]
    public Task Realtime_runs_the_apps_function_and_hangs_up_on_goodbye() => Task.Run(() => CallAsync("Voice"));

    [LiveFact(LiveFactAttribute.OpenAI)]
    public Task Gpt_live_delegates_to_the_apps_function_and_hangs_up_on_goodbye() => Task.Run(() => CallAsync("VoiceLive"));

    [LiveFact(LiveFactAttribute.OpenAI, LiveFactAttribute.Anthropic)]
    public Task A_claude_pipeline_runs_the_apps_function_and_hangs_up_on_goodbye() => Task.Run(() => CallAsync("Claude"));

    /// <summary>A call: the time, then goodbye, spoken as the user would, and the session's own hang-up.</summary>
    /// <remarks>
    /// Run off xUnit's synchronization context: the session's loops continue on the context they started on, and with
    /// them on xUnit's few threads the test's own waits stood still until the call's time limit.
    /// </remarks>
    private async Task CallAsync(string feature)
    {
        var options = new SpineVoiceOptions { SessionEndpoint = _server.Prefix };
        options.Functions.Add(AppClock);
        var audio = new LiveAudio();
        Task<string?> Installation(CancellationToken _) => Task.FromResult<string?>("live-tests");
        void Dispatch(Action action)
        {
            lock (_ui)
                action();
        }

        await using var services = new ServiceCollection().BuildServiceProvider();
        var started = DateTime.UtcNow;
        var logger = new OutputLogger(output, started);
        IVoiceSession session;
        if (feature == "Claude")
        {
            // The pipeline's chat is the app's Spine.AI feature, as UseSpineAI registers it.
            var ai = new SpineAIOptions { Backend = _server.Prefix };
            ai.Functions.Add(AppClock);
            var runtime = new SpineAIRuntime(ai, Installation, new InMemoryAIDeviceUsageStore());
            var chain = new AIFeatureChain();
            chain.Server();
            var keyed = new ServiceCollection()
                .AddKeyedSingleton<IChatClient>("Claude", (sp, _) => new SpineFeatureChatClient("Claude", chain, runtime, sp))
                .BuildServiceProvider();
            options.Pipeline("Claude", p => p.AddServerSpeechToText("Transcribe").AddChat("Claude").AddServerTextToSpeech("Read"));
            session = new PipelineVoiceSession(audio, options, new PipelineServer(options, Installation), Dispatch, logger: logger, services: keyed);
        }
        else
        {
            session = new VoiceSession(audio, options, new VoiceTokenClient(options, Installation), () => new ClientVoiceSocket(), Dispatch, logger: logger);
        }

        await using var _ = session;
        session.EndsOnGoodbye = true;
        var ended = new TaskCompletionSource<(VoiceSessionEnd Reason, Exception? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Ended += (reason, error) => ended.TrySetResult((reason, error));

        void Log(string what) => output.WriteLine($"{(DateTime.UtcNow - started).TotalSeconds,6:0.0} s  {what}: {session.State}, app clock {_appClock}, server clock {_server.ServerClock}");
        session.StateChanged += state => output.WriteLine($"{(DateTime.UtcNow - started).TotalSeconds,6:0.0} s  state {state}");

        await session.ConnectAsync(feature);
        await Task.Delay(TimeSpan.FromSeconds(1));

        Log("connected");
        audio.Say(await LiveServer.SpeakAsync("Vad är klockan?"));
        Log("asked the time");
        await Until(() => _appClock > 0 || ended.Task.IsCompleted, Answer, () => $"the app's clock was not asked ({Said(session)})");
        if (ended.Task.IsCompleted)
            Assert.Fail($"the call ended early: {ended.Task.Result}");
        Log("the clock was asked");
        await QuietAsync(session, audio, TimeSpan.FromSeconds(2));

        Log("answered");
        // Said again, as a person would, when the call goes on: the transcriber has heard "Tack, hej då!" as "Heador".
        var goodbye = await LiveServer.SpeakAsync("Hej då, och tack för hjälpen!");
        for (var attempt = 1; attempt <= 2 && !ended.Task.IsCompleted; attempt++)
        {
            audio.Say(goodbye);
            Log($"said goodbye ({attempt})");
            await Task.WhenAny(ended.Task, QuietAsync(session, audio, TimeSpan.FromSeconds(4)));
        }
        await Until(() => ended.Task.IsCompleted, Answer, () => $"the call did not hang up: {session.State}, playing {audio.IsPlaying} ({Said(session)})");
        var end = await ended.Task;
        Log("ended");
        Assert.True(end.Reason == VoiceSessionEnd.Goodbye, $"the call ended with {end.Reason} {end.Error?.Message} ({Said(session)})");
    }

    /// <summary>Waits until the answer has played out and the session has listened for <paramref name="quiet"/>.</summary>
    private async Task QuietAsync(IVoiceSession session, LiveAudio audio, TimeSpan quiet)
    {
        var quietSince = DateTime.UtcNow;
        var deadline = DateTime.UtcNow + Answer;
        while (DateTime.UtcNow < deadline)
        {
            if (audio.IsPlaying || !audio.HasSaid || session.State is not VoiceState.Listening)
                quietSince = DateTime.UtcNow;
            else if (DateTime.UtcNow - quietSince > quiet)
                return;
            await Task.Delay(100);
        }
        Assert.Fail($"the answer did not end: {session.State}, playing {audio.IsPlaying}, said {audio.HasSaid} ({Said(session)})");
    }

    private string Said(IVoiceSession session)
    {
        lock (_ui)
            return string.Join(" | ", session.Transcript.Select(m => $"{m.Role}: {m.Text}{string.Concat(m.Contents.OfType<FunctionCallContent>().Select(c => $"[{c.Name}]"))}"));
    }

    private static async Task Until(Func<bool> condition, TimeSpan timeout, Func<string> what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        if (!condition())
            Assert.Fail(what());
    }
}
