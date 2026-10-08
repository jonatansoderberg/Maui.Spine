using Plugin.Maui.Spine.Controls;
using SkiaSharp;
using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

/// <summary>The geometry and colours a <see cref="MeshBackground"/> draws.</summary>
public class MeshSurfaceTests
{
    private const float Width = 200, Height = 400;

    private static readonly float[] Phases = [0f, 0.7f, 2.1f, 5f, 13.3f, 40f];

    private static MeshSurface Surface(int columns, int rows, SKColor[]? colors = null)
    {
        var surface = new MeshSurface();
        surface.Resize(columns, rows);
        surface.SetColors(colors ?? MeshPalettes.Sample(MeshPalettes.Design(MeshPreset.Aurora, dark: true, default), columns, rows));
        return surface;
    }

    private static int Close(SKColor a, SKColor b) =>
        Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)), Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 6)]
    [InlineData(8, 8)]
    public void The_mesh_covers_the_whole_area_at_every_phase(int columns, int rows)
    {
        var surface = Surface(columns, rows);
        var across = surface.VertexColumns;
        var down = surface.Vertices.Length / across;

        foreach (var phase in Phases)
        {
            surface.Layout(Width, Height, phase);
            var v = surface.Vertices;

            for (var i = 0; i < across; i++)
            {
                Assert.Equal(0, v[i].Y, 3);
                Assert.Equal(Height, v[(down - 1) * across + i].Y, 3);
            }

            for (var j = 0; j < down; j++)
            {
                Assert.Equal(0, v[j * across].X, 3);
                Assert.Equal(Width, v[j * across + across - 1].X, 3);
            }
        }
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(5, 4)]
    [InlineData(8, 8)]
    public void The_mesh_never_folds_over(int columns, int rows)
    {
        var surface = Surface(columns, rows);
        var across = surface.VertexColumns;
        var down = surface.Vertices.Length / across;

        for (var phase = 0f; phase < 60f; phase += 0.37f)
        {
            surface.Layout(Width, Height, phase);
            var v = surface.Vertices;

            for (var j = 0; j < down; j++)
                for (var i = 1; i < across; i++)
                    Assert.True(v[j * across + i].X > v[j * across + i - 1].X, $"Row {j} folds at column {i}, phase {phase}.");

            for (var i = 0; i < across; i++)
                for (var j = 1; j < down; j++)
                    Assert.True(v[j * across + i].Y > v[(j - 1) * across + i].Y, $"Column {i} folds at row {j}, phase {phase}.");
        }
    }

    [Fact]
    public void Every_control_point_shows_its_own_colour()
    {
        SKColor[] colors =
        [
            SKColors.Red, SKColors.Lime, SKColors.Blue,
            SKColors.Yellow, SKColors.White, SKColors.Black,
            SKColors.Magenta, SKColors.Cyan, new(0x80, 0x40, 0x20, 0x80),
        ];
        var surface = Surface(3, 3, colors);
        var across = surface.VertexColumns;

        for (var j = 0; j < 3; j++)
            for (var i = 0; i < 3; i++)
                Assert.True(Close(colors[j * 3 + i], surface.VertexColors[j * MeshSurface.Subdivisions * across + i * MeshSurface.Subdivisions]) <= 1);
    }

    [Fact]
    public void The_triangles_cover_every_cell_once()
    {
        var surface = Surface(4, 3);
        var cells = (surface.VertexColumns - 1) * (surface.Vertices.Length / surface.VertexColumns - 1);

        Assert.Equal(cells * 6, surface.Indices.Length);
        Assert.All(surface.Indices, index => Assert.InRange(index, 0, surface.Vertices.Length - 1));
    }

    [Fact]
    public void A_short_colour_list_repeats_row_by_row()
    {
        var colors = MeshPalettes.Repeat([SKColors.Red, SKColors.Blue], 3, 2);

        Assert.Equal([SKColors.Red, SKColors.Blue, SKColors.Red, SKColors.Blue, SKColors.Red, SKColors.Blue], colors);
    }

    [Fact]
    public void A_three_by_three_mesh_takes_a_preset_as_designed()
    {
        var design = MeshPalettes.Design(MeshPreset.Sunset, dark: false, default);

        Assert.All(design.Zip(MeshPalettes.Sample(design, 3, 3)), pair => Assert.True(Close(pair.First, pair.Second) <= 1));
    }

    [Theory]
    [InlineData(0xFF007AFF)]
    [InlineData(0xFFFF2D55)]
    [InlineData(0xFFFFCC00)]
    [InlineData(0xFF8E8E93)]
    public void The_accent_preset_is_light_in_light_mode_and_dark_in_dark_mode(uint accent)
    {
        var light = MeshPalettes.FromAccent(new SKColor(accent), dark: false).Select(Oklab.FromColor).ToList();
        var dark = MeshPalettes.FromAccent(new SKColor(accent), dark: true).Select(Oklab.FromColor).ToList();

        Assert.True(light.Average(c => c.L) > 0.85f);
        Assert.True(dark.Average(c => c.L) < 0.45f);
    }

    [Fact]
    public void Oklab_round_trips_and_blends_without_grey()
    {
        foreach (var color in new[] { SKColors.Red, SKColors.Lime, SKColors.Blue, SKColors.White, SKColors.Black, new SKColor(0x12, 0x34, 0x56) })
            Assert.True(Close(color, Oklab.FromColor(color).ToColor()) <= 1);

        var middle = (Oklab.FromColor(SKColors.Blue) * 0.5f + Oklab.FromColor(SKColors.Yellow) * 0.5f).ToColor();
        var spread = Math.Max(middle.Red, Math.Max(middle.Green, middle.Blue)) - Math.Min(middle.Red, Math.Min(middle.Green, middle.Blue));

        // sRGB's midpoint of blue and yellow is a flat grey (128, 128, 128); Oklab's keeps a hue.
        Assert.True(spread > 40, $"The blend {middle} is grey.");
    }
}
