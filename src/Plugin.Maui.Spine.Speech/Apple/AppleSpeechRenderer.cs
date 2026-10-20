#if IOS || MACCATALYST
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using AVFoundation;
using Foundation;
using ObjCRuntime;

namespace Plugin.Maui.Spine.Speech;

/// <summary><c>AVSpeechSynthesizer.write(_:toBufferCallback:)</c>, which .NET for iOS does not bind, called through a block.</summary>
internal sealed class AppleSpeechRenderer : ISpineSpeechRenderer
{
    private static readonly IntPtr WriteSelector = Selector.GetHandle("writeUtterance:toBufferCallback:");
    private static readonly TimeSpan FirstAudio = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NextAudio = TimeSpan.FromSeconds(1);

    // One synthesizer for every render, one render at a time: a new synthesizer per sentence took about 0.7 s to give its
    // first audio on an iPhone 16 Pro, which a voice conversation hears as a pause before each answer.
    private readonly SemaphoreSlim _one = new(1, 1);
    private Synthesizer? _synthesizer;

    public bool IsSupported => true;

    public async IAsyncEnumerable<SpeechAudio> RenderAsync(string text, SpeakOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        await _one.WaitAsync(cancellationToken);
        Render? render = null;
        try
        {
            render = (_synthesizer ??= new Synthesizer()).Start(AppleSpineSpeech.Native(text, options ?? new()));
            using var _ = cancellationToken.Register(render.Stop);
            // The end is the empty buffer (or the delegate); a render that goes quiet without either ends anyway, rather
            // than holding up the sentences after it.
            for (var first = true; ; first = false)
            {
                bool more;
                using (var quiet = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    quiet.CancelAfter(first ? FirstAudio : NextAudio);
                    try
                    {
                        more = await render.Pieces.WaitToReadAsync(quiet.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        more = false;
                    }
                }
                if (!more)
                    yield break;
                while (render.Pieces.TryRead(out var piece))
                    yield return piece;
            }
        }
        finally
        {
            render?.Stop();
            _one.Release();
        }
    }

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern unsafe void WriteUtterance(IntPtr receiver, IntPtr selector, IntPtr utterance, BlockLiteral* callback);

    [UnmanagedCallersOnly]
    private static void OnBuffer(IntPtr block, IntPtr buffer) => BlockLiteral.GetTarget<Action<IntPtr>>(block)?.Invoke(buffer);

    // The shared synthesizer; its delegate ends the render whose utterance it reports.
    private sealed class Synthesizer : NSObject, IAVSpeechSynthesizerDelegate
    {
        private readonly AVSpeechSynthesizer _native = new();
        private Render? _current;

        public Synthesizer() => _native.Delegate = this;

        public unsafe Render Start(AVSpeechUtterance utterance)
        {
            var render = new Render(utterance, _native);
            _current = render;
            Action<IntPtr> callback = render.OnBuffer;
            delegate* unmanaged<IntPtr, IntPtr, void> trampoline = &AppleSpeechRenderer.OnBuffer;
            // The synthesizer copies the block, so this one is released when the call returns.
            using var block = new BlockLiteral(trampoline, callback, typeof(AppleSpeechRenderer), nameof(AppleSpeechRenderer.OnBuffer));
            WriteUtterance(_native.Handle, WriteSelector, utterance.Handle, &block);
            return render;
        }

        private Render? For(AVSpeechUtterance utterance) => _current is { } render && render.Utterance == utterance ? render : null;

        // The empty buffer ends a render; this is the fallback for a system that does not send it. Not at once, since the
        // order of the two is not documented and the last buffers could still be on their way.
        [Export("speechSynthesizer:didFinishSpeechUtterance:")]
        public void DidFinishSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance)
        {
            if (For(utterance) is { } render)
                _ = Task.Delay(500).ContinueWith(_ => render.Complete(), TaskScheduler.Default);
        }

        [Export("speechSynthesizer:didCancelSpeechUtterance:")]
        public void DidCancelSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => For(utterance)?.Complete();
    }

    // One utterance's audio. Buffers from an utterance that was stopped arrive at its own completed channel, not the next's.
    private sealed class Render(AVSpeechUtterance utterance, AVSpeechSynthesizer synthesizer)
    {
        private readonly Channel<SpeechAudio> _pieces = Channel.CreateUnbounded<SpeechAudio>(new() { SingleReader = true });

        public AVSpeechUtterance Utterance { get; } = utterance;

        public ChannelReader<SpeechAudio> Pieces => _pieces.Reader;

        public void Complete() => _pieces.Writer.TryComplete();

        public void Stop()
        {
            if (_pieces.Writer.TryComplete())
                synthesizer.StopSpeaking(AVSpeechBoundary.Immediate);
        }

        public unsafe void OnBuffer(IntPtr handle)
        {
            // The last buffer is empty: the utterance is rendered.
            if (Runtime.GetNSObject<AVAudioPcmBuffer>(handle) is not { FrameLength: > 0 } buffer)
            {
                _pieces.Writer.TryComplete();
                return;
            }

            var frames = (int)buffer.FrameLength;
            var pcm = new byte[frames * 2];
            var samples = MemoryMarshal.Cast<byte, short>(pcm.AsSpan());
            switch (buffer.Format.CommonFormat)
            {
                case AVAudioCommonFormat.PCMInt16:
                    new ReadOnlySpan<short>(((short**)buffer.Int16ChannelData)[0], frames).CopyTo(samples);
                    break;
                case AVAudioCommonFormat.PCMFloat32:
                    var floats = new ReadOnlySpan<float>(((float**)buffer.FloatChannelData)[0], frames);
                    for (var i = 0; i < frames; i++)
                        samples[i] = (short)(Math.Clamp(floats[i], -1f, 1f) * short.MaxValue);
                    break;
                default:
                    _pieces.Writer.TryComplete(new InvalidOperationException($"The voice rendered {buffer.Format.CommonFormat}, which Spine cannot convert."));
                    return;
            }
            _pieces.Writer.TryWrite(new SpeechAudio(pcm, (int)buffer.Format.SampleRate));
        }
    }
}
#endif
