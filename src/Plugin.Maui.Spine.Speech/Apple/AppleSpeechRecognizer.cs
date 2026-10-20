#if IOS || MACCATALYST
using System.Runtime.InteropServices;
using AVFoundation;
using Foundation;
using Speech;

namespace Plugin.Maui.Spine.Speech;

/// <summary><c>SFSpeechRecognizer</c> fed with buffers, so it hears the microphone the voice engine already has open.</summary>
internal sealed class AppleSpeechRecognizer : ISpineSpeechRecognizer
{
    private const string UsageKey = "NSSpeechRecognitionUsageDescription";

    public bool IsSupported => true;

    public bool IsOnDevice(string? language = null) => Recognizer(language) is { SupportsOnDeviceRecognition: true };

    public Task<bool> RequestPermissionAsync(CancellationToken cancellationToken = default)
    {
        // Without the key iOS ends the app at the request, so the missing key is said here instead.
        if (NSBundle.MainBundle.ObjectForInfoDictionary(UsageKey) is null)
            throw new InvalidOperationException($"The app's Info.plist has no {UsageKey}; add one saying what speech recognition is for.");

        if (SFSpeechRecognizer.AuthorizationStatus != SFSpeechRecognizerAuthorizationStatus.NotDetermined)
            return Task.FromResult(SFSpeechRecognizer.AuthorizationStatus == SFSpeechRecognizerAuthorizationStatus.Authorized);

        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SFSpeechRecognizer.RequestAuthorization(status => answer.TrySetResult(status == SFSpeechRecognizerAuthorizationStatus.Authorized));
        return answer.Task.WaitAsync(cancellationToken);
    }

    public ISpeechRecognition Start(RecognitionOptions? options = null)
    {
        options ??= new();
        if (SFSpeechRecognizer.AuthorizationStatus != SFSpeechRecognizerAuthorizationStatus.Authorized)
            throw new InvalidOperationException($"Speech recognition is {SFSpeechRecognizer.AuthorizationStatus}; call RequestPermissionAsync first.");
        var recognizer = Recognizer(options.Language)
            ?? throw new InvalidOperationException($"iOS has no speech recognizer for {options.Language ?? "the device's language"}.");
        if (options.OnDeviceOnly && !recognizer.SupportsOnDeviceRecognition)
            throw new InvalidOperationException($"{recognizer.Locale.Identifier} cannot be recognised on this device, and OnDeviceOnly is set.");
        return new Recognition(recognizer, options);
    }

    private static SFSpeechRecognizer? Recognizer(string? language) =>
        language is null ? new SFSpeechRecognizer() : new SFSpeechRecognizer(new NSLocale(language.Replace('-', '_')));

    private sealed class Recognition : ISpeechRecognition
    {
        private readonly SFSpeechAudioBufferRecognitionRequest _request;
        private readonly SFSpeechRecognitionTask _task;
        private readonly AVAudioFormat _format;
        private readonly TaskCompletionSource<string> _words = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _heard = "";
        private bool _ended;

        public Recognition(SFSpeechRecognizer recognizer, RecognitionOptions options)
        {
            _format = new AVAudioFormat(AVAudioCommonFormat.PCMFloat32, options.SampleRate, 1, false);
            _request = new SFSpeechAudioBufferRecognitionRequest
            {
                ShouldReportPartialResults = true,
                RequiresOnDeviceRecognition = options.OnDeviceOnly || recognizer.SupportsOnDeviceRecognition,
                AddsPunctuation = true,
                TaskHint = SFSpeechRecognitionTaskHint.Dictation,
            };
            if (options.Phrases.Count > 0)
                _request.ContextualStrings = [.. options.Phrases];
            _task = recognizer.GetRecognitionTask(_request, OnResult);
        }

        public event Action<string>? Partial;

        public unsafe void Append(ReadOnlySpan<byte> pcm16)
        {
            var samples = MemoryMarshal.Cast<byte, short>(pcm16);
            if (_ended || samples.Length == 0)
                return;
            var buffer = new AVAudioPcmBuffer(_format, (uint)samples.Length) { FrameLength = (uint)samples.Length };
            var floats = new Span<float>(((float**)buffer.FloatChannelData)[0], samples.Length);
            for (var i = 0; i < samples.Length; i++)
                floats[i] = samples[i] / 32768f;
            _request.Append(buffer);
        }

        public Task<string> FinishAsync(CancellationToken cancellationToken = default)
        {
            if (!_ended)
            {
                _ended = true;
                _request.EndAudio();
            }
            return _words.Task.WaitAsync(cancellationToken);
        }

        public void Cancel()
        {
            _ended = true;
            _words.TrySetResult("");
            _task.Cancel();
        }

        public void Dispose() => Cancel();

        private void OnResult(SFSpeechRecognitionResult? result, NSError? error)
        {
            if (result is not null)
            {
                _heard = result.BestTranscription.FormattedString;
                if (result.Final)
                {
                    _words.TrySetResult(_heard);
                    return;
                }
                Partial?.Invoke(_heard);
            }

            if (error is null)
                return;
            // 1110 is "no speech detected"; after a cancel the error is only the cancel.
            if (_words.Task.IsCompleted || error.Code == 1110)
                _words.TrySetResult(_heard);
            else
                _words.TrySetException(new SpeechRecognitionException($"Speech recognition failed ({error.Domain} {error.Code}): {error.LocalizedDescription}"));
        }
    }
}
#endif
