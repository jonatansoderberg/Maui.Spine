using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Voice;

namespace MauiSpineSampleApp.Pages.VoiceLab;

public partial class VoiceLabPageViewModel : SampleViewModel
{
    private short[]? _reference;
    private CancellationTokenSource? _test;

    public VoiceLabPageViewModel()
    {
        Audio = IPlatformApplication.Current!.Services.GetRequiredService<IVoiceAudio>();
        Audio.StateChanged += _ => MainThread.BeginInvokeOnMainThread(ShowStatus);
        Audio.RouteChanged += _ => MainThread.BeginInvokeOnMainThread(ShowStatus);
        Audio.Failed += e => MainThread.BeginInvokeOnMainThread(() => Status = e.Message);
    }

    /// <summary>A new engine for this page; the meter view reads its frame.</summary>
    public IVoiceAudio Audio { get; }

    [ObservableProperty]
    public partial bool VoiceProcessing { get; set; } = true;

    [ObservableProperty]
    public partial bool Speaker { get; set; } = true;

    [ObservableProperty]
    public partial bool Muted { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Stopped.";

    [ObservableProperty]
    public partial string Results { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand), nameof(PlayReferenceCommand), nameof(RunEchoTestCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(RunEchoTestCommand))]
    public partial bool IsTesting { get; set; }

    private bool CanStart => !IsRunning && !IsTesting;

    public override async Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _test?.Cancel();
        await Audio.StopAsync();
        IsRunning = false;
    }

    partial void OnMutedChanged(bool value) => Audio.IsMuted = value;

    partial void OnSpeakerChanged(bool value) => Audio.UseSpeaker = value;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        try
        {
            await Audio.StartAsync(new VoiceAudioOptions { VoiceProcessing = VoiceProcessing, PreferSpeaker = Speaker });
            Audio.IsMuted = Muted;
            IsRunning = true;
            ShowStatus();
        }
        catch (VoiceAudioException e)
        {
            Status = e.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task Stop()
    {
        await Audio.StopAsync();
        IsRunning = false;
        ShowStatus();
    }

    /// <summary>Plays the reference while the engine runs: talk over it and watch both meters.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task PlayReference()
    {
        var reference = await ReferenceAsync();
        var bytes = new byte[reference.Length * 2];
        Buffer.BlockCopy(reference, 0, bytes, 0, bytes.Length);
        var item = $"reference-{Guid.NewGuid():N}";
        Audio.Enqueue(item, bytes);
        Audio.Complete(item);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task RunEchoTest()
    {
        IsTesting = true;
        _test = new CancellationTokenSource();
        try
        {
            var result = await EchoTest.RunAsync(Audio, await ReferenceAsync(), Speaker, step => Status = step, _test.Token);
            Results = Describe(result);
            Status = "Done.";
            await SaveAsync(result);
        }
        catch (VoiceAudioException e)
        {
            Status = e.Message;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        finally
        {
            IsTesting = false;
            IsRunning = Audio.IsRunning;
        }
    }

    private void ShowStatus()
    {
        if (!Audio.IsRunning)
        {
            Status = "Stopped.";
            return;
        }
        var processing = Audio.IsVoiceProcessingOn ? "echo cancellation on" : "echo cancellation off";
        Status = $"{Audio.State} · {Audio.Route} · {processing}";
    }

    private async Task<short[]> ReferenceAsync()
    {
        if (_reference is not null)
            return _reference;
        await using var stream = await FileSystem.OpenAppPackageFileAsync("voice-reference.wav");
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return _reference = EchoTest.ReadWav(memory.ToArray());
    }

    private static string Describe(EchoResult r)
    {
        static string Take(EchoTake t) => string.Create(CultureInfo.InvariantCulture,
            $"echo {t.EchoDbfs:0.0} dBFS (loudest {t.LoudestDbfs:0.0}, room {t.NoiseDbfs:0.0}), return loss {t.EchoReturnLossDb:0.0} dB, CPU {t.CpuPercent:0}%{(t.LatencyMs is { } ms ? $", latency {ms:0} ms" : "")}");

        return string.Create(CultureInfo.InvariantCulture, $"""
            {r.Device}, {r.System}, {r.With.Route}
            Reference {r.Without.ReferenceDbfs:0.0} dBFS
            Without echo cancellation: {Take(r.Without)}
            With echo cancellation{(r.With.VoiceProcessingOn ? "" : " (not given)")}: {Take(r.With)}
            Echo cancellation removed {r.ErleDb:0.0} dB.
            """);
    }

    /// <summary>Appends the result to voice-lab.json in the app's data, for fetching from a device.</summary>
    private static async Task SaveAsync(EchoResult result)
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "voice-lab.json");
        var all = File.Exists(path)
            ? JsonSerializer.Deserialize<List<EchoResult>>(await File.ReadAllTextAsync(path)) ?? []
            : [];
        all.Add(result);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }
}
