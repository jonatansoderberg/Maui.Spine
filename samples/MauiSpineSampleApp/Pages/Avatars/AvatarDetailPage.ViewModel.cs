using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Speech;
using Plugin.Maui.Spine.Voice;

namespace MauiSpineSampleApp.Pages.Avatars;

public partial class AvatarDetailPageViewModel : SampleViewModel, IReceivesNavigationParameter<AvatarEntry>
{
    private static readonly int[] Sizes = [64, 128, 320];
    private static readonly int[] FrameRates = [15, 30, 60];

    private readonly TextAvatarDriver _text = new();
    private readonly SpeechAvatarDriver _speech;
    private readonly ISpineSpeech _voices;
    private readonly ServerSpeech _server;
    private readonly SpeechAvatarDriver _serverSpeech;
    private AvatarView? _view;

    private readonly IVoiceAudio _audio;
    private readonly VoiceAvatarDriver _listen;
    private short[]? _reference;

    public AvatarDetailPageViewModel(ISpineSpeech speech, IVoiceAudio audio, ServerSpeech server)
    {
        _voices = speech;
        _speech = new SpeechAvatarDriver(speech);
        _server = server;
        _serverSpeech = new SpeechAvatarDriver(server);
        _server.Failed += e => SpeechStatus = $"The server voice failed: {e.Message}";
        _audio = audio;
        _listen = new VoiceAvatarDriver(audio);
    }

    public IReadOnlyList<AvatarState> States { get; } = Enum.GetValues<AvatarState>();

    public IReadOnlyList<AvatarExpression> Expressions { get; } = Enum.GetValues<AvatarExpression>();

    [ObservableProperty]
    public partial AvatarEntry? Entry { get; set; }

    /// <summary>0 drives the avatar by hand, 1 mouths text, 2 reads it aloud.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManual), nameof(IsText), nameof(IsSpeak), nameof(IsListen))]
    public partial int DriverIndex { get; set; }

    public bool IsListen => DriverIndex == 3;

    [ObservableProperty]
    public partial string ListenStatus { get; set; } = "Tap Listen and talk, or tap Play an answer.";

    /// <summary>The microphone's level, 0 to 1, while the engine runs.</summary>
    [ObservableProperty]
    public partial double InputLevel { get; set; }

    /// <summary>The answer's level, 0 to 1, as it plays.</summary>
    [ObservableProperty]
    public partial double OutputLevel { get; set; }

    [ObservableProperty]
    public partial bool IsEngineRunning { get; set; }

    private IDispatcherTimer? _meter;

    // The meters read the engine's frame, which the avatar's driver updates on every tick.
    private void Meter(bool on)
    {
        IsEngineRunning = on;
        if (_meter is null)
        {
            _meter = Application.Current!.Dispatcher.CreateTimer();
            _meter.Interval = TimeSpan.FromMilliseconds(66);
            _meter.Tick += (_, _) =>
            {
                var frame = _audio.Frame;
                InputLevel = frame.InputLevel;
                OutputLevel = frame.OutputLevel;
                ListenStatus = frame.State switch
                {
                    VoiceState.Speaking => "Speaking: the answer plays and the mouth follows its level.",
                    _ when frame.InputLevel > 0.35f => "Listening: you are talking, and the avatar leans in with your level.",
                    _ => "Listening: the microphone is open. Talk and watch the meter and the avatar.",
                };
            };
        }
        if (on)
            _meter.Start();
        else
        {
            _meter.Stop();
            InputLevel = OutputLevel = 0;
        }
    }

    public bool IsManual => DriverIndex == 0;

    public bool IsText => DriverIndex == 1;

    public bool IsSpeak => DriverIndex == 2;

    /// <summary>The installed voices, best first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<VoiceChoice> Voices { get; set; } = [];

    [ObservableProperty]
    public partial VoiceChoice? Voice { get; set; }

    [ObservableProperty]
    public partial string SpeechStatus { get; set; } = "";

    [ObservableProperty]
    public partial AvatarDriver? Driver { get; set; }

    [ObservableProperty]
    public partial AvatarState State { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial AvatarExpression Expression { get; set; }

    [ObservableProperty]
    public partial AvatarExpression TimedExpression { get; set; } = AvatarExpression.Happy;

    [ObservableProperty]
    public partial double Intensity { get; set; } = 0.9;

    [ObservableProperty]
    public partial double DurationSeconds { get; set; } = 3;

    [ObservableProperty]
    public partial string Text { get; set; } = "Hello! I am an avatar. Pick a voice and I read this aloud; on the Text tab I only move my mouth.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Size))]
    public partial int SizeIndex { get; set; } = 2;

    public int Size => Sizes[SizeIndex];

    /// <summary>System, Reduced, Full.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotionMode))]
    public partial int MotionIndex { get; set; }

    public AvatarMotionMode MotionMode => (AvatarMotionMode)MotionIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameRate))]
    public partial int FrameRateIndex { get; set; } = 1;

    public int FrameRate => FrameRates[FrameRateIndex];

    public Task OnNavigationParameterAsync(AvatarEntry entry)
    {
        Entry = entry;
        return Task.CompletedTask;
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _text.Clear();
        _speech.Stop();
        _serverSpeech.Stop();
        _try?.Cancel();
        Meter(false);
        return _audio.StopAsync();
    }

    internal void Attach(AvatarView view) => _view = view;

    partial void OnDriverIndexChanged(int value)
    {
        _text.Clear();
        _speech.Stop();
        _serverSpeech.Stop();
        Driver = value switch { 1 => _text, 2 => SpeechDriver, 3 => _listen, _ => null };
        if (value != 3 && _audio.IsRunning)
            _ = StopEngine();
        if (value == 2 && Voices.Count == 0)
            _ = LoadVoicesAsync();
    }

    // The server's speech features come after the installed voices: the sample server's demo voice needs no key, and
    // ReadAzure is Azure Speech once its key is set there.
    private static readonly VoiceChoice[] ServerVoices =
    [
        new(null, "Read", "Server: demo voice (a hum, mouth shapes from the server)"),
        new(null, "ReadAzure", "Server: Azure Speech (only where the server has its key)"),
    ];

    private SpeechAvatarDriver SpeechDriver => Voice?.Feature is null ? _speech : _serverSpeech;

    partial void OnVoiceChanged(VoiceChoice? value)
    {
        if (DriverIndex != 2)
            return;
        _speech.Stop();
        _serverSpeech.Stop();
        Driver = SpeechDriver;
    }

    private async Task LoadVoicesAsync()
    {
        if (!_voices.IsSupported)
        {
            Voices = ServerVoices;
            Voice = Voices[0];
            SpeechStatus = "This platform has no text-to-speech of its own; the server's voices remain.";
            return;
        }
        var voices = await _voices.GetVoicesAsync();
        Voices = [.. voices.Select(v => new VoiceChoice(v)), .. ServerVoices];
        var language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        // English first: the sample text is English (a Swedish voice read it with a Swedish accent).
        Voice = Voices.FirstOrDefault(v => v.Voice is not null && v.Voice.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? Voices.FirstOrDefault(v => v.Voice is not null && v.Voice.Language.Replace('_', '-').Equals(language, StringComparison.OrdinalIgnoreCase))
            ?? Voices.FirstOrDefault();
        SpeechStatus = $"{Voices.Count - ServerVoices.Length} voices on this device, and the server's. Tap Speak.";
    }

    /// <summary>Reads <see cref="Text"/> aloud in the chosen voice; each word's mouth starts as the voice reaches it.</summary>
    [RelayCommand]
    private async Task Speak()
    {
        if (string.IsNullOrWhiteSpace(Text))
            return;
        var words = 0;
        var visemes = 0;
        var started = DateTime.Now;
        var driver = SpeechDriver;
        if (Voice?.Feature is { } feature)
            _server.Feature = feature;
        Driver = driver;
        SpeechStatus = Voice?.Feature is null ? "Starting the voice…" : "Asking the server…";
        var done = driver.SpeakAsync(Text, new SpeakOptions { VoiceId = Voice?.Voice?.Id, Language = Voice?.Voice?.Language });
        if (driver.Current is { } utterance)
        {
            utterance.Word += (_, word) => SpeechStatus = $"Word {++words} at {word.At.TotalSeconds:0.00} s: {Text.Substring(word.Start, word.Length)}";
            utterance.Viseme += (_, _) => visemes++;
            utterance.Started += (_, _) => SpeechStatus = visemes > 0 ? $"Speaking, {visemes} visemes from the server ({driver.LipSyncQuality})." : "Speaking.";
        }
        var reason = await done;
        if (reason == SpeechEndReason.Failed)
            SpeechStatus = Voice?.Feature is null ? "The voice could not start; pick another one." : SpeechStatus.StartsWith("The server", StringComparison.Ordinal) ? SpeechStatus : "The server voice failed; check the connection or pick a device voice.";
        else if (words == 0 && visemes == 0 && reason == SpeechEndReason.Completed)
            SpeechStatus = "The voice finished without saying anything; pick another one.";
        else
            SpeechStatus = $"{reason} after {(visemes > 0 ? $"{visemes} mouth shapes from the server" : $"{words} words")} in {(DateTime.Now - started).TotalSeconds:0.0} s.";
    }

    [RelayCommand]
    private void StopSpeaking()
    {
        _speech.Stop();
        _serverSpeech.Stop();
    }

    /// <summary>Opens the microphone through Spine.Voice's engine: Listening with its level, no provider.</summary>
    [RelayCommand]
    private async Task StartEngine()
    {
        try
        {
            await _audio.StartAsync(new VoiceAudioOptions());
            Meter(true);
        }
        catch (VoiceAudioException e)
        {
            ListenStatus = e.Message;
        }
    }

    /// <summary>Plays the Voice lab's reference speech through the engine, as an answer would: Speaking on its level.</summary>
    [RelayCommand]
    private async Task PlayReference()
    {
        if (!_audio.IsRunning)
            await StartEngine();
        if (!_audio.IsRunning)
            return;
        _reference ??= await ReadReferenceAsync();
        var bytes = new byte[_reference.Length * 2];
        Buffer.BlockCopy(_reference, 0, bytes, 0, bytes.Length);
        var item = $"reference-{Guid.NewGuid():N}";
        _audio.Enqueue(item, bytes);
        _audio.Complete(item);
    }

    [RelayCommand]
    private async Task StopEngine()
    {
        await _audio.StopAsync();
        Meter(false);
        ListenStatus = "Stopped: the microphone is closed.";
    }

    private static async Task<short[]> ReadReferenceAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("voice-reference.wav");
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return MauiSpineSampleApp.Pages.VoiceLab.EchoTest.ReadWav(memory.ToArray());
    }

    /// <summary>What the Try buttons are showing, step by step.</summary>
    [ObservableProperty]
    public partial string TryStatus { get; set; } = "";

    private CancellationTokenSource? _try;

    private static readonly Dictionary<AvatarState, string> StateMeaning = new()
    {
        [AvatarState.Idle] = "at rest: breathing, blinking, glancing about",
        [AvatarState.Connecting] = "a conversation is being set up",
        [AvatarState.Listening] = "hearing the user; with a microphone it follows the level",
        [AvatarState.Thinking] = "working on an answer, the gaze turned inward",
        [AvatarState.Speaking] = "talking; the mouth moves with the voice",
        [AvatarState.Interrupted] = "cut short by the user: a quick reflex, then back",
        [AvatarState.Muted] = "the conversation is muted",
    };

    private static readonly Dictionary<AvatarExpression, string> ExpressionMeaning = new()
    {
        [AvatarExpression.Neutral] = "the resting face",
        [AvatarExpression.Happy] = "pleased",
        [AvatarExpression.Curious] = "interested, asking",
        [AvatarExpression.Thinking] = "pondering",
        [AvatarExpression.Concerned] = "worried, careful",
        [AvatarExpression.Surprised] = "startled",
        [AvatarExpression.Apologetic] = "sorry",
        [AvatarExpression.Confident] = "sure of itself",
    };

    // One demonstration at a time: a new one, or leaving the page, cancels the running one.
    private async Task Demonstrate(Func<CancellationToken, Task> steps)
    {
        _try?.Cancel();
        using var run = _try = new CancellationTokenSource();
        try
        {
            await steps(run.Token);
            TryStatus = "Done. Pick any of them yourself above.";
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_try == run)
                _try = null;
        }
    }

    /// <summary>Plays every state for a moment, saying what each is for.</summary>
    [RelayCommand]
    private Task TryStates() => Demonstrate(async token =>
    {
        foreach (var state in States)
        {
            State = state;
            TryStatus = $"{state}: {StateMeaning[state]}.";
            await Task.Delay(2200, token);
        }
        State = AvatarState.Idle;
    });

    /// <summary>Shows every expression over the current state.</summary>
    [RelayCommand]
    private Task TryExpressions() => Demonstrate(async token =>
    {
        foreach (var expression in Expressions)
        {
            _view?.SetExpression(new(expression, 0.9f, TimeSpan.FromSeconds(1.8)));
            TryStatus = $"{expression}: {ExpressionMeaning[expression]}.";
            await Task.Delay(2000, token);
        }
    });

    /// <summary>Listening with the microphone-off badge on, then off.</summary>
    [RelayCommand]
    private Task TryMute() => Demonstrate(async token =>
    {
        State = AvatarState.Listening;
        IsMuted = true;
        TryStatus = "Microphone off: the avatar shows that the user's microphone is muted (a badge where the avatar has one). It opens and mutes nothing itself.";
        await Task.Delay(3500, token);
        IsMuted = false;
        TryStatus = "Microphone on again.";
        await Task.Delay(1500, token);
        State = AvatarState.Idle;
    });

    [RelayCommand]
    private void ShowExpression() =>
        _view?.SetExpression(new(TimedExpression, (float)Intensity, TimeSpan.FromSeconds(DurationSeconds)));

    [RelayCommand]
    private Task Gesture(string name) => _view?.PlayGestureAsync(name) ?? Task.CompletedTask;

    [RelayCommand]
    private void Interrupt() => _view?.Interrupt();

    [RelayCommand]
    private void Mouth()
    {
        if (!string.IsNullOrWhiteSpace(Text))
            _text.Mouth(Text);
    }

    /// <summary>A reply streamed in pieces, as a chat answer arrives: Thinking first, then each piece as it comes.</summary>
    [RelayCommand]
    private async Task Stream(CancellationToken cancellationToken)
    {
        _text.Clear();
        _text.Begin();
        await Task.Delay(900, cancellationToken);
        foreach (var piece in Text.Split(' '))
        {
            _text.Append(piece + " ");
            await Task.Delay(Random.Shared.Next(60, 220), cancellationToken);
        }
        _text.Complete();
    }

    [RelayCommand]
    private void Stop() => _text.Clear();
}

/// <summary>A voice in the picker: an installed voice with its name, language and quality, or a speech feature on the server.</summary>
public sealed record VoiceChoice(SpeechVoice? Voice, string? Feature = null, string? Label = null)
{
    public override string ToString() => Label
        ?? $"{Voice!.Name} ({Voice.Language}{(Voice.Quality == SpeechVoiceQuality.Default ? "" : ", " + Voice.Quality)})";
}
