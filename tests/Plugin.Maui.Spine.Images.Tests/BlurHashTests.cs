using Xunit;

namespace Plugin.Maui.Spine.Images.Tests;

// The expected values come from Wolt's TypeScript implementation (the npm package blurhash 2.0.5), run on
// the same pixels. Two quirks of that package are not copied: it scales the AC components by their largest
// value rather than their largest absolute value (Wolt's C and Swift encoders, which this follows, take the
// absolute value), and it ORs the punch with 1. So the encoder is compared where the two agree (all AC
// components positive, or none), and the punch at an odd value.
public class BlurHashTests
{
    const string Reference = "LEHV6nWB2yk8pyo0adR*.7kCMdnj";

    static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 255 / (width - 1));
                pixels[i + 1] = (byte)(y * 255 / (height - 1));
                pixels[i + 2] = (byte)(x * y % 256);
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    [Fact]
    public void Encode_matches_the_reference_for_a_solid_colour()
    {
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)200, (byte)90, (byte)30, (byte)255);

        Assert.Equal("LBM{}2}WfQ}W}WoKfQoKfQfQfQfQ", BlurHash.Encode(pixels, 16, 16));
    }

    [Fact]
    public void Encode_matches_the_reference_without_components() =>
        Assert.Equal("00HewC", BlurHash.Encode(Gradient(32, 24), 32, 24, 1, 1));

    [Theory]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    public void Encode_writes_the_size_and_the_reference_average(int componentsX, int componentsY)
    {
        var hash = BlurHash.Encode(Gradient(32, 24), 32, 24, componentsX, componentsY);

        Assert.Equal(4 + 2 * componentsX * componentsY, hash.Length);
        Assert.Equal("HewC", hash[2..6]);
        Assert.True(BlurHash.IsValid(hash));
    }

    [Fact]
    public void Decode_matches_the_reference()
    {
        byte[] expected =
        [
            135, 164, 177, 255, 143, 167, 177, 255, 161, 173, 177, 255, 176, 178, 174, 255, 181, 180, 171, 255, 175, 177, 171, 255, 160, 172, 174, 255, 144, 167, 179, 255,
            131, 161, 175, 255, 140, 162, 174, 255, 157, 166, 170, 255, 171, 168, 165, 255, 176, 169, 161, 255, 170, 168, 162, 255, 155, 166, 169, 255, 141, 163, 176, 255,
            124, 154, 169, 255, 131, 153, 165, 255, 148, 148, 154, 255, 161, 145, 141, 255, 164, 145, 134, 255, 159, 148, 140, 255, 146, 152, 155, 255, 134, 154, 169, 255,
            120, 148, 162, 255, 127, 144, 155, 255, 141, 134, 139, 255, 154, 126, 118, 255, 158, 125, 108, 255, 153, 132, 118, 255, 142, 140, 141, 255, 130, 146, 159, 255,
            124, 144, 154, 255, 130, 141, 148, 255, 144, 134, 132, 255, 157, 129, 113, 255, 163, 130, 104, 255, 160, 135, 114, 255, 148, 140, 134, 255, 133, 142, 151, 255,
            132, 144, 149, 255, 138, 144, 145, 255, 151, 144, 135, 255, 166, 146, 124, 255, 174, 148, 118, 255, 171, 149, 123, 255, 158, 147, 135, 255, 139, 143, 146, 255,
        ];
        Assert.Equal(expected, BlurHash.Decode(Reference, 8, 6));
    }

    [Fact]
    public void Punch_matches_the_reference()
    {
        byte[] expected =
        [
            91, 187, 221, 255, 180, 210, 219, 255, 227, 225, 208, 255,
            176, 207, 214, 255, 0, 163, 203, 255, 140, 145, 164, 255,
            188, 134, 96, 255, 137, 155, 165, 255, 0, 131, 164, 255,
            129, 92, 87, 255, 185, 70, 0, 255, 141, 116, 95, 255,
        ];
        Assert.Equal(expected, BlurHash.Decode(Reference, 4, 3, punch: 3));
    }

    [Fact]
    public void A_solid_colour_decodes_to_itself_in_the_middle()
    {
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)200, (byte)90, (byte)30, (byte)255);

        var decoded = BlurHash.Decode(BlurHash.Encode(pixels, 16, 16), 16, 16);

        // The cosine basis is not centred, so the edges drift; the middle keeps the colour.
        var middle = (8 * 16 + 8) * 4;
        Assert.InRange(decoded[middle], 185, 215);
        Assert.InRange(decoded[middle + 1], 75, 105);
        Assert.InRange(decoded[middle + 2], 15, 45);
    }

    [Theory]
    [InlineData(Reference, true)]
    [InlineData("00HewC", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("LEHV6nWB2yk8pyo0adR*.7kCMdn", false)]
    [InlineData("LEHV6nWB2yk8pyo0adR*.7kCMdnjj", false)]
    [InlineData("LEHV6nWB2yk8pyo0adR*.7kCMdn\"", false)]
    public void IsValid_checks_length_and_alphabet(string? hash, bool valid) =>
        Assert.Equal(valid, BlurHash.IsValid(hash));

    [Fact]
    public void Decode_names_a_malformed_hash() =>
        Assert.Contains("\"nope\"", Assert.Throws<FormatException>(() => BlurHash.Decode("nope", 4, 4)).Message);
}
