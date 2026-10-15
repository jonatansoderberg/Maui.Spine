using Microsoft.Extensions.DependencyInjection;
using MauiSpineSampleApp.Pages.VoiceLab;
using Plugin.Maui.Spine.Voice;

namespace MauiSpineSampleApp.Pages.VoiceVisuals;

public partial class VoiceVisualsPageViewModel : SampleViewModel
{
    private short[]? _reference;
    private IDispatcherTimer? _repeat;

    public VoiceVisualsPageViewModel()
    {
        Audio = IPlatformApplication.Current!.Services.GetRequiredService<IVoiceAudio>();
        Audio.Failed += e => MainThread.BeginInvokeOnMainThread(() => Status = e.Message);
    }

    /// <summary>One engine for all four views; each reads the same frame.</summary>
    public IVoiceAudio Audio { get; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Stopped.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    public partial bool IsRunning { get; set; }

    public override async Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _repeat?.Stop();
        await Audio.StopAsync();
        IsRunning = false;
    }

    [RelayCommand(CanExecute = nameof(IsNotRunning))]
    private async Task Start()
    {
        try
        {
            _reference ??= await ReadReferenceAsync();
            await Audio.StartAsync(new VoiceAudioOptions());
            IsRunning = true;
            Status = "Playing the reference; talk to see the microphone.";
            Play();
            _repeat ??= Application.Current!.Dispatcher.CreateTimer();
            _repeat.Interval = TimeSpan.FromSeconds(1);
            _repeat.Tick -= OnRepeat;
            _repeat.Tick += OnRepeat;
            _repeat.Start();
        }
        catch (VoiceAudioException e)
        {
            Status = e.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task Stop()
    {
        _repeat?.Stop();
        await Audio.StopAsync();
        IsRunning = false;
        Status = "Stopped.";
    }

    private bool IsNotRunning => !IsRunning;

    // The reference again each time it ends, so the views always have an answer to show.
    private void OnRepeat(object? sender, EventArgs e)
    {
        if (Audio.IsRunning && !Audio.IsPlaying)
            Play();
    }

    private void Play()
    {
        if (_reference is null)
            return;
        var bytes = new byte[_reference.Length * 2];
        Buffer.BlockCopy(_reference, 0, bytes, 0, bytes.Length);
        var item = $"reference-{Guid.NewGuid():N}";
        Audio.Enqueue(item, bytes);
        Audio.Complete(item);
    }

    private static async Task<short[]> ReadReferenceAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("voice-reference.wav");
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return EchoTest.ReadWav(memory.ToArray());
    }
}
