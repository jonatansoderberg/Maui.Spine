using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Controls;
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
        Driver = new VoiceAvatarDriver(Session);
        Avatar = Avatars[0];
    }

    /// <summary>The conversation: the avatar, the chat view and the edge read it.</summary>
    public IVoiceSession Session { get; }

    /// <summary>The free-standing avatar's driver; VoiceView's character makes its own.</summary>
    public VoiceAvatarDriver Driver { get; }

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
