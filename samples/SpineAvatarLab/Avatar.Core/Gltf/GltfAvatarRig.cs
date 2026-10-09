using System.Numerics;
using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Gltf;

/// <summary>
/// Turns an <see cref="AvatarRenderFrame"/> into node transforms and morph weights for a GLB avatar,
/// following its native3d bindings. Renderer-neutral: a native engine copies <see cref="Translation"/>,
/// <see cref="Rotation"/>, <see cref="Scale"/> and <see cref="Weights"/> to its scene each frame.
/// </summary>
/// <remarks>
/// Same composition as the Skia renderer and the three.js page: clips first (additive against their
/// first frame for node rigs; replacing each other for skeletal rigs with <c>clipBlend: override</c>,
/// where a gesture fades the rest out over 0.25 s), then activity, expression (mouth targets damped while
/// speaking) and speech poses, each blending from the values at the start of its layer, then the mute
/// badge, blink, level parameters, gaze and the springs. Allocation-free per frame.
/// </remarks>
public sealed class GltfAvatarRig
{
    private const float GestureFade = 0.25f;

    private readonly GltfModel _model;
    private readonly Vector3[] _baseT, _baseS;
    private readonly Quaternion[] _baseR;
    private readonly float[][] _baseWeights;
    private readonly float[][] _before;
    private readonly Dictionary<string, Write[]> _poses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (GltfClip Clip, float[][] First)> _clips = new(StringComparer.Ordinal);
    private readonly Parameter[] _outputParameters, _inputParameters;
    private readonly HashSet<int> _mouthTargets;
    private readonly int _speechMouthMesh, _headNode;
    private readonly bool _override, _blinkByWeight;
    private readonly (int Mesh, int Target, int[] Suppress)? _blink;
    private readonly (int Mesh, int Target)? _muteBadge;
    private readonly (int Node, float Range)? _gaze;
    private readonly float[] _sample = new float[64];

    public GltfAvatarRig(GltfModel model, AvatarBindings bindings)
    {
        _model = model;
        var n = model.Nodes.Length;
        Translation = new Vector3[n];
        Rotation = new Quaternion[n];
        Scale = new Vector3[n];
        _baseT = [.. model.Nodes.Select(x => x.Translation)];
        _baseR = [.. model.Nodes.Select(x => x.Rotation)];
        _baseS = [.. model.Nodes.Select(x => x.Scale)];
        _baseWeights = [.. model.Meshes.Select(m => m.DefaultWeights.Length == m.TargetCount ? m.DefaultWeights.ToArray() : new float[m.TargetCount])];
        Weights = [.. _baseWeights.Select(w => new float[w.Length])];
        _before = [.. _baseWeights.Select(w => new float[w.Length])];

        foreach (var (name, element) in bindings.Poses)
            _poses[name] = [.. element.EnumerateArray().Select(ReadWrite)];

        _override = Extra(bindings, "clipBlend")?.GetString() == "override";
        foreach (var clip in model.Clips)
        {
            // The first frame of every channel, for additive composition.
            var first = clip.Channels.Select(c =>
            {
                var values = new float[c.Width];
                c.Sample(c.Times[0], values);
                return values;
            }).ToArray();
            _clips[clip.Name] = (clip, first);
        }

        _outputParameters = ReadParameters(bindings, "outputLevel");
        _inputParameters = ReadParameters(bindings, "inputLevel");
        _mouthTargets = Extra(bindings, "expressionMouthTargets") is { } targets ? [.. targets.EnumerateArray().Select(t => t.GetInt32())] : [];
        _speechMouthMesh = Extra(bindings, "speechMouthMesh")?.GetInt32() ?? -1;
        _headNode = Extra(bindings, "headNode")?.GetInt32() ?? -1;

        if (Extra(bindings, "blink") is { } blink)
        {
            _blink = (blink.GetProperty("mesh").GetInt32(), blink.GetProperty("targetIndex").GetInt32(),
                blink.TryGetProperty("suppressExpressionTargets", out var s) ? [.. s.EnumerateArray().Select(x => x.GetInt32())] : []);
            _blinkByWeight = true;
        }
        if (Extra(bindings, "muteBadge") is { } mute)
            _muteBadge = (mute.GetProperty("mesh").GetInt32(), mute.GetProperty("targetIndex").GetInt32());
        if (Extra(bindings, "gaze") is { } gaze)
            _gaze = (gaze.GetProperty("node").GetInt32(), gaze.TryGetProperty("rangeMeters", out var r) ? r.GetSingle() : 0.01f);

        (BoundsMin, BoundsMax) = RestBounds(model);
    }

    public Vector3[] Translation { get; }

    public Quaternion[] Rotation { get; }

    public Vector3[] Scale { get; }

    /// <summary>Morph weights per mesh.</summary>
    public float[][] Weights { get; }

    /// <summary>Scale and offset for the whole model from the springs: squash and stretch around its feet, and lift.</summary>
    public Vector3 RootScale { get; private set; } = Vector3.One;

    public Vector3 RootOffset { get; private set; }

    public Vector3 BoundsMin { get; }

    public Vector3 BoundsMax { get; }

    public void Apply(AvatarRenderFrame f)
    {
        _baseT.CopyTo(Translation, 0);
        _baseR.CopyTo(Rotation, 0);
        _baseS.CopyTo(Scale, 0);
        for (var m = 0; m < Weights.Length; m++)
            _baseWeights[m].CopyTo(Weights[m], 0);

        ApplyClips(f);

        BeginLayer();
        foreach (var p in f.Activity)
            ApplyPose(p.Pose, p.Weight, 1);
        BeginLayer();
        var damping = 1 + (f.ExpressionMouthScale - 1) * f.SpeakingWeight;
        foreach (var p in f.Expression)
            ApplyPose(p.Pose, p.Weight, damping);
        BeginLayer();
        foreach (var p in f.Speech)
            ApplyPose(p.Pose, p.Weight, 1);

        if (_muteBadge is { } badge)
            SetWeight(badge.Mesh, badge.Target, Math.Max(Weights[badge.Mesh][badge.Target], f.MicMutedWeight));

        if (_blink is { } blink)
        {
            foreach (var t in blink.Suppress)
                if (t < Weights[blink.Mesh].Length) Weights[blink.Mesh][t] *= 1 - f.Blink;
            SetWeight(blink.Mesh, blink.Target, f.Blink);
        }

        ApplyParameters(_outputParameters, f.OutputLevel, f.OutputReactiveWeight);
        ApplyParameters(_inputParameters, f.InputLevel, f.InputReactiveWeight);

        if (_gaze is { } gaze && gaze.Node < Translation.Length)
        {
            const float full = 12 * MathF.PI / 180;
            Translation[gaze.Node].X += Math.Clamp(f.GazeX / full, -1, 1) * gaze.Range;
            Translation[gaze.Node].Y += Math.Clamp(f.GazeY / full, -1, 1) * gaze.Range;
        }

        var height = BoundsMax.Y - BoundsMin.Y;
        RootScale = new Vector3(1 - f.Squash * 0.6f, 1 + f.Squash, 1 - f.Squash * 0.6f);
        RootOffset = new Vector3(0, BoundsMin.Y * -f.Squash + f.Lift * height, 0);
        if (f.Tilt != 0 && _headNode >= 0)
            Rotation[_headNode] *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -f.Tilt);
    }

    private void ApplyClips(AvatarRenderFrame f)
    {
        var gesture = 0f;
        if (_override)
        {
            foreach (var c in f.Clips)
            {
                if (c.Layer == AvatarClipLayer.Gesture && _clips.TryGetValue(c.Clip, out var g))
                    gesture = Math.Max(gesture, c.Weight * Math.Clamp(Math.Min((float)c.Time, g.Clip.Duration - (float)c.Time) / GestureFade, 0, 1));
            }
        }

        foreach (var c in f.Clips)
        {
            if (c.Clip == "blink" && _blinkByWeight)
                continue;
            if (!_clips.TryGetValue(c.Clip, out var entry))
                continue;
            var weight = _override ? (c.Layer == AvatarClipLayer.Gesture ? gesture : c.Weight * (1 - gesture)) : c.Weight;
            if (weight <= 0)
                continue;

            var channels = entry.Clip.Channels;
            for (var i = 0; i < channels.Length; i++)
            {
                var channel = channels[i];
                if (channel.Node >= Translation.Length || channel.Width > _sample.Length)
                    continue;
                var value = _sample.AsSpan(0, channel.Width);
                channel.Sample((float)c.Time, value);
                var first = entry.First[i];
                var node = channel.Node;

                switch (channel.Path)
                {
                    case GltfPath.Translation:
                        var v = new Vector3(value[0], value[1], value[2]);
                        Translation[node] = _override
                            ? Vector3.Lerp(Translation[node], v, weight)
                            : Translation[node] + (v - new Vector3(first[0], first[1], first[2])) * weight;
                        break;
                    case GltfPath.Rotation:
                        var q = new Quaternion(value[0], value[1], value[2], value[3]);
                        Rotation[node] = _override
                            ? Quaternion.Slerp(Rotation[node], q, weight)
                            : Rotation[node] * Quaternion.Slerp(Quaternion.Identity, Quaternion.Inverse(new Quaternion(first[0], first[1], first[2], first[3])) * q, weight);
                        break;
                    case GltfPath.Scale:
                        var s = new Vector3(value[0], value[1], value[2]);
                        Scale[node] = _override
                            ? Vector3.Lerp(Scale[node], s, weight)
                            : Scale[node] * Vector3.Lerp(Vector3.One, s / new Vector3(first[0], first[1], first[2]), weight);
                        break;
                    case GltfPath.Weights:
                        var mesh = _model.Nodes[node].Mesh;
                        if (mesh < 0)
                            break;
                        var w = Weights[mesh];
                        for (var k = 0; k < Math.Min(w.Length, value.Length); k++)
                            w[k] = _override ? w[k] + (value[k] - w[k]) * weight : w[k] + (value[k] - first[k]) * weight;
                        break;
                }
            }
        }
    }

    private void BeginLayer()
    {
        for (var m = 0; m < Weights.Length; m++)
            Weights[m].CopyTo(_before[m], 0);
    }

    private void ApplyPose(string name, float weight, float damping)
    {
        if (weight <= 0 || !_poses.TryGetValue(name, out var writes))
            return;
        foreach (var w in writes)
        {
            if (w.Target >= 0)
            {
                if (w.Mesh >= Weights.Length || w.Target >= Weights[w.Mesh].Length)
                    continue;
                var d = w.Mesh == _speechMouthMesh && _mouthTargets.Contains(w.Target) ? damping : 1;
                Weights[w.Mesh][w.Target] += weight * d * (w.Value - _before[w.Mesh][w.Target]);
            }
            else
                ApplyTransform(w.Node, w.Property, w.Vector, weight);
        }
    }

    // A pose's transform is relative to the node's rest pose, so it adds to clip motion.
    private void ApplyTransform(int node, GltfPath property, Vector4 value, float weight)
    {
        if (node < 0 || node >= Translation.Length)
            return;
        switch (property)
        {
            case GltfPath.Rotation:
                var delta = Quaternion.Inverse(_baseR[node]) * new Quaternion(value.X, value.Y, value.Z, value.W);
                Rotation[node] *= Quaternion.Slerp(Quaternion.Identity, delta, weight);
                break;
            case GltfPath.Translation:
                Translation[node] += (new Vector3(value.X, value.Y, value.Z) - _baseT[node]) * weight;
                break;
            case GltfPath.Scale:
                Scale[node] *= Vector3.Lerp(Vector3.One, new Vector3(value.X, value.Y, value.Z) / _baseS[node], weight);
                break;
        }
    }

    private void ApplyParameters(Parameter[] parameters, float level, float weight)
    {
        if (weight <= 0)
            return;
        foreach (var p in parameters)
        {
            var value = p.Min + (p.Max - p.Min) * level;
            if (p.Target >= 0)
            {
                if (p.Mesh < Weights.Length && p.Target < Weights[p.Mesh].Length)
                    Weights[p.Mesh][p.Target] = Math.Max(Weights[p.Mesh][p.Target], value.X * weight);
            }
            else
                ApplyTransform(p.Node, p.Property, value, weight);
        }
    }

    private void SetWeight(int mesh, int target, float value)
    {
        if (mesh < Weights.Length && target < Weights[mesh].Length)
            Weights[mesh][target] = value;
    }

    private static JsonElement? Extra(AvatarBindings bindings, string name) =>
        bindings.Extra?.TryGetValue(name, out var value) == true ? value : null;

    private static Write ReadWrite(JsonElement w)
    {
        var node = w.GetProperty("node").GetInt32();
        if (w.TryGetProperty("targetIndex", out var target))
            return new Write(node, w.GetProperty("mesh").GetInt32(), target.GetInt32(), w.GetProperty("value").GetSingle(), default, default);
        return new Write(node, -1, -1, 0, Path(w.GetProperty("property").GetString()), Vector(w.GetProperty("value")));
    }

    private static Parameter[] ReadParameters(AvatarBindings bindings, string name)
    {
        if (bindings.Parameters?.TryGetValue(name, out var element) != true)
            return [];
        return [.. element.EnumerateArray().Select(p => p.TryGetProperty("targetIndex", out var t)
            ? new Parameter(p.GetProperty("node").GetInt32(), p.GetProperty("mesh").GetInt32(), t.GetInt32(), default, new Vector4(p.GetProperty("min").GetSingle()), new Vector4(p.GetProperty("max").GetSingle()))
            : new Parameter(p.GetProperty("node").GetInt32(), -1, -1, Path(p.GetProperty("property").GetString()), Vector(p.GetProperty("min")), Vector(p.GetProperty("max"))))];
    }

    private static GltfPath Path(string? property) => property switch
    {
        "rotation" => GltfPath.Rotation,
        "scale" => GltfPath.Scale,
        _ => GltfPath.Translation,
    };

    private static Vector4 Vector(JsonElement e) => e.GetArrayLength() switch
    {
        4 => new Vector4(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle()),
        _ => new Vector4(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), 0),
    };

    private static (Vector3 Min, Vector3 Max) RestBounds(GltfModel model)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        void Visit(int index, Matrix4x4 parent)
        {
            var node = model.Nodes[index];
            var world = Matrix4x4.CreateScale(node.Scale) * Matrix4x4.CreateFromQuaternion(node.Rotation) * Matrix4x4.CreateTranslation(node.Translation) * parent;
            if (node.Mesh >= 0 && node.Skin < 0)
            {
                foreach (var p in model.Meshes[node.Mesh].Primitives)
                {
                    for (var i = 0; i < p.Positions.Length; i += 3)
                    {
                        var v = Vector3.Transform(new Vector3(p.Positions[i], p.Positions[i + 1], p.Positions[i + 2]), world);
                        min = Vector3.Min(min, v);
                        max = Vector3.Max(max, v);
                    }
                }
            }
            foreach (var child in node.Children)
                Visit(child, world);
        }

        foreach (var root in model.SceneRoots)
            Visit(root, Matrix4x4.Identity);
        return min.X == float.MaxValue ? (Vector3.Zero, Vector3.One) : (min, max);
    }

    private readonly record struct Write(int Node, int Mesh, int Target, float Value, GltfPath Property, Vector4 Vector);

    private readonly record struct Parameter(int Node, int Mesh, int Target, GltfPath Property, Vector4 Min, Vector4 Max);
}
