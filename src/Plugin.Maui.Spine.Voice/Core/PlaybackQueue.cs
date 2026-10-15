namespace Plugin.Maui.Spine.Voice;

/// <summary>Where playback of an answer stood when it was cut off.</summary>
/// <param name="ItemId">The answer's item, as the provider named it.</param>
/// <param name="Played">How much of it was played. OpenAI's truncate takes it as <c>audio_end_ms</c>.</param>
public readonly record struct PlaybackPosition(string ItemId, TimeSpan Played);

/// <summary>
/// The answer's audio, queued per item as it arrives (faster than real time) and read by the player as it plays. An
/// item starts only once it has a prebuffer or is complete, so a slow network does not stutter its first syllables;
/// <see cref="Flush"/> drops everything at once and says how far the playing item got.
/// </summary>
internal sealed class PlaybackQueue(int sampleRate, TimeSpan? prebuffer = null)
{
    private readonly int _prebuffer = (int)(sampleRate * (prebuffer ?? TimeSpan.FromMilliseconds(100)).TotalSeconds);
    private readonly LinkedList<Item> _items = [];
    private readonly Dictionary<string, long> _played = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private Item? _current;

    public int SampleRate => sampleRate;

    /// <summary>Whether anything is queued or playing.</summary>
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
                return _current is not null || _items.Count > 0;
        }
    }

    /// <summary>Queues PCM16 samples for <paramref name="itemId"/>, after what is already queued for it.</summary>
    public void Enqueue(string itemId, ReadOnlySpan<short> samples)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        if (samples.IsEmpty)
            return;

        lock (_gate)
        {
            var item = Find(itemId);
            if (item is null)
            {
                item = new Item(itemId);
                _items.AddLast(item);
            }
            item.Append(samples);
        }
    }

    /// <summary>Says no more audio comes for <paramref name="itemId"/>, so a short answer plays without its prebuffer.</summary>
    public void Complete(string itemId)
    {
        lock (_gate)
        {
            if (Find(itemId) is { } item)
                item.Complete = true;
        }
    }

    /// <summary>Fills <paramref name="destination"/> with what plays next, and silence where nothing is ready.</summary>
    /// <returns>How many samples were real audio rather than silence.</returns>
    public int Read(Span<short> destination)
    {
        lock (_gate)
        {
            var filled = 0;
            while (filled < destination.Length)
            {
                // An item is done when it is complete, or when the provider has moved on to a later one.
                if (_current is null || _current.Remaining == 0 && (_current.Complete || _items.Last!.Value != _current))
                {
                    if (_current is not null)
                        _items.Remove(_current);
                    _current = _items.First?.Value;
                    if (_current is null)
                        break;
                    _current.Started = false;
                }

                if (!_current.Started && _current.Remaining < _prebuffer && !_current.Complete)
                    break;
                _current.Started = true;

                var taken = _current.Take(destination[filled..]);
                if (taken == 0)
                    break;

                _played[_current.Id] = _played.GetValueOrDefault(_current.Id) + taken;
                filled += taken;
            }

            destination[filled..].Clear();
            return filled;
        }
    }

    /// <summary>How much of <paramref name="itemId"/> has played.</summary>
    public TimeSpan Played(string itemId)
    {
        lock (_gate)
            return TimeSpan.FromSeconds(_played.GetValueOrDefault(itemId) / (double)sampleRate);
    }

    /// <summary>Drops everything queued. Returns where the playing item stood, or <see langword="null"/> when nothing played.</summary>
    public PlaybackPosition? Flush()
    {
        lock (_gate)
        {
            PlaybackPosition? position = _current is { Started: true } playing
                ? new PlaybackPosition(playing.Id, TimeSpan.FromSeconds(_played.GetValueOrDefault(playing.Id) / (double)sampleRate))
                : null;
            _items.Clear();
            _current = null;
            return position;
        }
    }

    /// <summary>Forgets the played counts, as for a new conversation.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _items.Clear();
            _current = null;
            _played.Clear();
        }
    }

    private Item? Find(string itemId)
    {
        for (var node = _items.Last; node is not null; node = node.Previous)
        {
            if (node.Value.Id == itemId)
                return node.Value;
        }
        return null;
    }

    private sealed class Item(string id)
    {
        private readonly Queue<short[]> _chunks = new();
        private int _offset;

        public string Id { get; } = id;
        public bool Complete { get; set; }
        public bool Started { get; set; }
        public int Remaining { get; private set; }

        public void Append(ReadOnlySpan<short> samples)
        {
            _chunks.Enqueue(samples.ToArray());
            Remaining += samples.Length;
        }

        public int Take(Span<short> destination)
        {
            var taken = 0;
            while (taken < destination.Length && _chunks.TryPeek(out var chunk))
            {
                var n = Math.Min(destination.Length - taken, chunk.Length - _offset);
                chunk.AsSpan(_offset, n).CopyTo(destination[taken..]);
                taken += n;
                _offset += n;
                if (_offset == chunk.Length)
                {
                    _chunks.Dequeue();
                    _offset = 0;
                }
            }
            Remaining -= taken;
            return taken;
        }
    }
}
