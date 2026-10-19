using System.Collections.ObjectModel;
using System.Globalization;
using MauiSpineSampleApp.Pages.Avatars;
using MauiSpineSampleApp.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Voice;

namespace MauiSpineSampleApp.Pages.Chat;

public partial class ChatPageViewModel : SampleViewModel
{
    private readonly IReadOnlyList<ObservableObject> _options;
    private readonly SpineVoiceOptions _voiceOptions;
    private readonly IHighlights _highlights;
    private IDispatcherTimer? _clock;
    private IEdgeGlow? _edge;
    private string _using = "";

    public ChatPageViewModel()
    {
        var services = IPlatformApplication.Current!.Services;

        // Typed turns go to the chosen model: the in-app demo, Spine.AI's Assistant (the device first, then the push sample
        // server) or the server's alone. The server's Assistant has two functions: get_my_bookings runs there with the
        // signed-in user, and show_time runs on the phone or on the server, as this chat's options say.
        IChatClient demo = new DemoChatClient();
        var assistant = services.GetRequiredKeyedService<IChatClient>("Assistant");
        var server = services.GetRequiredKeyedService<IChatClient>("ServerAssistant");
        Client = demo;

        // Spoken turns go to the push sample server's realtime Voice feature, in the same conversation: the microphone
        // connects a session that is given the turns so far and adds its own to Messages.
        _voiceOptions = services.GetRequiredService<SpineVoiceOptions>();
        _highlights = services.GetRequiredService<IHighlights>();
        Server = _voiceOptions.SessionEndpoint?.ToString() ?? "http://localhost:5100/ai";
        Session = services.GetRequiredService<IVoiceSession>();
        Session.StateChanged += _ => ShowStatus();
        Session.Ended += (reason, error) =>
        {
            IsTalking = false;
            _clock?.Stop();
            Status = error is not null ? $"The call ended: {error.Message}"
                : reason == VoiceSessionEnd.Goodbye ? "You said goodbye, so it hung up."
                : _using;
        };
        Driver = new VoiceAvatarDriver(Session);
        // "Tack, hej då" ends the call, as with a person.
        Session.EndsOnGoodbye = true;
        // A simulator has no echo cancellation: the Mac's speaker reaches its microphone, the model hears its own answer
        // as the user talking over it, and cuts it off. Not sending the microphone while it speaks avoids that.
        if (DeviceInfo.DeviceType == DeviceType.Virtual)
            Session.Duplex = VoiceDuplex.HalfWhileSpeaking;

        var model = new ChoiceGroup("Model for typed turns", [
            new Choice("Demo", "Canned answers in the app, with a tool-call card.", () => Use(demo, "Demo answers in the app")),
            new Choice("Assistant", "Spine.AI's Assistant: Apple Intelligence on the device when it has it, else the push sample server.", () => Use(assistant, "Spine.AI's Assistant: the device first, then the server")),
            new Choice("Server", "The push sample server's Assistant. Ask \"Vilka bokningar har jag?\" signed in and signed out, or \"Vad är klockan?\".", () => Use(server, "The server's Assistant: ask about your bookings or the time")),
        ]);
        model.Select(0);
        Use(demo, "Demo answers in the app");

        var avatars = new ChoiceGroup("Avatar during a call", [.. VoiceAvatars.All.Select(a => new Choice(a.Name, a.Name switch
        {
            "Bars" or "Ring" => "Built into Spine: the answer's sound one way, the microphone's the other.",
            _ => "A character or ambient avatar following the conversation: its state, the levels and the mouth.",
        }, () => Avatar = a))]);
        avatars.Select(0);

        _options = [
            model,
            new ToggleOption("Read the clock on the server", "show_time is on both sides; this chat runs it on the server instead of the phone (ChatOptions.Prefer for typed turns, IVoiceSession.Prefer for spoken ones).", () => ClockOnServer, value => ClockOnServer = value),
            new ToggleOption("Show function calls", "A card for each function the model called (ChatView.ShowToolCalls), for debugging; the model tells the user the result anyway.", () => ShowToolCalls, value => ShowToolCalls = value),
            avatars,
            new ToggleOption("Hang up after goodbye", "Say goodbye (\"tack, hej då\") and the call ends once the answer has been said.", () => Session.EndsOnGoodbye, value => Session.EndsOnGoodbye = value),
            new ToggleOption("Glow the screen's edge", "During a call the window's edge glows with the answer's level (Spine.Controls.Highlight).", () => Edge, value => Edge = value),
            new TextOption("Server", "The push sample server's AI prefix for calls; on a phone, the Mac's address on the network or the Azure one.", () => Server, value => Server = value, Microsoft.Maui.Keyboard.Url),
        ];

        // The header bar has one trailing slot; the chat's menu takes it from the base class's theme menu.
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
        PageActions.Add(new PageAction(null, new MenuItems
        {
            new MenuAction("Options", null, ShowChatOptionsCommand),
            new MenuAction("New chat", null, NewChatCommand),
        }) { Svg = "more.svg", Description = "Chat" });
    }

    /// <summary>The conversation, typed and spoken; the view adds the typed turns, the voice session the spoken ones.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public IReadOnlyList<string> Suggestions { get; } = ["Vilka bokningar har jag?", "Vad är klockan?", "Summarise my race"];

    [ObservableProperty]
    public partial IChatClient Client { get; set; }

    /// <summary>What the line above the chat says: the model for typed turns, or the call's state.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>The chat's options: where show_time runs.</summary>
    [ObservableProperty]
    public partial ChatOptions Options { get; set; } = new ChatOptions().Prefer(SampleFunctions.ShowTime.Name, SpineAIFunctionSide.Device);

    [ObservableProperty]
    public partial bool ClockOnServer { get; set; }

    partial void OnClockOnServerChanged(bool value)
    {
        var side = value ? SpineAIFunctionSide.Server : SpineAIFunctionSide.Device;
        Options = new ChatOptions().Prefer(SampleFunctions.ShowTime.Name, side);
        Session.Prefer(SampleFunctions.ShowTime.Name, side);
    }

    [ObservableProperty]
    public partial bool ShowToolCalls { get; set; } = true;

    /// <summary>The spoken half of the conversation.</summary>
    public IVoiceSession Session { get; }

    /// <summary>The avatar follows the call: its state, the levels and bands each way, the mouth on the answer.</summary>
    public AvatarDriver Driver { get; }

    [ObservableProperty]
    public partial AvatarChoice Avatar { get; set; } = VoiceAvatars.All[0];

    /// <summary>A call is on or connecting: the microphone is a hang-up button, typed text goes into the call, the avatar shows.</summary>
    [ObservableProperty]
    public partial bool IsTalking { get; set; }

    /// <summary>The push sample server's AI prefix for calls; on a phone, the Mac's address on the network.</summary>
    [ObservableProperty]
    public partial string Server { get; set; }

    [ObservableProperty]
    public partial bool Edge { get; set; }

    partial void OnEdgeChanged(bool value) => UpdateEdge();

    partial void OnIsTalkingChanged(bool value) => UpdateEdge();

    private void UpdateEdge()
    {
        _edge?.Dispose();
        _edge = null;
        if (Edge && IsTalking && Application.Current?.Windows.FirstOrDefault() is { } window)
        {
            _edge = _highlights.AttachEdge(window);
            _edge.Intensity = () => Session.UpdateFrame().OutputLevel;
        }
    }

    private void Use(IChatClient client, string label)
    {
        Client = client;
        _using = label;
        if (!IsTalking)
            Status = label;
    }

    /// <summary>The microphone: starts a call in this conversation, or ends the one that is on.</summary>
    [RelayCommand]
    private async Task Talk()
    {
        if (IsTalking)
        {
            _clock?.Stop();
            await Session.EndAsync();
            IsTalking = false;
            Status = _using;
            return;
        }

        if (!Uri.TryCreate(Server, UriKind.Absolute, out var endpoint))
        {
            Status = "The server address is not a URL.";
            return;
        }

        _voiceOptions.SessionEndpoint = endpoint;
        IsTalking = true;
        Status = "Connecting…";
        try
        {
            await Session.ConnectAsync("Voice", Messages);
            _clock ??= Application.Current!.Dispatcher.CreateTimer();
            _clock.Interval = TimeSpan.FromSeconds(1);
            _clock.Tick -= OnClock;
            _clock.Tick += OnClock;
            _clock.Start();
            ShowStatus();
        }
        catch (Exception e)
        {
            IsTalking = false;
            Status = e switch
            {
                AIFeatureOffException { Reason: AIOffReason.SignInRequired } => "The server's Voice feature answers signed-in users only; sign in first.",
                AIFeatureOffException { Reason: AIOffReason.Budget } => "Today's Voice budget is spent; try again tomorrow.",
                AIFeatureOffException off => $"The server's Voice feature is off ({off.Reason}). Does it have an OpenAI key?",
                AIRateLimitedException limited => $"Too many calls; try again in {limited.RetryAfter?.TotalSeconds:0} s.",
                _ => e.Message,
            };
        }
    }

    /// <summary>Text typed during a call: said into the call, and answered aloud.</summary>
    [RelayCommand]
    private Task Say(string text) => Session.IsConnected ? Session.SendTextAsync(text) : Task.CompletedTask;

    [RelayCommand]
    private Task ShowChatOptions() => ShowOptionsAsync("Chat", _options);

    [RelayCommand]
    private async Task NewChat()
    {
        if (IsTalking)
            await Talk();
        Messages.Clear();
    }

    public override async Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _clock?.Stop();
        Edge = false;
        await Session.EndAsync();
        IsTalking = false;
    }

    private void OnClock(object? sender, EventArgs e) => ShowStatus();

    private void ShowStatus()
    {
        if (!Session.IsConnected)
            return;
        var left = Session.TimeLeft is { } t ? string.Create(CultureInfo.InvariantCulture, $" · {t:m\\:ss} left") : "";
        var voice = Session.SpokenVoice is { } v ? $" · {v}" : "";
        Status = $"Call: {Session.State}{voice}{left}";
    }
}
