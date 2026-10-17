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
    private AvatarView? _view;

    private readonly IVoiceAudio _audio;
    private readonly VoiceAvatarDriver _listen;
    private short[]? _reference;

    public AvatarDetailPageViewModel(ISpineSpeech speech, IVoiceAudio audio)
    {
        _voices = speech;
        _speech = new SpeechAvatarDriver(speech);
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
    public partial string ListenStatus { get; set; } = "The engine is off.";

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
    public partial string Text { get; set; } = "Hello! I am an avatar, and this text is mouthed from its letters, with no audio.";

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
        return _audio.StopAsync();
    }

    internal void Attach(AvatarView view) => _view = view;

    partial void OnDriverIndexChanged(int value)
    {
        _text.Clear();
        _speech.Stop();
        Driver = value switch { 1 => _text, 2 => _speech, 3 => _listen, _ => null };
        if (value != 3 && _audio.IsRunning)
            _ = StopEngine();
        if (value == 2 && Voices.Count == 0)
            _ = LoadVoicesAsync();
    }

    private async Task LoadVoicesAsync()
    {
        if (!_voices.IsSupported)
        {
            SpeechStatus = "This platform has no text-to-speech.";
            return;
        }
        var voices = await _voices.GetVoicesAsync();
        Voices = [.. voices.Select(v => new VoiceChoice(v))];
        var language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        Voice = Voices.FirstOrDefault(v => v.Voice.Language.Replace('_', '-').Equals(language, StringComparison.OrdinalIgnoreCase))
            ?? Voices.FirstOrDefault(v => v.Voice.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            ?? Voices.FirstOrDefault();
        SpeechStatus = $"{Voices.Count} voices installed.";
    }

    /// <summary>Reads <see cref="Text"/> aloud in the chosen voice; each word's mouth starts as the voice reaches it.</summary>
    [RelayCommand]
    private async Task Speak()
    {
        if (string.IsNullOrWhiteSpace(Text))
            return;
        var words = 0;
        var started = DateTime.Now;
        SpeechStatus = "Starting the voice…";
        var done = _speech.SpeakAsync(Text, new SpeakOptions { VoiceId = Voice?.Voice.Id, Language = Voice?.Voice.Language });
        if (_speech.Current is { } utterance)
            utterance.Word += (_, word) => SpeechStatus = $"Word {++words} at {word.At.TotalSeconds:0.00} s: {Text.Substring(word.Start, word.Length)}";
        var reason = await done;
        SpeechStatus = $"{reason} after {words} words in {(DateTime.Now - started).TotalSeconds:0.0} s.";
    }

    [RelayCommand]
    private void StopSpeaking() => _speech.Stop();

    /// <summary>Opens the microphone through Spine.Voice's engine: Listening with its level, no provider.</summary>
    [RelayCommand]
    private async Task StartEngine()
    {
        try
        {
            await _audio.StartAsync(new VoiceAudioOptions());
            ListenStatus = "Listening: talk and the avatar follows your level.";
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
        ListenStatus = "Playing the reference answer.";
    }

    [RelayCommand]
    private async Task StopEngine()
    {
        await _audio.StopAsync();
        ListenStatus = "The engine is off.";
    }

    private static async Task<short[]> ReadReferenceAsync()
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync("voice-reference.wav");
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return MauiSpineSampleApp.Pages.VoiceLab.EchoTest.ReadWav(memory.ToArray());
    }

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

/// <summary>A voice in the picker: its name, language and quality.</summary>
public sealed record VoiceChoice(SpeechVoice Voice)
{
    public override string ToString() => $"{Voice.Name} ({Voice.Language}{(Voice.Quality == SpeechVoiceQuality.Default ? "" : ", " + Voice.Quality)})";
}
