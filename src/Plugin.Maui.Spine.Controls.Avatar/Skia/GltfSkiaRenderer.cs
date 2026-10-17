using System.Numerics;
using Plugin.Maui.Spine.Controls.Avatar;
using Plugin.Maui.Spine.Controls.Avatar.Gltf;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Skia;

/// <summary>
/// A glTF avatar drawn with Skia alone, where the platform has no SceneKit (D1 of docs/plans/spine-avatar.md, spike S2).
/// The shared rig poses the model; vertices are morphed, skinned and lit on the CPU (one key, a fill and ambient light,
/// per vertex); triangles are sorted back to front and drawn in one
/// <see cref="SKCanvas.DrawVertices(SKVertices, SKBlendMode, SKPaint)"/>. Textures are not sampled: a textured material
/// shows its base colour.
/// </summary>
internal sealed class GltfSkiaModel : ISkiaAvatarModel
{
    private readonly GltfModel _model;
    private readonly AvatarBindings _bindings;

    public GltfSkiaModel(AvatarPackage package, AvatarRepresentation representation)
        : this(GltfModel.Read(package.GetFile(representation.Model), representation.Model), package.GetBindings(representation))
    {
    }

    private GltfSkiaModel(GltfModel model, AvatarBindings bindings)
    {
        _model = model;
        _bindings = bindings;
    }

    /// <summary>
    /// The model, or <see langword="null"/> when a material is alpha-masked (hair cards, lashes): its shape comes from a
    /// texture this renderer does not sample, so the avatar shows its poster instead.
    /// </summary>
    public static GltfSkiaModel? TryCreate(AvatarPackage package, AvatarRepresentation representation)
    {
        var model = GltfModel.Read(package.GetFile(representation.Model), representation.Model);
        return model.Materials.Any(m => m.Mask) ? null : new GltfSkiaModel(model, package.GetBindings(representation));
    }

    public ISkiaAvatarRenderer CreateRenderer() => new GltfSkiaRenderer(_model, _bindings);
}

internal sealed class GltfSkiaRenderer : ISkiaAvatarRenderer
{
    private static readonly Vector3 KeyLight = Vector3.Normalize(new(-0.5f, 0.7f, 0.9f));
    private static readonly Vector3 FillLight = Vector3.Normalize(new(0.8f, 0.2f, 0.6f));

    private readonly GltfModel _model;
    private readonly GltfAvatarRig _rig;
    private readonly Matrix4x4[] _world;
    private readonly Matrix4x4[][] _joints;
    private readonly (int Node, int Mesh, int Primitive, int First)[] _parts;
    private readonly Vector3[] _viewPositions;
    private readonly SKColor[] _shade;
    private static readonly byte[] SrgbTable = BuildSrgbTable();
    private readonly int _triangles;
    private readonly int[] _triangleVertex;    // first vertex of each triangle's three, in _viewPositions
    private readonly int[] _triangleIndex;     // vertex indices, three per triangle
    private readonly int[] _order;
    private readonly float[] _depth;
    private readonly bool[] _blend, _twoSided;
    private readonly SKPoint[] _points;
    private readonly SKColor[] _colors;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly Vector3 _camera, _target;
    private readonly float _fov;
    private int _visibleBefore;

    /// <summary>The last frame's time in each phase, for measurements: posing, sorting, rasterising.</summary>
    internal (double Pose, double Sort, double Raster) LastPhases { get; private set; }

    public GltfSkiaRenderer(GltfModel model, AvatarBindings bindings)
    {
        _model = model;
        _rig = new GltfAvatarRig(model, bindings);
        _world = new Matrix4x4[model.Nodes.Length];
        _joints = [.. model.Skins.Select(s => new Matrix4x4[s.Joints.Length])];

        var parts = new List<(int, int, int, int)>();
        int vertices = 0, triangles = 0;
        for (var n = 0; n < model.Nodes.Length; n++)
        {
            if (model.Nodes[n].Mesh is var mesh and >= 0)
            {
                for (var p = 0; p < model.Meshes[mesh].Primitives.Length; p++)
                {
                    parts.Add((n, mesh, p, vertices));
                    vertices += model.Meshes[mesh].Primitives[p].VertexCount;
                    triangles += model.Meshes[mesh].Primitives[p].Indices.Length / 3;
                }
            }
        }
        _parts = [.. parts];
        _viewPositions = new Vector3[vertices];
        _shade = new SKColor[vertices];
        _triangles = triangles;
        _triangleIndex = new int[triangles * 3];
        _triangleVertex = new int[triangles];
        _order = new int[triangles];
        _depth = new float[triangles];
        _blend = new bool[triangles];
        _twoSided = new bool[triangles];
        _points = new SKPoint[triangles * 3];
        _colors = new SKColor[triangles * 3];
        _visibleBefore = triangles;

        var t = 0;
        foreach (var (_, mesh, p, first) in _parts)
        {
            var primitive = model.Meshes[mesh].Primitives[p];
            var material = primitive.Material >= 0 ? model.Materials[primitive.Material] : null;
            for (var i = 0; i < primitive.Indices.Length; i += 3, t++)
            {
                _triangleIndex[t * 3] = first + primitive.Indices[i];
                _triangleIndex[t * 3 + 1] = first + primitive.Indices[i + 1];
                _triangleIndex[t * 3 + 2] = first + primitive.Indices[i + 2];
                _blend[t] = material?.Blend == true;
                _twoSided[t] = material?.DoubleSided == true;
            }
        }

        var framing = bindings.Framing?.Extra;
        _fov = framing?.TryGetValue("verticalFov", out var fov) == true ? fov.GetSingle() : 35;
        _camera = framing?.TryGetValue("cameraPosition", out var cp) == true ? new(cp[0].GetSingle(), cp[1].GetSingle(), cp[2].GetSingle()) : new(0, 0, 3.5f);
        _target = framing?.TryGetValue("lookAt", out var la) == true ? new(la[0].GetSingle(), la[1].GetSingle(), la[2].GetSingle()) : Vector3.Zero;
    }

    public void Evaluate(AvatarRenderFrame frame) => _rig.Apply(frame);

    public void Draw(SKCanvas canvas, SKRect bounds, bool dark, SKColor? accent = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // The rig's springs scale and lift the whole model, as SceneKit's container node does.
        var container = Matrix4x4.CreateScale(_rig.RootScale) * Matrix4x4.CreateTranslation(_rig.RootOffset);
        foreach (var root in _model.SceneRoots)
            World(root, container);
        for (var s = 0; s < _model.Skins.Length; s++)
        {
            var skin = _model.Skins[s];
            for (var j = 0; j < skin.Joints.Length; j++)
                _joints[s][j] = skin.InverseBindMatrices[j] * _world[skin.Joints[j]];
        }

        var aspect = bounds.Width / bounds.Height;
        var fov = aspect < 1 ? 2 * MathF.Atan(MathF.Tan(_fov * MathF.PI / 360) / aspect) : _fov * MathF.PI / 180;
        var view = Matrix4x4.CreateLookAt(_camera, _target, Vector3.UnitY);
        var focal = 0.5f * bounds.Height / MathF.Tan(fov / 2);

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var background = dark ? 0.85f : 1f;
        foreach (var (node, mesh, p, first) in _parts)
            Pose(node, mesh, p, first, view, background);

        var posed = System.Diagnostics.Stopwatch.GetTimestamp();
        // Back faces go before the sort; the rest back to front, transparent triangles after the opaque ones.
        var visible = 0;
        for (var t = 0; t < _triangles; t++)
        {
            var a = _viewPositions[_triangleIndex[t * 3]];
            var b = _viewPositions[_triangleIndex[t * 3 + 1]];
            var c = _viewPositions[_triangleIndex[t * 3 + 2]];
            // Seen from the camera at the origin of view space: counter-clockwise triangles face it.
            if (!_twoSided[t] && Vector3.Dot(Vector3.Cross(b - a, c - a), a) >= 0)
                continue;
            _order[visible] = t;
            _depth[visible] = (a.Z + b.Z + c.Z) / 3 + (_blend[t] ? 1000 : 0);
            visible++;
        }
        Array.Sort(_depth, _order, 0, visible);

        var cx = bounds.MidX;
        var cy = bounds.MidY;
        for (var k = 0; k < visible; k++)
        {
            var t = _order[k];
            var o = k * 3;
            for (var v = 0; v < 3; v++)
            {
                var i = _triangleIndex[t * 3 + v];
                var q = _viewPositions[i];
                var z = MathF.Max(-q.Z, 0.01f);
                _points[o + v] = new SKPoint(cx + q.X / z * focal, cy - q.Y / z * focal);
                _colors[o + v] = _shade[i];
            }
        }
        // The arrays keep their length; what is past the visible triangles collapses to nothing.
        if (_visibleBefore > visible)
            Array.Fill(_points, default, visible * 3, (_visibleBefore - visible) * 3);
        _visibleBefore = visible;

        var sorted = System.Diagnostics.Stopwatch.GetTimestamp();
        using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, _points, null, _colors);
        canvas.DrawVertices(vertices, SKBlendMode.Dst, _paint);
        canvas.Flush();
        LastPhases = (System.Diagnostics.Stopwatch.GetElapsedTime(started, posed).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(posed, sorted).TotalMilliseconds,
            System.Diagnostics.Stopwatch.GetElapsedTime(sorted).TotalMilliseconds);
    }

    public void Dispose() => _paint.Dispose();

    private void World(int node, Matrix4x4 parent)
    {
        var local = Matrix4x4.CreateScale(_rig.Scale[node]) * Matrix4x4.CreateFromQuaternion(_rig.Rotation[node]) * Matrix4x4.CreateTranslation(_rig.Translation[node]);
        _world[node] = local * parent;
        foreach (var child in _model.Nodes[node].Children)
            World(child, _world[node]);
    }

    // Morphs, skins, transforms to view space and lights one primitive's vertices.
    private void Pose(int node, int mesh, int p, int first, Matrix4x4 view, float background)
    {
        var primitive = _model.Meshes[mesh].Primitives[p];
        var weights = _rig.Weights[mesh];
        var material = primitive.Material >= 0 ? _model.Materials[primitive.Material] : null;
        var baseColor = material is { } m ? new Vector3(m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z) : new Vector3(0.8f);
        var emissive = material?.Emissive ?? Vector3.Zero;
        var metallic = material?.Metallic ?? 0;
        var skin = _model.Nodes[node].Skin;
        var skinned = skin >= 0 && primitive.Joints is not null && primitive.Weights is not null;
        var world = _world[node];
        var positions = primitive.Positions;
        var normals = primitive.Normals;

        for (var v = 0; v < primitive.VertexCount; v++)
        {
            var position = new Vector3(positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2]);
            var normal = normals is null ? Vector3.UnitZ : new Vector3(normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2]);
            for (var k = 0; k < primitive.Targets.Length && k < weights.Length; k++)
            {
                var w = weights[k];
                if (w is > -0.001f and < 0.001f)
                    continue;
                var target = primitive.Targets[k];
                position += w * new Vector3(target.Positions[v * 3], target.Positions[v * 3 + 1], target.Positions[v * 3 + 2]);
                if (target.Normals is { } tn && normals is not null)
                    normal += w * new Vector3(tn[v * 3], tn[v * 3 + 1], tn[v * 3 + 2]);
            }

            Vector3 placed, turned;
            if (skinned)
            {
                placed = Vector3.Zero;
                turned = Vector3.Zero;
                for (var j = 0; j < 4; j++)
                {
                    var weight = primitive.Weights![v * 4 + j];
                    if (weight <= 0)
                        continue;
                    var joint = _joints[skin][primitive.Joints![v * 4 + j]];
                    placed += weight * Vector3.Transform(position, joint);
                    turned += weight * Vector3.TransformNormal(normal, joint);
                }
            }
            else
            {
                placed = Vector3.Transform(position, world);
                turned = Vector3.TransformNormal(normal, world);
            }

            _viewPositions[first + v] = Vector3.Transform(placed, view);
            var n = turned.LengthSquared() > 0 ? Vector3.Normalize(turned) : Vector3.UnitZ;
            var light = 0.32f + 0.75f * MathF.Max(0, Vector3.Dot(n, KeyLight)) + 0.25f * MathF.Max(0, Vector3.Dot(n, FillLight));
            // A metal reads darker than paint under the same light; the specular sheen is not drawn.
            var shade = (baseColor * light * (1 - 0.4f * metallic) + emissive) * background;
            _shade[first + v] = new SKColor(Encode(shade.X), Encode(shade.Y), Encode(shade.Z));
        }
    }

    // Linear 0–1 to an sRGB byte through a table, so a frame of vertices costs no power functions.
    private static byte Encode(float linear) => SrgbTable[(int)(Math.Clamp(linear, 0, 1) * (SrgbTable.Length - 1))];

    private static byte[] BuildSrgbTable()
    {
        var table = new byte[4096];
        for (var i = 0; i < table.Length; i++)
        {
            var linear = i / (float)(table.Length - 1);
            var c = linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;
            table[i] = (byte)Math.Clamp(c * 255 + 0.5f, 0, 255);
        }
        return table;
    }
}
