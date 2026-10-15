namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// A streaming windowed-sinc resampler for mono audio: chunks go in as they come, and the output keeps its phase across
/// chunks. Its delay is <c>halfTaps</c> input samples.
/// </summary>
public sealed class PcmResampler
{
    private readonly double _step;
    private readonly int _halfTaps;
    private readonly double _cutoff;
    private const int Phases = 256;

    private readonly float[] _kernel;
    private float[] _history;
    private int _historyLength;
    private double _position;

    /// <param name="inputRate">The rate of the samples going in.</param>
    /// <param name="outputRate">The rate wanted out.</param>
    /// <param name="halfTaps">Input samples on each side of an output sample; more is cleaner and slower.</param>
    public PcmResampler(int inputRate, int outputRate, int halfTaps = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inputRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(outputRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(halfTaps, 2);

        InputRate = inputRate;
        OutputRate = outputRate;
        _step = inputRate / (double)outputRate;
        _halfTaps = halfTaps;
        // Below the lower Nyquist, with a margin for the window's slope.
        _cutoff = Math.Min(1.0, outputRate / (double)inputRate) * 0.92;
        _history = new float[4 * halfTaps];

        // The kernel tabled once at Phases points per input sample: the audio thread then interpolates instead of
        // computing a sine and a cosine for every tap of every sample.
        _kernel = new float[(2 * halfTaps) * Phases + 1];
        for (var i = 0; i < _kernel.Length; i++)
            _kernel[i] = (float)Kernel(i / (double)Phases - halfTaps);
        Reset();
    }

    /// <summary>The input's rate.</summary>
    public int InputRate { get; }

    /// <summary>The output's rate.</summary>
    public int OutputRate { get; }

    /// <summary>The most samples <see cref="Process"/> can write for <paramref name="inputLength"/> samples in.</summary>
    public int MaxOutput(int inputLength) => (int)Math.Ceiling((inputLength + 1) / _step) + 2;

    /// <summary>Starts over, as after a gap in the audio.</summary>
    public void Reset()
    {
        Array.Clear(_history);
        // Starts on silence so the first output sample has a full window behind it.
        _historyLength = _halfTaps;
        _position = 0;
    }

    /// <summary>Resamples <paramref name="input"/> into <paramref name="output"/>.</summary>
    /// <returns>How many samples were written; at most <see cref="MaxOutput"/>.</returns>
    public int Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (InputRate == OutputRate)
        {
            input.CopyTo(output);
            return input.Length;
        }

        var written = 0;
        var consumed = 0;
        while (true)
        {
            // An output sample at _position (in history coordinates) needs input up to _position + _halfTaps.
            var needed = (int)Math.Floor(_position) + _halfTaps + 1;
            while (_historyLength < needed && consumed < input.Length)
                Push(input[consumed++]);
            if (_historyLength < needed)
                break;

            output[written++] = Sample(_position);
            _position += _step;

            // Keeps the history short: drops what no later sample reaches.
            var drop = (int)Math.Floor(_position) - _halfTaps;
            if (drop > _history.Length / 2)
            {
                Array.Copy(_history, drop, _history, 0, _historyLength - drop);
                _historyLength -= drop;
                _position -= drop;
            }
        }

        return written;
    }

    private void Push(float sample)
    {
        if (_historyLength == _history.Length)
        {
            var drop = Math.Clamp((int)Math.Floor(_position) - _halfTaps, 0, _historyLength);
            if (drop > 0)
            {
                Array.Copy(_history, drop, _history, 0, _historyLength - drop);
                _historyLength -= drop;
                _position -= drop;
            }
            else
            {
                Array.Resize(ref _history, _history.Length * 2);
            }
        }
        _history[_historyLength++] = sample;
    }

    private float Sample(double position)
    {
        var center = (int)Math.Floor(position);
        var fraction = position - center;
        float sum = 0;
        for (var k = -_halfTaps + 1; k <= _halfTaps; k++)
        {
            var index = center + k;
            if (index < 0 || index >= _historyLength)
                continue;
            // x = k - fraction, in table steps from -halfTaps.
            var t = (k - fraction + _halfTaps) * Phases;
            var i = (int)t;
            if (i < 0 || i >= _kernel.Length - 1)
                continue;
            var w = (float)(t - i);
            sum += _history[index] * (_kernel[i] + w * (_kernel[i + 1] - _kernel[i]));
        }
        return sum;
    }

    private double Kernel(double x)
    {
        var sinc = Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * _cutoff * x) / (Math.PI * x);
        var window = 0.5 + 0.5 * Math.Cos(Math.PI * x / _halfTaps);
        return Math.Abs(x) >= _halfTaps ? 0 : (Math.Abs(x) < 1e-9 ? _cutoff : sinc) * window;
    }
}
