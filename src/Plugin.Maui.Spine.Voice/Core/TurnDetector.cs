using System.Runtime.InteropServices;

namespace Plugin.Maui.Spine.Voice;

/// <summary>What the microphone's latest audio changed about the user's turn.</summary>
internal enum TurnEdge
{
    None,
    Started,
    Ended,
}

/// <summary>
/// Voice activity on the microphone by its level, in windows of 20 ms: speech for <c>start</c> begins a turn, quiet for
/// <c>end</c> ends it. The threshold rises over the room's noise floor and while an answer plays.
/// </summary>
internal sealed class TurnDetector(int rate, float threshold, TimeSpan start, TimeSpan end, TimeSpan maxTurn)
{
    private readonly float _floor = threshold;
    private readonly int _window = rate / 50;
    private readonly int _startWindows = Math.Max(1, (int)(start.TotalMilliseconds / 20));
    private readonly int _endWindows = Math.Max(1, (int)(end.TotalMilliseconds / 20));
    private readonly int _maxWindows = Math.Max(1, (int)(maxTurn.TotalMilliseconds / 20));
    private readonly float[] _held = new float[rate / 50];
    private int _heldCount;
    private int _loud;
    private int _quiet;
    private int _turnWindows;
    private float _noise = threshold / 4;

    public bool InTurn { get; private set; }

    /// <summary>The level the last window was judged against.</summary>
    public float Threshold { get; private set; } = threshold;

    /// <summary>Ends the turn without waiting for quiet, as push-to-talk does.</summary>
    public void Reset()
    {
        InTurn = false;
        _loud = _quiet = _turnWindows = 0;
    }

    /// <summary>Starts a turn now, as push-to-talk does.</summary>
    public void Begin()
    {
        InTurn = true;
        _loud = _quiet = _turnWindows = 0;
    }

    /// <param name="pcm16">The microphone's audio.</param>
    /// <param name="answerPlaying">Whether an answer plays, whose echo must not count as the user.</param>
    /// <returns>The edge this audio crossed, the last one when it crossed several.</returns>
    public TurnEdge Feed(ReadOnlySpan<byte> pcm16, bool answerPlaying)
    {
        var edge = TurnEdge.None;
        foreach (var sample in MemoryMarshal.Cast<byte, short>(pcm16))
        {
            _held[_heldCount++] = sample / 32768f;
            if (_heldCount < _window)
                continue;
            _heldCount = 0;
            if (Window(answerPlaying) is var crossed and not TurnEdge.None)
                edge = crossed;
        }
        return edge;
    }

    private TurnEdge Window(bool answerPlaying)
    {
        var sum = 0.0;
        foreach (var s in _held)
            sum += s * s;
        var rms = (float)Math.Sqrt(sum / _held.Length);

        Threshold = Math.Max(_floor, _noise * 4) * (answerPlaying ? 2 : 1);
        var loud = rms >= Threshold;
        // The room's level, from the quiet between turns only, and slowly; an answer's echo is not the room.
        if (!InTurn && !loud && !answerPlaying)
            _noise += (rms - _noise) * 0.05f;

        if (!InTurn)
        {
            _loud = loud ? _loud + 1 : 0;
            if (_loud < _startWindows)
                return TurnEdge.None;
            Begin();
            return TurnEdge.Started;
        }

        _turnWindows++;
        _quiet = loud ? 0 : _quiet + 1;
        if (_quiet < _endWindows && _turnWindows < _maxWindows)
            return TurnEdge.None;
        Reset();
        return TurnEdge.Ended;
    }
}

/// <summary>
/// Cuts a streamed answer into sentences to read aloud as each is complete, without the markdown a chat model writes.
/// </summary>
internal sealed class SentenceSplitter
{
    private readonly System.Text.StringBuilder _text = new();

    /// <summary>Adds text and returns the sentences it completed.</summary>
    public List<string> Append(string text)
    {
        _text.Append(text);
        var sentences = new List<string>();
        var from = 0;
        for (var i = 0; i < _text.Length; i++)
        {
            var c = _text[i];
            if (c != '\n' && !(c is '.' or '!' or '?' or '…' or ':' or ';' && EndsSentence(i)))
                continue;
            if (Clean(_text.ToString(from, i + 1 - from)) is { Length: > 0 } sentence)
                sentences.Add(sentence);
            from = i + 1;
        }
        _text.Remove(0, from);
        return sentences;
    }

    // Punctuation followed by space ends a sentence when what comes next starts one: not "kl. 18" or "t.ex. en",
    // and not before the next word has arrived.
    private bool EndsSentence(int at)
    {
        if (at + 1 >= _text.Length || !char.IsWhiteSpace(_text[at + 1]))
            return false;
        for (var i = at + 1; i < _text.Length; i++)
        {
            if (_text[i] == '\n')
                return true;
            if (!char.IsWhiteSpace(_text[i]))
                return _text[at] != '.' || !(char.IsLower(_text[i]) || char.IsDigit(_text[i]));
        }
        return false;
    }

    /// <summary>What is left once the answer has ended.</summary>
    public string? Flush()
    {
        var rest = Clean(_text.ToString());
        _text.Clear();
        return rest.Length > 0 ? rest : null;
    }

    private static string Clean(string text)
    {
        var cleaned = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is not ('*' or '#' or '`' or '_' or '|'))
                cleaned.Append(c);
        }
        var line = cleaned.ToString().Trim();
        // A list's bullet is not read out.
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("• ", StringComparison.Ordinal))
            line = line[2..].TrimStart();
        return line.Any(char.IsLetterOrDigit) ? line : "";
    }
}
