using System.Diagnostics;

namespace Plugin.Maui.Spine.Controls;

/// <summary>Frame timing for an inspector: how long rendering takes and what it allocates.</summary>
internal sealed class AvatarFrameStats
{
    private readonly double[] _times = new double[240];
    private readonly long[] _ticks = new long[240];
    private int _count, _next;

    public long Frames { get; private set; }

    /// <summary>When the first frame was drawn (<see cref="Stopwatch"/> ticks), 0 until then.</summary>
    public long FirstFrameTimestamp { get; private set; }

    /// <summary>Managed bytes allocated by the last rendered frame on the UI thread.</summary>
    public long LastAllocatedBytes { get; private set; }

    public long MaxAllocatedBytes { get; private set; }

    public void Record(double milliseconds, long allocatedBytes)
    {
        _times[_next] = milliseconds;
        _ticks[_next] = Stopwatch.GetTimestamp();
        _next = (_next + 1) % _times.Length;
        _count = Math.Min(_count + 1, _times.Length);
        Frames++;
        if (Frames == 1)
            FirstFrameTimestamp = _ticks[(_next - 1 + _times.Length) % _times.Length];
        LastAllocatedBytes = allocatedBytes;
        if (Frames > 30)
            MaxAllocatedBytes = Math.Max(MaxAllocatedBytes, allocatedBytes);
    }

    public double Percentile(double p)
    {
        if (_count == 0)
            return 0;
        Span<double> sorted = stackalloc double[_count];
        _times.AsSpan(0, _count).CopyTo(sorted);
        sorted.Sort();
        return sorted[Math.Min(_count - 1, (int)(p * _count))];
    }

    /// <summary>Frames per second over the recorded window.</summary>
    public double FramesPerSecond
    {
        get
        {
            if (_count < 2)
                return 0;
            var newest = _ticks[(_next - 1 + _times.Length) % _times.Length];
            var oldest = _ticks[_count < _times.Length ? 0 : _next];
            var seconds = Stopwatch.GetElapsedTime(oldest, newest).TotalSeconds;
            return seconds > 0 ? (_count - 1) / seconds : 0;
        }
    }
}
