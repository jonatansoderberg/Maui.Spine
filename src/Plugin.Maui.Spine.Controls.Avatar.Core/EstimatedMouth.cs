namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// A mouth with no audio to measure: visemes on a timeline (guessed from letters, or timed by a voice), laid down a little
/// ahead of the playhead, and a level and bands guessed from the viseme under it. The Text and Speech drivers share it.
/// </summary>
internal sealed class EstimatedMouth
{
    private const double Lookahead = 1;

    private readonly List<AvatarTextVisemes.Estimate> _timeline = [];
    private readonly float[] _bands = new float[AvatarRenderFrame.BandCount];
    private int _next, _current;

    /// <summary>Where the last viseme ends, in the timeline's seconds.</summary>
    public double End { get; private set; }

    /// <summary>How strongly the mouth shapes show, 0–1.</summary>
    public float Strength { get; init; } = 0.8f;

    public void Clear()
    {
        _timeline.Clear();
        _next = _current = 0;
        End = 0;
    }

    /// <summary>Visemes guessed from <paramref name="text"/>, starting at <paramref name="start"/>.</summary>
    public void AddText(string text, double start, double charactersPerSecond)
    {
        foreach (var estimate in AvatarTextVisemes.FromText(text, charactersPerSecond))
            Add(estimate.Viseme, start + estimate.Start, estimate.Duration);
    }

    /// <summary>One viseme at a time on the timeline; later ones need not come in order.</summary>
    public void Add(int viseme, double start, double duration)
    {
        var index = _timeline.Count;
        while (index > _next && _timeline[index - 1].Start > start)
            index--;
        _timeline.Insert(index, new(start, duration, viseme));
        End = Math.Max(End, start + duration);
    }

    /// <summary>Lays down the visemes that start within a second of <paramref name="position"/>.</summary>
    public void Push(AvatarInput input, long generation, double position)
    {
        while (_next < _timeline.Count && _timeline[_next].Start <= position + Lookahead)
        {
            var e = _timeline[_next++];
            if (e.Start + e.Duration > position)
                input.EnqueueViseme(generation, TimeSpan.FromSeconds(e.Start), TimeSpan.FromSeconds(e.Duration), e.Viseme, Strength);
        }
    }

    /// <summary>Starts laying down again from <paramref name="position"/>: for a new avatar, whose scheduler has none of them.</summary>
    public void Rewind(double position)
    {
        _next = _current = 0;
        while (_next < _timeline.Count && _timeline[_next].Start + _timeline[_next].Duration <= position)
            _next++;
    }

    /// <summary>Sets the output level and bands from the viseme under <paramref name="position"/>; zero past the end. Returns the level.</summary>
    public float Sound(AvatarInput input, double position)
    {
        if (_timeline.Count == 0 || position >= End || position < _timeline[0].Start)
        {
            input.SetOutputLevel(0);
            return 0;
        }
        _current = Math.Min(_current, _timeline.Count - 1);
        while (_current > 0 && _timeline[_current].Start > position)
            _current--;
        while (_current < _timeline.Count - 1 && _timeline[_current].Start + _timeline[_current].Duration <= position)
            _current++;
        var viseme = _timeline[_current].Viseme;
        var level = AvatarTextVisemes.Level(viseme);
        AvatarTextVisemes.Bands(viseme, level, position, _bands);
        input.SetOutputLevel(level, _bands);
        return level;
    }
}
