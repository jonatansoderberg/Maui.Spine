using System.Numerics;
using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Gltf;

/// <summary>
/// The parts of a GLB 2.0 file an avatar needs, decoded once into plain arrays: node hierarchy and
/// rest transforms, triangle meshes with normals, UVs, morph targets and skin weights, PBR factors with
/// colour, normal and emissive textures, and animation clips, sparse accessors included. No compressed
/// geometry or texture extensions; a file that needs them fails with the reason.
/// Renderer-neutral, so a native engine (SceneKit, Filament) and a software renderer can share it.
/// </summary>
public sealed class GltfModel
{
    public required GltfNode[] Nodes { get; init; }

    public required GltfMesh[] Meshes { get; init; }

    public required GltfMaterial[] Materials { get; init; }

    public required GltfSkin[] Skins { get; init; }

    public required GltfClip[] Clips { get; init; }

    /// <summary>Embedded images (PNG or JPEG bytes), referenced by index from materials.</summary>
    public required GltfImage[] Images { get; init; }

    /// <summary>Root nodes of the default scene.</summary>
    public required int[] SceneRoots { get; init; }

    public static GltfModel Read(ReadOnlyMemory<byte> glb, string path)
    {
        var bytes = glb.Span;
        if (bytes.Length < 20 || BitConverter.ToUInt32(bytes) != 0x46546C67 || BitConverter.ToUInt32(bytes[4..]) != 2)
            throw new AvatarFormatException(path, "not a GLB 2.0 file");

        var jsonLength = (int)BitConverter.ToUInt32(bytes[12..]);
        var binStart = 20 + jsonLength + 8;
        var bin = binStart <= glb.Length ? glb[binStart..] : ReadOnlyMemory<byte>.Empty;

        try
        {
            using var document = JsonDocument.Parse(glb.Slice(20, jsonLength));
            return new Reader(document.RootElement, bin, path).Read();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException or ArgumentException)
        {
            throw new AvatarFormatException(path, e.Message, e);
        }
    }

    private sealed class Reader(JsonElement root, ReadOnlyMemory<byte> bin, string path)
    {
        public GltfModel Read()
        {
            if (root.TryGetProperty("extensionsRequired", out var required) && required.GetArrayLength() > 0)
                throw new AvatarFormatException(path, $"requires glTF extensions this runtime does not support: {required}");

            var nodes = Array(root, "nodes").Select(ReadNode).ToArray();
            var scene = root.TryGetProperty("scene", out var s) ? s.GetInt32() : 0;
            var roots = root.GetProperty("scenes")[scene].GetProperty("nodes").EnumerateArray().Select(n => n.GetInt32()).ToArray();

            return new GltfModel
            {
                Nodes = nodes,
                Meshes = Array(root, "meshes").Select(ReadMesh).ToArray(),
                Materials = Array(root, "materials").Select(ReadMaterial).ToArray(),
                Skins = Array(root, "skins").Select(ReadSkin).ToArray(),
                Clips = Array(root, "animations").Select(ReadClip).ToArray(),
                Images = Array(root, "images").Select(ReadImage).ToArray(),
                SceneRoots = roots,
            };
        }

        private static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
            element.TryGetProperty(name, out var items) ? items.EnumerateArray() : [];

        private static GltfNode ReadNode(JsonElement n, int index)
        {
            var t = n.TryGetProperty("translation", out var tr) ? Vec3(tr) : Vector3.Zero;
            var r = n.TryGetProperty("rotation", out var ro) ? new Quaternion(ro[0].GetSingle(), ro[1].GetSingle(), ro[2].GetSingle(), ro[3].GetSingle()) : Quaternion.Identity;
            var sc = n.TryGetProperty("scale", out var sca) ? Vec3(sca) : Vector3.One;
            if (n.TryGetProperty("matrix", out var matrix))
            {
                var m = new Matrix4x4();
                for (var i = 0; i < 16; i++)
                    m[i % 4, i / 4] = matrix[i].GetSingle();
                Matrix4x4.Decompose(Matrix4x4.Transpose(m), out sc, out r, out t);
            }

            return new GltfNode(
                index,
                n.TryGetProperty("name", out var name) ? name.GetString() ?? $"node{index}" : $"node{index}",
                n.TryGetProperty("children", out var children) ? [.. children.EnumerateArray().Select(c => c.GetInt32())] : [],
                n.TryGetProperty("mesh", out var mesh) ? mesh.GetInt32() : -1,
                n.TryGetProperty("skin", out var skin) ? skin.GetInt32() : -1,
                t, Quaternion.Normalize(r), sc);
        }

        private GltfMesh ReadMesh(JsonElement m, int index)
        {
            var primitives = new List<GltfPrimitive>();
            foreach (var p in m.GetProperty("primitives").EnumerateArray())
            {
                if (p.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4)
                    throw new AvatarFormatException(path, $"mesh {index}: only triangle primitives are supported");

                var attributes = p.GetProperty("attributes");
                var positions = Floats(attributes.GetProperty("POSITION").GetInt32(), 3);
                var vertexCount = positions.Length / 3;
                var targets = p.TryGetProperty("targets", out var ts)
                    ? ts.EnumerateArray().Select(t => new GltfMorphTarget(
                        t.TryGetProperty("POSITION", out var tp) ? Floats(tp.GetInt32(), 3) : new float[vertexCount * 3],
                        t.TryGetProperty("NORMAL", out var tn) ? Floats(tn.GetInt32(), 3) : null)).ToArray()
                    : [];

                primitives.Add(new GltfPrimitive(
                    positions,
                    attributes.TryGetProperty("NORMAL", out var normal) ? Floats(normal.GetInt32(), 3) : null,
                    p.TryGetProperty("indices", out var indices) ? Indices(indices.GetInt32()) : [.. Enumerable.Range(0, vertexCount)],
                    attributes.TryGetProperty("TEXCOORD_0", out var uv) ? Floats(uv.GetInt32(), 2) : null,
                    attributes.TryGetProperty("JOINTS_0", out var joints) ? Ints(joints.GetInt32(), 4) : null,
                    attributes.TryGetProperty("WEIGHTS_0", out var weights) ? Floats(weights.GetInt32(), 4) : null,
                    p.TryGetProperty("material", out var material) ? material.GetInt32() : -1,
                    targets));
            }

            var names = m.TryGetProperty("extras", out var extras) && extras.TryGetProperty("targetNames", out var tn2)
                ? tn2.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                : [];
            var defaults = m.TryGetProperty("weights", out var w) ? w.EnumerateArray().Select(e => e.GetSingle()).ToArray() : [];
            return new GltfMesh(m.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "", [.. primitives], names, defaults);
        }

        private GltfImage ReadImage(JsonElement image, int index)
        {
            if (!image.TryGetProperty("bufferView", out var view))
                throw new AvatarFormatException(path, $"image {index} is not embedded; a .spineavatar loads nothing from outside its GLB");
            var mime = image.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "" : "";
            if (mime is not ("image/png" or "image/jpeg"))
                throw new AvatarFormatException(path, $"image {index} is {mime}; only PNG and JPEG are supported");
            var v = root.GetProperty("bufferViews")[view.GetInt32()];
            var offset = v.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
            return new GltfImage(image.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", mime, bin.Slice(offset, v.GetProperty("byteLength").GetInt32()));
        }

        // A material's texture slot, resolved to an image index; -1 when the slot is empty.
        private int Texture(JsonElement holder, string name)
        {
            if (holder.ValueKind != JsonValueKind.Object || !holder.TryGetProperty(name, out var info))
                return -1;
            var texture = root.GetProperty("textures")[info.GetProperty("index").GetInt32()];
            return texture.TryGetProperty("source", out var source) ? source.GetInt32() : -1;
        }

        private GltfMaterial ReadMaterial(JsonElement m)
        {
            var pbr = m.TryGetProperty("pbrMetallicRoughness", out var p) ? p : default;
            var color = pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty("baseColorFactor", out var c)
                ? new Vector4(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle(), c[3].GetSingle())
                : Vector4.One;
            float Factor(string name, float fallback) => pbr.ValueKind == JsonValueKind.Object && pbr.TryGetProperty(name, out var f) ? f.GetSingle() : fallback;
            var emissive = m.TryGetProperty("emissiveFactor", out var e) ? Vec3(e) : Vector3.Zero;
            var strength = m.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("KHR_materials_emissive_strength", out var es)
                ? es.GetProperty("emissiveStrength").GetSingle()
                : 1f;
            var alpha = m.TryGetProperty("alphaMode", out var am) ? am.GetString() : "OPAQUE";
            return new GltfMaterial(
                m.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                color, Factor("metallicFactor", 1), Factor("roughnessFactor", 1), emissive * strength,
                m.TryGetProperty("doubleSided", out var ds) && ds.GetBoolean(),
                alpha == "BLEND")
            {
                Mask = alpha == "MASK",
                AlphaCutoff = m.TryGetProperty("alphaCutoff", out var cutoff) ? cutoff.GetSingle() : 0.5f,
                BaseColorTexture = Texture(pbr, "baseColorTexture"),
                NormalTexture = Texture(m, "normalTexture"),
                EmissiveTexture = Texture(m, "emissiveTexture"),
            };
        }

        private GltfSkin ReadSkin(JsonElement s)
        {
            var joints = s.GetProperty("joints").EnumerateArray().Select(j => j.GetInt32()).ToArray();
            var matrices = s.TryGetProperty("inverseBindMatrices", out var ibm) ? Floats(ibm.GetInt32(), 16) : null;
            var inverseBind = new Matrix4x4[joints.Length];
            for (var i = 0; i < joints.Length; i++)
            {
                if (matrices is null)
                {
                    inverseBind[i] = Matrix4x4.Identity;
                    continue;
                }
                // glTF matrices are column-major; System.Numerics multiplies row vectors, which is the same layout read row by row.
                var m = new Matrix4x4();
                for (var k = 0; k < 16; k++)
                    m[k / 4, k % 4] = matrices[i * 16 + k];
                inverseBind[i] = m;
            }
            return new GltfSkin(joints, inverseBind, s.TryGetProperty("skeleton", out var sk) ? sk.GetInt32() : -1);
        }

        private GltfClip ReadClip(JsonElement a, int index)
        {
            var samplers = a.GetProperty("samplers").EnumerateArray().ToArray();
            var channels = new List<GltfChannel>();
            foreach (var c in a.GetProperty("channels").EnumerateArray())
            {
                var target = c.GetProperty("target");
                if (!target.TryGetProperty("node", out var node))
                    continue;
                var sampler = samplers[c.GetProperty("sampler").GetInt32()];
                var times = Floats(sampler.GetProperty("input").GetInt32(), 1);
                var pathName = target.GetProperty("path").GetString();
                var property = pathName switch
                {
                    "translation" => GltfPath.Translation,
                    "rotation" => GltfPath.Rotation,
                    "scale" => GltfPath.Scale,
                    "weights" => GltfPath.Weights,
                    _ => throw new AvatarFormatException(path, $"animation path '{pathName}' is not supported"),
                };
                var output = sampler.GetProperty("output").GetInt32();
                var interpolation = sampler.TryGetProperty("interpolation", out var ip) ? ip.GetString() : "LINEAR";
                var values = Floats(output, ComponentCount(output));
                var width = values.Length / Math.Max(1, times.Length) / (interpolation == "CUBICSPLINE" ? 3 : 1);
                channels.Add(new GltfChannel(node.GetInt32(), property, times, values, width,
                    interpolation switch { "STEP" => GltfInterpolation.Step, "CUBICSPLINE" => GltfInterpolation.CubicSpline, _ => GltfInterpolation.Linear }));
            }
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? $"clip{index}" : $"clip{index}";
            return new GltfClip(name, [.. channels], channels.Count == 0 ? 0 : channels.Max(c => c.Times[^1]));
        }

        private int ComponentCount(int accessor) => root.GetProperty("accessors")[accessor].GetProperty("type").GetString() switch
        {
            "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16,
            var t => throw new AvatarFormatException(path, $"accessor type {t} is not supported"),
        };

        private static int ComponentSize(int type, string path) => type switch
        {
            5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
            _ => throw new AvatarFormatException(path, $"component type {type}"),
        };

        private ReadOnlyMemory<byte> Slice(JsonElement owner)
        {
            var view = root.GetProperty("bufferViews")[owner.GetProperty("bufferView").GetInt32()];
            var offset = (view.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) + (owner.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            return bin[offset..];
        }

        // Sparse accessors (morph targets exported by MPFB and others) start from zeros, or from their
        // bufferView when they have one, and overwrite the listed elements.
        private float[] Floats(int accessor, int width)
        {
            var a = root.GetProperty("accessors")[accessor];
            var count = a.GetProperty("count").GetInt32();
            var type = a.GetProperty("componentType").GetInt32();
            var normalized = a.TryGetProperty("normalized", out var n) && n.GetBoolean();
            var size = ComponentSize(type, path);
            var result = new float[count * width];

            if (a.TryGetProperty("bufferView", out _))
            {
                var view = root.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
                var stride = view.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : 0;
                stride = stride == 0 ? width * size : stride;
                var span = Slice(a).Span;
                for (var i = 0; i < count; i++)
                    for (var k = 0; k < width; k++)
                        result[i * width + k] = Component(span[(i * stride + k * size)..], type, normalized);
            }

            if (a.TryGetProperty("sparse", out var sparse))
            {
                var sparseCount = sparse.GetProperty("count").GetInt32();
                var indices = sparse.GetProperty("indices");
                var indexType = indices.GetProperty("componentType").GetInt32();
                var indexSize = ComponentSize(indexType, path);
                var indexSpan = Slice(indices).Span;
                var valueSpan = Slice(sparse.GetProperty("values")).Span;
                for (var i = 0; i < sparseCount; i++)
                {
                    var target = (int)Component(indexSpan[(i * indexSize)..], indexType, false);
                    if ((uint)target >= (uint)count)
                        throw new AvatarFormatException(path, $"accessor {accessor} has sparse index {target} outside its {count} elements");
                    for (var k = 0; k < width; k++)
                        result[target * width + k] = Component(valueSpan[((i * width + k) * size)..], type, normalized);
                }
            }
            return result;
        }

        private static float Component(ReadOnlySpan<byte> at, int type, bool normalized) => type switch
        {
            5126 => BitConverter.ToSingle(at),
            5121 => normalized ? at[0] / 255f : at[0],
            5123 => normalized ? BitConverter.ToUInt16(at) / 65535f : BitConverter.ToUInt16(at),
            5120 => normalized ? Math.Max((sbyte)at[0] / 127f, -1) : (sbyte)at[0],
            5122 => normalized ? Math.Max(BitConverter.ToInt16(at) / 32767f, -1) : BitConverter.ToInt16(at),
            _ => BitConverter.ToUInt32(at),
        };

        private int[] Ints(int accessor, int width) => [.. Floats(accessor, width).Select(f => (int)f)];

        private int[] Indices(int accessor) => Ints(accessor, 1);

        private static Vector3 Vec3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
    }
}

public sealed record GltfNode(int Index, string Name, int[] Children, int Mesh, int Skin, Vector3 Translation, Quaternion Rotation, Vector3 Scale);

public sealed record GltfMesh(string Name, GltfPrimitive[] Primitives, string[] TargetNames, float[] DefaultWeights)
{
    public int TargetCount => Primitives.Length == 0 ? 0 : Primitives.Max(p => p.Targets.Length);
}

/// <param name="Positions">x, y, z per vertex.</param>
/// <param name="Joints">Four joint indices per vertex (indices into the skin's joint list), or null.</param>
public sealed record GltfPrimitive(float[] Positions, float[]? Normals, int[] Indices, float[]? TexCoords, int[]? Joints, float[]? Weights, int Material, GltfMorphTarget[] Targets)
{
    public int VertexCount => Positions.Length / 3;
}

/// <summary>Per-vertex position (and optionally normal) deltas.</summary>
public sealed record GltfMorphTarget(float[] Positions, float[]? Normals);

/// <param name="BaseColor">Linear RGBA.</param>
/// <param name="Emissive">Linear RGB, already multiplied by KHR_materials_emissive_strength.</param>
public sealed record GltfMaterial(string Name, Vector4 BaseColor, float Metallic, float Roughness, Vector3 Emissive, bool DoubleSided, bool Blend)
{
    /// <summary>Alpha-tested (hair, lashes): pixels under <see cref="AlphaCutoff"/> are discarded.</summary>
    public bool Mask { get; init; }

    public float AlphaCutoff { get; init; } = 0.5f;

    /// <summary>Image indices into <see cref="GltfModel.Images"/>, or -1.</summary>
    public int BaseColorTexture { get; init; } = -1;

    public int NormalTexture { get; init; } = -1;

    public int EmissiveTexture { get; init; } = -1;
}

public sealed record GltfImage(string Name, string MimeType, ReadOnlyMemory<byte> Bytes);

public sealed record GltfSkin(int[] Joints, Matrix4x4[] InverseBindMatrices, int Skeleton);

public enum GltfPath { Translation, Rotation, Scale, Weights }

public enum GltfInterpolation { Linear, Step, CubicSpline }

/// <param name="Width">Values per keyframe: 3 for translation and scale, 4 for rotation, the morph target count for weights.</param>
public sealed record GltfChannel(int Node, GltfPath Path, float[] Times, float[] Values, int Width, GltfInterpolation Interpolation)
{
    /// <summary>Writes the value at <paramref name="time"/> into <paramref name="result"/> (length <see cref="Width"/>). No allocation.</summary>
    public void Sample(float time, Span<float> result)
    {
        var last = Times.Length - 1;
        var cubic = Interpolation == GltfInterpolation.CubicSpline;
        var keyWidth = cubic ? Width * 3 : Width;
        var valueOffset = cubic ? Width : 0;

        if (time <= Times[0] || last == 0)
        {
            Values.AsSpan(valueOffset, Width).CopyTo(result);
            return;
        }
        if (time >= Times[last])
        {
            Values.AsSpan(last * keyWidth + valueOffset, Width).CopyTo(result);
            return;
        }

        var a = System.Array.BinarySearch(Times, time);
        a = a >= 0 ? a : ~a - 1;
        var dt = Times[a + 1] - Times[a];
        var t = dt > 0 ? (time - Times[a]) / dt : 0;

        if (Interpolation == GltfInterpolation.Step)
        {
            Values.AsSpan(a * keyWidth, Width).CopyTo(result);
            return;
        }

        if (cubic)
        {
            // Hermite with the stored in/out tangents (scaled by the key interval).
            var t2 = t * t;
            var t3 = t2 * t;
            var p0 = a * keyWidth + Width;
            var m0 = a * keyWidth + 2 * Width;
            var p1 = (a + 1) * keyWidth + Width;
            var m1 = (a + 1) * keyWidth;
            for (var i = 0; i < Width; i++)
                result[i] = (2 * t3 - 3 * t2 + 1) * Values[p0 + i] + (t3 - 2 * t2 + t) * dt * Values[m0 + i]
                    + (-2 * t3 + 3 * t2) * Values[p1 + i] + (t3 - t2) * dt * Values[m1 + i];
        }
        else if (Path == GltfPath.Rotation)
        {
            var q0 = new Quaternion(Values[a * 4], Values[a * 4 + 1], Values[a * 4 + 2], Values[a * 4 + 3]);
            var q1 = new Quaternion(Values[a * 4 + 4], Values[a * 4 + 5], Values[a * 4 + 6], Values[a * 4 + 7]);
            var q = Quaternion.Slerp(q0, q1, t);
            result[0] = q.X; result[1] = q.Y; result[2] = q.Z; result[3] = q.W;
            return;
        }
        else
        {
            for (var i = 0; i < Width; i++)
                result[i] = Values[a * Width + i] + (Values[(a + 1) * Width + i] - Values[a * Width + i]) * t;
        }

        if (Path == GltfPath.Rotation)
        {
            var length = MathF.Sqrt(result[0] * result[0] + result[1] * result[1] + result[2] * result[2] + result[3] * result[3]);
            for (var i = 0; i < 4; i++)
                result[i] /= length;
        }
    }
}

public sealed record GltfClip(string Name, GltfChannel[] Channels, float Duration);
