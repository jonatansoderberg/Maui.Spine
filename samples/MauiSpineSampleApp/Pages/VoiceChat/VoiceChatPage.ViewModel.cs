using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Voice;
using MauiSpineSampleApp.Pages.Avatars;

namespace MauiSpineSampleApp.Pages.VoiceChat;

public partial class VoiceChatPageViewModel : SampleViewModel
{
    private readonly SpineVoiceOptions _options;
    private readonly IHighlights _highlights;
    private IDispatcherTimer? _clock;
    private IEdgeGlow? _edge;

    public VoiceChatPageViewModel()
    {
        var services = IPlatformApplication.Current!.Services;
        _options = services.GetRequiredService<SpineVoiceOptions>();
        _highlights = services.GetRequiredService<IHighlights>();
        Server = _options.SessionEndpoint?.ToString() ?? "http://localhost:5100/ai";

        // A new session for this page: the transcript, the state and the audio are its own.
        Session = services.GetRequiredService<IVoiceSession>();
        Session.StateChanged += _ => ShowStatus();
        Session.Ended += (reason, error) =>
        {
            IsConnected = false;
            _clock?.Stop();
            Status = error is not null ? $"Ended: {error.Message}"
                : reason == VoiceSessionEnd.Goodbye ? "You said goodbye, so it hung up."
                : $"Ended: {reason}.";
        };
        Driver = new VoiceAvatarDriver(Session);
        // On by default here: "tack, hej då" ends the call, as with a person.
        Session.EndsOnGoodbye = true;
        // A simulator has no echo cancellation: the Mac's speaker reaches its microphone, the model hears its own answer
        // as the user talking over it, and cuts it off. Not sending the microphone while it speaks avoids that.
        if (DeviceInfo.DeviceType == DeviceType.Virtual)
            Session.Duplex = VoiceDuplex.HalfWhileSpeaking;

        var avatars = new ChoiceGroup("Avatar", [.. VoiceAvatars.All.Select(a => new Choice(a.Name, a.Name switch
        {
            "Bars" or "Ring" => "Built into Spine: the answer's sound one way, the microphone's the other.",
            _ => "A character or ambient avatar following the conversation: its state, the levels and the mouth.",
        }, () => Avatar = a))]);
        avatars.Select(0);
        _tryOptions = [
            avatars,
            new ToggleOption("Hang up after goodbye", "Say goodbye (\"tack, hej då\") and the session ends once the answer has been said.", () => Session.EndsOnGoodbye, value => Session.EndsOnGoodbye = value),
            new ToggleOption("Glow the screen's edge", "The window's edge glows with the answer's level (Spine.Controls.Highlight).", () => Edge, value => Edge = value),
            new TextOption("Server", "The push sample server's AI prefix; on a phone, the Mac's address on the network or the Azure one.", () => Server, value => Server = value, Microsoft.Maui.Keyboard.Url),
        ];

        // The chat takes what the screen has left under the page's header, the example's title and info and the controls.
        var display = DeviceDisplay.MainDisplayInfo;
        ChatHeight = Math.Max(300, display.Height / Math.Max(1, display.Density) - 470);
    }

    private readonly IReadOnlyList<ObservableObject> _tryOptions;

    /// <summary>How tall the chat is: most of the screen.</summary>
    public double ChatHeight { get; }

    [RelayCommand]
    private Task ShowOptions() => ShowOptionsAsync("Voice chat", _tryOptions);

    /// <summary>One button for the call: Connect, or End while it runs.</summary>
    public string CallText => IsConnected ? "End" : IsConnecting ? "Connecting…" : "Connect";

    [RelayCommand]
    private Task Call() => IsConnected ? End() : IsConnecting ? Task.CompletedTask : Connect();

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(CallText));

    partial void OnIsConnectingChanged(bool value) => OnPropertyChanged(nameof(CallText));

    [RelayCommand]
    private void ToggleMute() => Muted = !Muted;

    partial void OnMutedChanged(bool value)
    {
        Session.IsMuted = value;
        OnPropertyChanged(nameof(MuteText));
    }

    public string MuteText => Muted ? "Unmute" : "Mute";

    /// <summary>The conversation; the meter and the chat view read it.</summary>
    public IVoiceSession Session { get; }

    /// <summary>The push sample server's AI prefix; on a phone, the Mac's address on the network.</summary>
    [ObservableProperty]
    public partial string Server { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Not connected.";

    [ObservableProperty]
    public partial bool Muted { get; set; }

    /// <summary>What draws the conversation: the built-in bars or ring, or a character.</summary>
    public IReadOnlyList<AvatarChoice> Avatars => VoiceAvatars.All;

    [ObservableProperty]
    public partial AvatarChoice Avatar { get; set; } = VoiceAvatars.All[0];

    /// <summary>The avatar follows the session: its state, the levels and bands each way, the mouth on the answer.</summary>
    public AvatarDriver Driver { get; }

    [ObservableProperty]
    public partial bool Edge { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(EndCommand), nameof(StopCommand))]
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
            Status = e.Reason switch
            {
                AIOffReason.SignInRequired => "The server's Voice feature answers signed-in users only; sign in first.",
                AIOffReason.Budget => "Today's Voice budget is spent; try again tomorrow.",
                _ => $"The server's Voice feature is off ({e.Reason}). Does it have an OpenAI key?",
            };
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

    /// <summary>Cuts the answer off, as talking over it would.</summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void Stop() => Session.Interrupt();

    private void OnClock(object? sender, EventArgs e) => ShowStatus();

    private void ShowStatus()
    {
        if (!Session.IsConnected)
            return;
        var left = Session.TimeLeft is { } t ? string.Create(CultureInfo.InvariantCulture, $" · {t:m\\:ss} left") : "";
        Status = $"{Session.State}{left}";
    }
}
