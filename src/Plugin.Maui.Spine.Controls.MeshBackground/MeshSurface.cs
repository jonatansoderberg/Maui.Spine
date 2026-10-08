using SkiaSharp;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The geometry of a mesh gradient: a grid of control points with a colour each, and the triangles
/// drawn from them.
/// </summary>
/// <remarks>
/// The control points span a bicubic Catmull-Rom surface, in position and in colour (Oklab), that
/// passes through every point. It is sampled <see cref="Subdivisions"/> times per patch into a vertex
/// grid that <see cref="SKCanvas.DrawVertices(SKVertexMode, SKPoint[], SKPoint[], SKColor[], SKBlendMode, ushort[], SKPaint)"/>
/// fills with interpolated colour. Skia's own Coons patches (<c>DrawPatch</c>) blend each patch's
/// four corner colours on their own, which leaves a visible crease along every patch edge; the
/// bicubic surface is smooth across them, as SwiftUI's <c>MeshGradient</c> is with <c>smoothsColors</c>.
/// The vertex colours depend only on the control colours, so a frame of drift recomputes positions only.
/// </remarks>
internal sealed class MeshSurface
{
    /// <summary>Vertex rows and columns per patch.</summary>
    public const int Subdivisions = 12;

    /// <summary>How far a point wanders from its place, as a share of the distance to its neighbour.</summary>
    public const float DriftReach = 0.25f;

    private SKPoint[] _points = [];
    private float[] _motion = [];
    private Sample[] _samplesU = [];
    private Sample[] _samplesV = [];
    private SKPoint[] _rowPass = [];

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public SKPoint[] Vertices { get; private set; } = [];

    public SKColor[] VertexColors { get; private set; } = [];

    public ushort[] Indices { get; private set; } = [];

    /// <summary>Vertices per row.</summary>
    public int VertexColumns => _samplesU.Length;

    /// <summary>Sets the number of control points; the colours must be set again afterwards.</summary>
    public void Resize(int columns, int rows)
    {
        if (columns == Columns && rows == Rows)
            return;

        Columns = columns;
        Rows = rows;
        _points = new SKPoint[columns * rows];
        _samplesU = Samples(columns);
        _samplesV = Samples(rows);
        _rowPass = new SKPoint[_samplesU.Length * rows];
        Vertices = new SKPoint[_samplesU.Length * _samplesV.Length];
        VertexColors = new SKColor[Vertices.Length];
        Indices = Triangles(_samplesU.Length, _samplesV.Length);

        // Each point gets its own speed and phase on both axes, the same on every run.
        _motion = new float[columns * rows * 4];
        for (var k = 0; k < columns * rows; k++)
        {
            _motion[k * 4] = 0.7f + 0.6f * Hash(k, 0);
            _motion[k * 4 + 1] = 0.7f + 0.6f * Hash(k, 1);
            _motion[k * 4 + 2] = MathF.Tau * Hash(k, 2);
            _motion[k * 4 + 3] = MathF.Tau * Hash(k, 3);
        }
    }

    /// <summary>Sets one colour per control point, row by row.</summary>
    public void SetColors(SKColor[] pointColors)
    {
        if (pointColors.Length != Columns * Rows)
            throw new ArgumentException($"A {Columns} × {Rows} mesh needs {Columns * Rows} colours, not {pointColors.Length}.", nameof(pointColors));

        var lab = Array.ConvertAll(pointColors, Oklab.FromColor);
        var rowPass = new Oklab[_samplesU.Length * Rows];

        for (var j = 0; j < Rows; j++)
            for (var su = 0; su < _samplesU.Length; su++)
                rowPass[j * _samplesU.Length + su] = _samplesU[su].Apply(lab, j * Columns, 1);

        for (var sv = 0; sv < _samplesV.Length; sv++)
            for (var su = 0; su < _samplesU.Length; su++)
                VertexColors[sv * _samplesU.Length + su] = _samplesV[sv].Apply(rowPass, su, _samplesU.Length).ToColor();
    }

    /// <summary>
    /// Places the control points in a <paramref name="width"/> × <paramref name="height"/> area at
    /// <paramref name="phase"/> (radians of the slowest drift; a still mesh is drawn at 0) and recomputes the vertices.
    /// </summary>
    /// <remarks>
    /// The corners stay put and the points on an edge slide along it, so the mesh always covers the area.
    /// </remarks>
    public void Layout(float width, float height, float phase)
    {
        var cellWidth = width / (Columns - 1);
        var cellHeight = height / (Rows - 1);

        for (var j = 0; j < Rows; j++)
        {
            for (var i = 0; i < Columns; i++)
            {
                var k = j * Columns + i;
                var x = i * cellWidth;
                var y = j * cellHeight;

                if (i > 0 && i < Columns - 1)
                    x += DriftReach * cellWidth * MathF.Sin(phase * _motion[k * 4] + _motion[k * 4 + 2]);

                if (j > 0 && j < Rows - 1)
                    y += DriftReach * cellHeight * MathF.Sin(phase * _motion[k * 4 + 1] + _motion[k * 4 + 3]);

                _points[k] = new SKPoint(x, y);
            }
        }

        var stride = _samplesU.Length;
        for (var j = 0; j < Rows; j++)
            for (var su = 0; su < stride; su++)
                _rowPass[j * stride + su] = _samplesU[su].Apply(_points, j * Columns, 1);

        var vertices = Vertices;
        for (var sv = 0; sv < _samplesV.Length; sv++)
            for (var su = 0; su < stride; su++)
                vertices[sv * stride + su] = _samplesV[sv].Apply(_rowPass, su, stride);
    }

    /// <summary>Where the surface is sampled along one axis of <paramref name="count"/> control points.</summary>
    private static Sample[] Samples(int count)
    {
        var samples = new Sample[(count - 1) * Subdivisions + 1];
        for (var n = 0; n < samples.Length; n++)
        {
            var patch = Math.Min(n / Subdivisions, count - 2);
            var t = (n - patch * Subdivisions) / (float)Subdivisions;
            samples[n] = Sample.CatmullRom(patch, t, count);
        }

        return samples;
    }

    private static ushort[] Triangles(int columns, int rows)
    {
        var indices = new ushort[(columns - 1) * (rows - 1) * 6];
        var n = 0;
        for (var j = 0; j < rows - 1; j++)
        {
            for (var i = 0; i < columns - 1; i++)
            {
                var a = (ushort)(j * columns + i);
                var b = (ushort)(a + 1);
                var c = (ushort)(a + columns);
                var d = (ushort)(c + 1);
                indices[n++] = a;
                indices[n++] = b;
                indices[n++] = c;
                indices[n++] = b;
                indices[n++] = d;
                indices[n++] = c;
            }
        }

        return indices;
    }

    private static float Hash(int k, int salt)
    {
        var h = (uint)(k * 374761393 + salt * 668265263 + 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>
    /// A point on a Catmull-Rom curve as weights on (up to) four control points. Past the ends the
    /// curve continues in a straight line (a phantom point mirrored through the last one), which
    /// keeps the edge points' own line: a curve along an edge stays on that edge.
    /// </summary>
    private readonly struct Sample
    {
        private readonly int _first;
        private readonly float _w0, _w1, _w2, _w3;

        private Sample(int first, float w0, float w1, float w2, float w3)
        {
            _first = first;
            _w0 = w0;
            _w1 = w1;
            _w2 = w2;
            _w3 = w3;
        }

        public static Sample CatmullRom(int patch, float t, int count)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            Span<float> w =
            [
                0.5f * (-t3 + 2 * t2 - t),
                0.5f * (3 * t3 - 5 * t2 + 2),
                0.5f * (-3 * t3 + 4 * t2 + t),
                0.5f * (t3 - t2),
            ];

            // Fold a phantom point before the first into the first two (p[-1] = 2·p[0] − p[1]),
            // and one after the last likewise, so every weight lands on a real point.
            Span<float> weights = stackalloc float[count];
            for (var k = 0; k < 4; k++)
            {
                var index = patch - 1 + k;
                if (index < 0)
                {
                    weights[0] += 2 * w[k];
                    weights[1] -= w[k];
                }
                else if (index >= count)
                {
                    weights[count - 1] += 2 * w[k];
                    weights[count - 2] -= w[k];
                }
                else
                {
                    weights[index] += w[k];
                }
            }

            var first = Math.Max(patch - 1, 0);
            Span<float> four = stackalloc float[4];
            for (var k = 0; k < 4 && first + k < count; k++)
                four[k] = weights[first + k];

            return new Sample(first, four[0], four[1], four[2], four[3]);
        }

        /// <summary>The weighted sum of the points <c>values[offset + index * stride]</c>.</summary>
        public SKPoint Apply(SKPoint[] values, int offset, int stride)
        {
            float x = 0, y = 0;
            Add(ref x, ref y, values, offset + _first * stride, _w0);
            Add(ref x, ref y, values, offset + (_first + 1) * stride, _w1);
            Add(ref x, ref y, values, offset + (_first + 2) * stride, _w2);
            Add(ref x, ref y, values, offset + (_first + 3) * stride, _w3);
            return new SKPoint(x, y);
        }

        /// <inheritdoc cref="Apply(SKPoint[], int, int)"/>
        public Oklab Apply(Oklab[] values, int offset, int stride)
        {
            var result = values[offset + _first * stride] * _w0;
            if (_w1 != 0) result += values[offset + (_first + 1) * stride] * _w1;
            if (_w2 != 0) result += values[offset + (_first + 2) * stride] * _w2;
            if (_w3 != 0) result += values[offset + (_first + 3) * stride] * _w3;
            return result;
        }

        private static void Add(ref float x, ref float y, SKPoint[] values, int index, float weight)
        {
            if (weight == 0)
                return;

            x += values[index].X * weight;
            y += values[index].Y * weight;
        }
    }
}
