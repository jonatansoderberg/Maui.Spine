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

    public bool IsSupported => true;

    public async IAsyncEnumerable<SpeechAudio> RenderAsync(string text, SpeakOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var render = new Render();
        using var _ = cancellationToken.Register(render.Stop);
        render.Start(AppleSpineSpeech.Native(text, options ?? new()));
        try
        {
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
            render.Stop();
        }
    }

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern unsafe void WriteUtterance(IntPtr receiver, IntPtr selector, IntPtr utterance, BlockLiteral* callback);

    [UnmanagedCallersOnly]
    private static void OnBuffer(IntPtr block, IntPtr buffer) => BlockLiteral.GetTarget<Action<IntPtr>>(block)?.Invoke(buffer);

    // One synthesizer per render: its delegate says when the utterance is done, and it is kept alive until then.
    private sealed class Render : NSObject, IAVSpeechSynthesizerDelegate
    {
        private readonly AVSpeechSynthesizer _synthesizer = new();
        private readonly Channel<SpeechAudio> _pieces = Channel.CreateUnbounded<SpeechAudio>(new() { SingleReader = true });

        public Render() => _synthesizer.Delegate = this;

        public ChannelReader<SpeechAudio> Pieces => _pieces.Reader;

        public unsafe void Start(AVSpeechUtterance utterance)
        {
            Action<IntPtr> callback = OnBuffer;
            delegate* unmanaged<IntPtr, IntPtr, void> trampoline = &AppleSpeechRenderer.OnBuffer;
            // The synthesizer copies the block, so this one is released when the call returns.
            using var block = new BlockLiteral(trampoline, callback, typeof(AppleSpeechRenderer), nameof(AppleSpeechRenderer.OnBuffer));
            WriteUtterance(_synthesizer.Handle, WriteSelector, utterance.Handle, &block);
        }

        public void Stop()
        {
            if (_pieces.Writer.TryComplete())
                _synthesizer.StopSpeaking(AVSpeechBoundary.Immediate);
        }

        private unsafe void OnBuffer(IntPtr handle)
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

        // The empty buffer ends a render; this is the fallback for a system that does not send it. Not at once, since the
        // order of the two is not documented and the last buffers could still be on their way.
        [Export("speechSynthesizer:didFinishSpeechUtterance:")]
        public void DidFinishSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) =>
            _ = Task.Delay(500).ContinueWith(_ => _pieces.Writer.TryComplete(), TaskScheduler.Default);

        [Export("speechSynthesizer:didCancelSpeechUtterance:")]
        public void DidCancelSpeechUtterance(AVSpeechSynthesizer synthesizer, AVSpeechUtterance utterance) => _pieces.Writer.TryComplete();
    }
}
#endif
