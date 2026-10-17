using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.AI;
using Microsoft.Extensions.AI;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Speech;
using Plugin.Maui.Spine.Voice;

namespace MauiSpineSampleApp.Pages.Assistant;

public partial class AssistantPageViewModel : SampleViewModel
{
    private readonly SpineVoiceOptions _options;
    private readonly IHighlights _highlights;
    private IDispatcherTimer? _clock;
    private IEdgeGlow? _edge;

    public AssistantPageViewModel()
    {
        var services = IPlatformApplication.Current!.Services;
        _options = services.GetRequiredService<SpineVoiceOptions>();
        _highlights = services.GetRequiredService<IHighlights>();
        Server = _options.SessionEndpoint?.ToString() ?? "http://localhost:5100/ai";

        Session = services.GetRequiredService<IVoiceSession>();
        Session.StateChanged += _ => ShowStatus();
        Session.Ended += (reason, error) =>
        {
            IsConnected = false;
            Status = error is null ? $"Ended: {reason}." : $"Ended: {error.Message}";
        };
        _voiceDriver = new VoiceAvatarDriver(Session);
        _speech = new SpeechAvatarDriver(services.GetRequiredService<ISpineSpeech>());
        _assistant = services.GetRequiredKeyedService<IChatClient>("Assistant");
        Driver = _voiceDriver;
        ChatClient = _demo;
        Avatar = Avatars[0];
    }

    private readonly VoiceAvatarDriver _voiceDriver;
    private readonly TextAvatarDriver _textDriver = new();
    private readonly SpeechAvatarDriver _speech;
    private readonly IChatClient _demo = new MauiSpineSampleApp.Pages.Chat.DemoChatClient();
    private readonly IChatClient _assistant;

    /// <summary>0: talk with the realtime model; 1: type, and the avatar mouths the answer as it streams in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoice), nameof(IsText))]
    public partial int ModeIndex { get; set; }

    public bool IsVoice => ModeIndex == 0;

    public bool IsText => ModeIndex == 1;

    /// <summary>The text mode's answers: the Showcase's offline demo, or the server's Assistant feature.</summary>
    [ObservableProperty]
    public partial IChatClient ChatClient { get; set; }

    [ObservableProperty]
    public partial bool UseServer { get; set; }

    /// <summary>Read each finished answer aloud, after mouthing it as it streamed.</summary>
    [ObservableProperty]
    public partial bool ReadAloud { get; set; }

    [ObservableProperty]
    public partial string Draft { get; set; } = "What can an avatar do?";

    partial void OnModeIndexChanged(int value)
    {
        _textDriver.Clear();
        _speech.Stop();
        if (value == 1)
            HostIndex = 0;
        Driver = value == 1 ? _textDriver : _voiceDriver;
    }

    partial void OnUseServerChanged(bool value) => ChatClient = value ? _assistant : _demo;

    /// <summary>The question went out: the avatar thinks until the answer's first piece arrives.</summary>
    internal void OnSent()
    {
        _speech.Stop();
        Driver = _textDriver;
        _textDriver.Begin();
    }

    /// <summary>A piece of the answer arrived, or the answer ended.</summary>
    internal async void OnStreamed(ChatStreamedEventArgs e)
    {
        if (e.Delta.Length > 0)
            _textDriver.Append(e.Delta);
        if (!e.IsComplete)
            return;
        _textDriver.Complete();
        if (!ReadAloud || e.Text.Length == 0)
            return;
        while (_textDriver.IsBusy)
            await Task.Delay(100);
        Driver = _speech;
        await _speech.SpeakAsync(e.Text);
        if (IsText)
            Driver = _textDriver;
    }

    /// <summary>The conversation: the avatar, the chat view and the edge read it.</summary>
    public IVoiceSession Session { get; }

    /// <summary>The free-standing avatar's driver: the voice session's, or in text mode the text or speech driver.</summary>
    [ObservableProperty]
    public partial AvatarDriver Driver { get; set; }

    public IReadOnlyList<AvatarChoice> Avatars { get; } =
    [
        new("Nova", AvatarSource.FromMauiAsset("Avatars/nova.spineavatar")),
        new("Puff", AvatarSource.FromMauiAsset("Avatars/plush-puff.spineavatar")),
        new("Voice Totem", AvatarSource.FromMauiAsset("Avatars/voice-totem.spineavatar")),
        new("Aurora Flow", AvatarSource.FromMauiAsset("Avatars/aurora-flow.spineavatar")),
    ];

    [ObservableProperty]
    public partial AvatarChoice Avatar { get; set; }

    /// <summary>0: an AvatarView with VoiceAvatarDriver; 1: VoiceView's character.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFree), nameof(IsHosted))]
    public partial int HostIndex { get; set; }

    public bool IsFree => HostIndex == 0;

    public bool IsHosted => HostIndex == 1;

    /// <summary>The push sample server's AI prefix; on a phone, the Mac's address on the network.</summary>
    [ObservableProperty]
    public partial string Server { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Not connected.";

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial bool Muted { get; set; }

    [ObservableProperty]
    public partial bool Edge { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EndCommand), nameof(SendCommand))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsConnecting { get; set; }

    private bool CanConnect => !IsConnected && !IsConnecting;

    public override async Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _clock?.Stop();
        Edge = false;
        await Session.EndAsync();
    }

    partial void OnMutedChanged(bool value) => Session.IsMuted = value;

    partial void OnEdgeChanged(bool value)
    {
        _edge?.Dispose();
        _edge = null;
        if (value && Application.Current?.Windows.FirstOrDefault() is { } window)
        {
            _edge = _highlights.AttachEdge(window);
            _edge.Intensity = () => Session.UpdateFrame().OutputLevel;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect()
    {
        if (!Uri.TryCreate(Server, UriKind.Absolute, out var endpoint))
        {
            Status = "The server address is not a URL.";
            return;
        }

        _options.SessionEndpoint = endpoint;
        IsConnecting = true;
        Status = "Connecting…";
        try
        {
            await Session.ConnectAsync("Voice");
            IsConnected = true;
            _clock ??= Application.Current!.Dispatcher.CreateTimer();
            _clock.Interval = TimeSpan.FromSeconds(1);
            _clock.Tick -= OnClock;
            _clock.Tick += OnClock;
            _clock.Start();
            ShowStatus();
        }
        catch (AIFeatureOffException e)
        {
            Status = $"The server's Voice feature is off ({e.Reason}). Does it have an OpenAI key?";
        }
        catch (AIRateLimitedException e)
        {
            Status = $"Too many sessions; try again in {e.RetryAfter?.TotalSeconds:0} s.";
        }
        catch (Exception e)
        {
            Status = e.Message;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task End()
    {
        _clock?.Stop();
        await Session.EndAsync();
    }

    /// <summary>Typed instead of spoken: the model answers aloud as usual.</summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(Message))
            return;
        var text = Message;
        Message = "";
        await Session.SendTextAsync(text);
    }

    /// <summary>Push-to-talk: the user's turn lasts while the button is held.</summary>
    [RelayCommand]
    private void BeginTurn() => Session.BeginTurn();

    [RelayCommand]
    private void EndTurn() => Session.EndTurn();

    private void OnClock(object? sender, EventArgs e) => ShowStatus();

    private void ShowStatus()
    {
        if (!Session.IsConnected)
            return;
        var left = Session.TimeLeft is { } t ? string.Create(CultureInfo.InvariantCulture, $" · {t:m\\:ss} left") : "";
        Status = $"{Session.State}{left}";
    }
}

/// <summary>An avatar in the picker.</summary>
public sealed record AvatarChoice(string Name, AvatarSource Source)
{
    public override string ToString() => Name;
}
