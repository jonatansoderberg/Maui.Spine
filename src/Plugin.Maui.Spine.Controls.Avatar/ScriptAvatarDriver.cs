using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Speech;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Plays an <see cref="AvatarScript"/>: each step in turn, <c>Say</c> through a <see cref="SpeechAvatarDriver"/> when the app
/// has Spine.Speech and the step asks to speak, through a <see cref="TextAvatarDriver"/> otherwise. Its quality is that of
/// the driver saying the current line. <see cref="AvatarView.PlayAsync"/> makes one for each script.
/// </summary>
public sealed class ScriptAvatarDriver : AvatarDriver
{
    private readonly ISpineSpeech? _speech;
    private readonly TextAvatarDriver _text = new();
    private SpeechAvatarDriver? _voice;
    private AvatarDriver? _saying;
    private TaskCompletionSource? _mouthed;
    private TaskCompletionSource? _tapped;
    private string? _line;
    private int _shown = -1;
    private SpeechWord? _lastWord;

    /// <summary>A driver that speaks through <paramref name="speech"/>, or only mouths when it is null or unsupported.</summary>
    public ScriptAvatarDriver(ISpineSpeech? speech = null)
    {
        _speech = speech is { IsSupported: true } ? speech : null;
        LipSyncQuality = AvatarLipSyncQuality.None;
    }

    /// <summary>The part of the current line said so far, for a caption; raised on the main thread as it grows.</summary>
    public event EventHandler<string>? CaptionChanged;

    /// <summary>Ends a <c>WaitForTap</c> step, as a tap on the avatar does.</summary>
    public void Continue() => _tapped?.TrySetResult();

    /// <summary>Plays <paramref name="script"/> on <paramref name="view"/>, which this driver must be driving.</summary>
    public async Task RunAsync(AvatarScript script, AvatarView view, CancellationToken cancellationToken = default)
    {
        using var registration = cancellationToken.Register(StopSaying);
        foreach (var step in script.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (step)
            {
                case AvatarScriptStep.SetState s:
                    Input.SetState(s.State);
                    break;
                case AvatarScriptStep.Say s:
                    await SayAsync(s.Text, s.Speak, cancellationToken);
                    break;
                case AvatarScriptStep.Express e:
                    Input.SetExpression(e.Request);
                    break;
                case AvatarScriptStep.Gesture g:
                    await Task.Delay(Input.PlayGesture(g.Name), cancellationToken);
                    break;
                case AvatarScriptStep.LookAtView l:
                    if (SpineTargets.TryGetFrame(l.Target, out var frame))
                        LookAtWindow(view, frame.Center);
                    break;
                case AvatarScriptStep.LookAtPoint p:
                    LookAtWindow(view, p.WindowPoint);
                    break;
                case AvatarScriptStep.LookBack:
                    view.LookAt(null);
                    break;
                case AvatarScriptStep.Wait w:
                    await Task.Delay(w.Delay, cancellationToken);
                    break;
                case AvatarScriptStep.WaitForTap:
                    _tapped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    await _tapped.Task.WaitAsync(cancellationToken);
                    _tapped = null;
                    break;
            }
        }
    }

    /// <inheritdoc />
    protected internal override void OnDetached()
    {
        StopSaying();
        Use(null);
    }

    /// <inheritdoc />
    protected internal override void OnInterrupted() => _saying?.Interrupted();

    /// <inheritdoc />
    protected internal override void OnTick(TimeSpan delta)
    {
        if (_saying is not { } saying)
            return;
        saying.Tick(delta);
        LipSyncQuality = saying.LipSyncQuality;

        if (saying == _text && _line is not null)
        {
            // The text driver mouths at an even pace: the caption keeps up with it.
            Caption(_text.Length > 0 ? (int)Math.Ceiling(_line.Length * Math.Min(1, _text.Position / _text.Length)) : 0);
            if (!_text.IsBusy)
                _mouthed?.TrySetResult();
        }
        else if (_lastWord is { } word)
        {
            Caption(word.Start + word.Length);
        }
    }

    private async Task SayAsync(string text, bool speak, CancellationToken cancellationToken)
    {
        _line = text;
        _shown = -1;
        _lastWord = null;
        Caption(0);
        if (speak && _speech is not null)
        {
            _voice ??= new SpeechAvatarDriver(_speech);
            Use(_voice);
            var done = _voice.SpeakAsync(text, cancellationToken: cancellationToken);
            if (_voice.Current is { } utterance)
                utterance.Word += (_, w) => _lastWord = w;
            await done;
        }
        else
        {
            Use(_text);
            _mouthed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _text.Mouth(text);
            await _mouthed.Task.WaitAsync(cancellationToken);
        }
        Caption(text.Length);
    }

    private void Use(AvatarDriver? driver)
    {
        if (ReferenceEquals(driver, _saying))
            return;
        _saying?.Detach();
        _saying = driver;
        if (driver is not null && IsAttached)
            driver.Attach(Input);
    }

    private void StopSaying()
    {
        _text.Clear();
        _voice?.Stop();
        _mouthed?.TrySetCanceled();
    }

    private void Caption(int length)
    {
        if (_line is null)
            return;
        length = Math.Clamp(length, 0, _line.Length);
        if (length == _shown)
            return;
        _shown = length;
        CaptionChanged?.Invoke(this, _line[..length]);
    }

    // The direction to a point on screen, from where the avatar is on screen.
    private static void LookAtWindow(AvatarView view, Point windowPoint)
    {
        if (SpineTargets.TryGetFrame(view, out var own))
            view.LookAt(new Point(windowPoint.X - own.X, windowPoint.Y - own.Y));
    }
}
