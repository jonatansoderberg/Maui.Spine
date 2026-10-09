using System.Text.Json;
using Plugin.Maui.Spine.Controls.Avatar.Core;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Skia;

internal enum NodeProperty : byte { X, Y, ScaleX, ScaleY, Rotation, Opacity, PathPose }

internal enum NodeKind : byte { Group, Ellipse, RoundedRect, Path }

internal readonly record struct CompiledWrite(int Node, NodeProperty Property, float Value, int PathPose);

internal sealed class CompiledTrack(int node, NodeProperty property, float[] times, float[] values, int[] pathPoses, byte[] easings)
{
    public int Node { get; } = node;
    public NodeProperty Property { get; } = property;
    public float[] Times { get; } = times;
    public float[] Values { get; } = values;
    public int[] PathPoses { get; } = pathPoses;

    /// <summary>0 linear, 1 easeInOut, 2 step: the easing out of each keyframe.</summary>
    public byte[] Easings { get; } = easings;
}

internal readonly record struct CompiledParameter(int Node, NodeProperty Property, float Min, float Max);

/// <summary>
/// A <c>spine2d</c> scene and its Skia bindings compiled once into flat arrays: node indices instead
/// of ids, path data as floats, poses and clips as lists of writes. Immutable, shared by every view
/// that shows the same avatar.
/// </summary>
public sealed class Spine2dModel
{
    private Spine2dModel() { }

    public float Width { get; private set; }

    public float Height { get; private set; }

    public float SafeInset { get; private set; }

    internal int NodeCount { get; private set; }

    internal string[] NodeIds { get; private set; } = [];

    internal NodeKind[] Kinds { get; private set; } = [];

    internal int[] Parents { get; private set; } = [];

    internal string[] Channels { get; private set; } = [];

    /// <summary>Per node: x, y, scaleX, scaleY, rotation, opacity before any pose.</summary>
    internal float[] Base { get; private set; } = [];

    internal SKRect[] Bounds { get; private set; } = [];

    internal float[] Radii { get; private set; } = [];

    /// <summary>Per node: the theme slot index of its fill, or -1 with a literal colour in <see cref="FillColors"/>.</summary>
    internal int[] FillSlots { get; private set; } = [];

    internal SKColor[] FillColors { get; private set; } = [];

    internal string[] SlotNames { get; private set; } = [];

    internal SKColor[] SlotLight { get; private set; } = [];

    internal SKColor[] SlotDark { get; private set; } = [];

    /// <summary>Per path node: its commands and the offset of its points in a node's path buffer; -1 for other nodes.</summary>
    internal AvatarPathCommand[]?[] PathCommands { get; private set; } = [];

    internal int[] PathOffsets { get; private set; } = [];

    internal float[] BasePathPoints { get; private set; } = [];

    internal AvatarPathData[] PathPoses { get; private set; } = [];

    internal bool[] PathPoseLinear { get; private set; } = [];

    internal Dictionary<string, CompiledWrite[]> Poses { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, CompiledTrack[]> Clips { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, CompiledParameter[]> Parameters { get; } = new(StringComparer.Ordinal);

    /// <summary>Per layer name: which nodes the layer may write. A layer without a mask writes every node.</summary>
    internal Dictionary<string, bool[]> Masks { get; } = new(StringComparer.Ordinal);

    internal CompiledWrite[] DefaultPose { get; private set; } = [];

    internal int AccentSlot { get; private set; } = -1;

    public static Spine2dModel Compile(AvatarPackage package, AvatarRepresentation representation)
    {
        var scene = package.GetScene(representation);
        var bindings = package.GetBindings(representation);
        var model = new Spine2dModel
        {
            Width = (float)scene.Bounds.Width,
            Height = (float)scene.Bounds.Height,
            SafeInset = (float)(bindings.Framing?.SafeInset ?? 0),
            NodeCount = scene.Nodes.Count,
        };

        var n = scene.Nodes.Count;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
            index[scene.Nodes[i].Id] = i;

        model.SlotNames = [.. package.Manifest.Themes.Slots.Keys];
        model.SlotLight = [.. package.Manifest.Themes.Slots.Values.Select(s => SKColor.Parse(s.Light))];
        model.SlotDark = [.. package.Manifest.Themes.Slots.Values.Select(s => SKColor.Parse(s.Dark))];
        model.AccentSlot = Array.IndexOf(model.SlotNames, "accent");

        model.NodeIds = new string[n];
        model.Kinds = new NodeKind[n];
        model.Parents = new int[n];
        model.Channels = new string[n];
        model.Base = new float[n * 6];
        model.Bounds = new SKRect[n];
        model.Radii = new float[n];
        model.FillSlots = new int[n];
        model.FillColors = new SKColor[n];
        model.PathCommands = new AvatarPathCommand[]?[n];
        model.PathOffsets = new int[n];

        var points = new List<float>();
        for (var i = 0; i < n; i++)
        {
            var node = scene.Nodes[i];
            model.NodeIds[i] = node.Id;
            model.Kinds[i] = node.Type switch { "ellipse" => NodeKind.Ellipse, "roundedRect" => NodeKind.RoundedRect, "path" => NodeKind.Path, _ => NodeKind.Group };
            model.Parents[i] = node.Parent is { } parent ? index[parent] : -1;
            model.Channels[i] = node.Channel;

            var t = node.Transform ?? new Spine2dTransform();
            model.Base[i * 6 + 0] = (float)t.X;
            model.Base[i * 6 + 1] = (float)t.Y;
            model.Base[i * 6 + 2] = (float)t.ScaleX;
            model.Base[i * 6 + 3] = (float)t.ScaleY;
            model.Base[i * 6 + 4] = (float)t.Rotation;
            model.Base[i * 6 + 5] = (float)node.Opacity;

            if (node.Geometry is { } g)
            {
                model.Bounds[i] = new SKRect((float)-g.Width / 2, (float)-g.Height / 2, (float)g.Width / 2, (float)g.Height / 2);
                model.Radii[i] = (float)g.Radius;
            }

            model.FillSlots[i] = node.Fill?.Slot is { } slot ? Array.IndexOf(model.SlotNames, slot) : -1;
            model.FillColors[i] = node.Fill?.Color is { } color ? SKColor.Parse(color) : SKColors.Transparent;

            model.PathOffsets[i] = -1;
            if (model.Kinds[i] == NodeKind.Path)
            {
                var path = AvatarPathData.Parse(node.Geometry!.Path!, $"{representation.Model}: {node.Id}");
                model.PathCommands[i] = path.Commands;
                model.PathOffsets[i] = points.Count;
                points.AddRange(path.Points);
            }
        }
        model.BasePathPoints = [.. points];

        var poseNames = scene.PathPoses.Keys.ToArray();
        model.PathPoses = [.. scene.PathPoses.Select(p => AvatarPathData.Parse(p.Value.Data, $"{representation.Model}: pathPoses.{p.Key}"))];
        model.PathPoseLinear = [.. scene.PathPoses.Values.Select(p => p.Interpolation == "linear")];

        CompiledWrite Write(string node, string property, JsonElement value)
        {
            var prop = Property(property);
            return prop == NodeProperty.PathPose
                ? new CompiledWrite(index[node], prop, 0, Array.IndexOf(poseNames, value.GetString()))
                : new CompiledWrite(index[node], prop, value.GetSingle(), -1);
        }

        model.DefaultPose = [.. (scene.DefaultPose ?? []).Select(w => Write(w.Node, w.Property, w.Value))];

        foreach (var (name, element) in bindings.Poses)
            model.Poses[name] = [.. AvatarJson.ReadWrites(element, $"{representation.Bindings}: poses.{name}").Select(w => Write(w.Node, w.Property, w.Value))];

        foreach (var (name, clip) in scene.Animations)
        {
            model.Clips[name] = [.. clip.Tracks.Select(track => new CompiledTrack(
                index[track.Node],
                Property(track.Property),
                [.. track.Keyframes.Select(k => (float)k.Seconds)],
                [.. track.Keyframes.Select(k => k.Value.ValueKind == JsonValueKind.Number ? k.Value.GetSingle() : 0)],
                [.. track.Keyframes.Select(k => k.Value.ValueKind == JsonValueKind.String ? Array.IndexOf(poseNames, k.Value.GetString()) : -1)],
                [.. track.Keyframes.Select(k => (byte)(k.Easing switch { "easeInOut" => 1, "step" => 2, _ => 0 }))]))];
        }

        foreach (var (name, element) in bindings.Parameters ?? new Dictionary<string, JsonElement>())
            model.Parameters[name] = [.. AvatarJson.ReadParameters(element, name).Select(p => new CompiledParameter(index[p.Node], Property(p.Property), (float)p.Min, (float)p.Max))];

        foreach (var (layer, channels) in bindings.ChannelMasks ?? new Dictionary<string, IReadOnlyList<string>>())
            model.Masks[layer] = [.. model.Channels.Select(channels.Contains)];

        return model;
    }

    private static NodeProperty Property(string name) => name switch
    {
        "x" => NodeProperty.X,
        "y" => NodeProperty.Y,
        "scaleX" => NodeProperty.ScaleX,
        "scaleY" => NodeProperty.ScaleY,
        "rotation" => NodeProperty.Rotation,
        "opacity" => NodeProperty.Opacity,
        "pathPose" => NodeProperty.PathPose,
        _ => throw new AvatarFormatException(name, "is not a spine2d property"),
    };
}
