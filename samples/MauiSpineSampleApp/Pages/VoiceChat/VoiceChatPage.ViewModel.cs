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
            Status = error is null ? $"Ended: {reason}." : $"Ended: {error.Message}";
        };
        Driver = new VoiceAvatarDriver(Session);
    }

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
