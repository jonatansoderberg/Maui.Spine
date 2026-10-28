using System.Buffers.Binary;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// Converts PCM16 little-endian mono from one sample rate to another by linear interpolation, piece by piece: the last
/// sample and the position between samples carry over from one call to the next, so a stream resampled in pieces is the
/// stream resampled whole, with no click at a boundary. Enough for speech (24000 ↔ 16000 is 3:2). A trailing odd byte
/// is dropped.
/// </summary>
internal sealed class PcmResampler
{
    private readonly int _from;
    private readonly int _to;
    // Where the next output sample falls, in input samples from the start of the next piece, in units of 1/_to so that
    // every step of _from lands exactly. Between pieces it is never before -1: the last sample kept.
    private long _position;
    private short _last;

    public PcmResampler(int fromRate, int toRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fromRate, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toRate, 0);
        _from = fromRate;
        _to = toRate;
    }

    /// <summary>
    /// The next piece of the stream at the output rate. An output sample that falls between this piece's last input
    /// sample and the next piece's first waits for the next call.
    /// </summary>
    public byte[] Resample(ReadOnlySpan<byte> pcm)
    {
        var count = pcm.Length / 2;
        var end = (long)(count - 1) * _to;
        var outputs = _position < end ? (int)((end - _position + _from - 1) / _from) : 0;
        var output = new byte[outputs * 2];

        for (var k = 0; k < outputs; k++)
        {
            var at = _position + _to;
            var index = (int)(at / _to) - 1;
            var fraction = (double)(at % _to) / _to;
            var a = index < 0 ? _last : BinaryPrimitives.ReadInt16LittleEndian(pcm[(2 * index)..]);
            var b = BinaryPrimitives.ReadInt16LittleEndian(pcm[(2 * index + 2)..]);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(2 * k), (short)Math.Round(a + (b - a) * fraction));
            _position += _from;
        }

        if (count > 0)
        {
            _last = BinaryPrimitives.ReadInt16LittleEndian(pcm[(2 * count - 2)..]);
            _position -= (long)count * _to;
        }
        return output;
    }
}
