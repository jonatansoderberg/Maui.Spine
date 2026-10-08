namespace Plugin.Maui.Spine.Images;

/// <summary>
/// BlurHash (<see href="https://blurha.sh"/>): a picture's colours in 20–30 characters, drawn as a soft
/// placeholder while the picture itself loads. The same algorithm as Wolt's reference implementation, with
/// no dependencies, so a server can compute the hash on upload and the app can draw it without the network.
/// </summary>
public static class BlurHash
{
    const string Characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~";

    /// <summary>
    /// Computes the hash of a picture given as RGBA bytes, four per pixel, row by row. Alpha is ignored.
    /// </summary>
    /// <param name="rgba">The pixels, <paramref name="width"/> × <paramref name="height"/> × 4 bytes.</param>
    /// <param name="width">The picture's width in pixels.</param>
    /// <param name="height">The picture's height in pixels.</param>
    /// <param name="componentsX">Detail across, 1–9. Four is common for a landscape picture.</param>
    /// <param name="componentsY">Detail down, 1–9. Three is common for a landscape picture.</param>
    /// <remarks>
    /// The cost grows with the pixel count times the components; scale the picture down to about 100 pixels
    /// on its longest side first, which gives the same hash for practical purposes.
    /// </remarks>
    public static string Encode(ReadOnlySpan<byte> rgba, int width, int height, int componentsX = 4, int componentsY = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(componentsX, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(componentsX, 9);
        ArgumentOutOfRangeException.ThrowIfLessThan(componentsY, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(componentsY, 9);
        if (rgba.Length < width * height * 4)
            throw new ArgumentException($"{width} × {height} pixels need {width * height * 4} bytes of RGBA; got {rgba.Length}.", nameof(rgba));

        var linear = new double[width * height * 3];
        for (int i = 0, o = 0; o < linear.Length; i += 4, o += 3)
        {
            linear[o] = ToLinear(rgba[i]);
            linear[o + 1] = ToLinear(rgba[i + 1]);
            linear[o + 2] = ToLinear(rgba[i + 2]);
        }

        var factors = new double[componentsX * componentsY * 3];
        var cosX = new double[width];
        var cosY = new double[height];
        for (var j = 0; j < componentsY; j++)
        {
            for (var y = 0; y < height; y++)
                cosY[y] = Math.Cos(Math.PI * j * y / height);

            for (var i = 0; i < componentsX; i++)
            {
                for (var x = 0; x < width; x++)
                    cosX[x] = Math.Cos(Math.PI * i * x / width);

                double r = 0, g = 0, b = 0;
                for (var y = 0; y < height; y++)
                {
                    var row = y * width * 3;
                    for (var x = 0; x < width; x++)
                    {
                        var basis = cosX[x] * cosY[y];
                        var p = row + x * 3;
                        r += basis * linear[p];
                        g += basis * linear[p + 1];
                        b += basis * linear[p + 2];
                    }
                }

                var scale = (i == 0 && j == 0 ? 1d : 2d) / (width * height);
                var f = (j * componentsX + i) * 3;
                factors[f] = r * scale;
                factors[f + 1] = g * scale;
                factors[f + 2] = b * scale;
            }
        }

        var hash = new char[4 + 2 * componentsX * componentsY];
        Write(hash, 0, 1, componentsX - 1 + (componentsY - 1) * 9);

        double maximum;
        if (factors.Length > 3)
        {
            var actual = 0d;
            for (var k = 3; k < factors.Length; k++)
                actual = Math.Max(actual, Math.Abs(factors[k]));
            var quantised = (int)Math.Max(0, Math.Min(82, Math.Floor(actual * 166 - 0.5)));
            maximum = (quantised + 1) / 166d;
            Write(hash, 1, 1, quantised);
        }
        else
        {
            maximum = 1;
            Write(hash, 1, 1, 0);
        }

        Write(hash, 2, 4, (ToSrgb(factors[0]) << 16) + (ToSrgb(factors[1]) << 8) + ToSrgb(factors[2]));
        for (var k = 1; k < componentsX * componentsY; k++)
        {
            var quantR = Quantise(factors[k * 3] / maximum);
            var quantG = Quantise(factors[k * 3 + 1] / maximum);
            var quantB = Quantise(factors[k * 3 + 2] / maximum);
            Write(hash, 4 + (k - 1) * 2 + 2, 2, quantR * 19 * 19 + quantG * 19 + quantB);
        }

        return new string(hash);

        static int Quantise(double value) => (int)Math.Max(0, Math.Min(18, Math.Floor(SignPow(value, 0.5) * 9 + 9.5)));
    }

    /// <summary>
    /// Draws <paramref name="hash"/> as RGBA bytes, four per pixel, row by row, fully opaque. A placeholder
    /// needs few pixels: 32 × 32 scaled up by the view looks the same as a full-size decode.
    /// </summary>
    /// <param name="hash">A BlurHash string.</param>
    /// <param name="width">The width to draw at, in pixels.</param>
    /// <param name="height">The height to draw at, in pixels.</param>
    /// <param name="punch">Contrast: 1 is the picture's own; higher makes the colours stronger.</param>
    /// <exception cref="FormatException"><paramref name="hash"/> is not a BlurHash.</exception>
    public static byte[] Decode(string hash, int width, int height, double punch = 1)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (!TryReadSize(hash, out var componentsX, out var componentsY))
            throw new FormatException($"\"{hash}\" is not a BlurHash: its length does not match the size in its first character, or it has characters outside base 83.");

        var maximum = (Read(hash, 1, 1) + 1) / 166d * punch;
        var colors = new double[componentsX * componentsY * 3];
        var dc = Read(hash, 2, 4);
        colors[0] = ToLinear(dc >> 16);
        colors[1] = ToLinear((dc >> 8) & 255);
        colors[2] = ToLinear(dc & 255);
        for (var k = 1; k < componentsX * componentsY; k++)
        {
            var value = Read(hash, 4 + k * 2, 2);
            colors[k * 3] = SignPow((value / (19 * 19) - 9) / 9d, 2) * maximum;
            colors[k * 3 + 1] = SignPow((value / 19 % 19 - 9) / 9d, 2) * maximum;
            colors[k * 3 + 2] = SignPow((value % 19 - 9) / 9d, 2) * maximum;
        }

        var cosX = new double[width * componentsX];
        for (var x = 0; x < width; x++)
            for (var i = 0; i < componentsX; i++)
                cosX[x * componentsX + i] = Math.Cos(Math.PI * x * i / width);
        var cosY = new double[height * componentsY];
        for (var y = 0; y < height; y++)
            for (var j = 0; j < componentsY; j++)
                cosY[y * componentsY + j] = Math.Cos(Math.PI * y * j / height);

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double r = 0, g = 0, b = 0;
                for (var j = 0; j < componentsY; j++)
                {
                    for (var i = 0; i < componentsX; i++)
                    {
                        var basis = cosX[x * componentsX + i] * cosY[y * componentsY + j];
                        var c = (j * componentsX + i) * 3;
                        r += colors[c] * basis;
                        g += colors[c + 1] * basis;
                        b += colors[c + 2] * basis;
                    }
                }

                var p = (y * width + x) * 4;
                pixels[p] = (byte)ToSrgb(r);
                pixels[p + 1] = (byte)ToSrgb(g);
                pixels[p + 2] = (byte)ToSrgb(b);
                pixels[p + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>Whether <paramref name="hash"/> is a well-formed BlurHash.</summary>
    public static bool IsValid(string? hash) => hash is not null && TryReadSize(hash, out _, out _);

    static bool TryReadSize(string hash, out int componentsX, out int componentsY)
    {
        componentsX = componentsY = 0;
        if (hash.Length < 6 || hash.Any(static c => !Characters.Contains(c)))
            return false;

        var size = Read(hash, 0, 1);
        componentsX = size % 9 + 1;
        componentsY = size / 9 + 1;
        return hash.Length == 4 + 2 * componentsX * componentsY;
    }

    static int Read(string hash, int start, int length)
    {
        var value = 0;
        for (var k = start; k < start + length; k++)
            value = value * 83 + Characters.IndexOf(hash[k]);
        return value;
    }

    static void Write(char[] hash, int start, int length, int value)
    {
        for (var k = length - 1; k >= 0; k--)
        {
            hash[start + k] = Characters[value % 83];
            value /= 83;
        }
    }

    static double ToLinear(int value)
    {
        var v = value / 255d;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    static int ToSrgb(double value)
    {
        var v = Math.Clamp(value, 0, 1);
        return v <= 0.0031308
            ? (int)(v * 12.92 * 255 + 0.5)
            : (int)((1.055 * Math.Pow(v, 1 / 2.4) - 0.055) * 255 + 0.5);
    }

    static double SignPow(double value, double exponent) => Math.CopySign(Math.Pow(Math.Abs(value), exponent), value);
}
