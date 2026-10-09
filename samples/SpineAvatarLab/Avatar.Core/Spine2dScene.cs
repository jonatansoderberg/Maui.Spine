using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>A P0 <c>*.avatar2d.json</c> scene: vector nodes, path poses and keyframed clips.</summary>
public sealed record Spine2dScene(
    string SchemaVersion,
    Spine2dBounds Bounds,
    IReadOnlyList<Spine2dNode> Nodes,
    IReadOnlyDictionary<string, Spine2dPathPose> PathPoses,
    IReadOnlyDictionary<string, Spine2dAnimation> Animations,
    IReadOnlyList<Spine2dWrite>? DefaultPose = null);

public sealed record Spine2dBounds(double Width, double Height);

public sealed record Spine2dNode(
    string Id,
    string Type,
    string Channel,
    string? Parent = null,
    Spine2dTransform? Transform = null,
    double Opacity = 1,
    Spine2dFill? Fill = null,
    Spine2dGeometry? Geometry = null,
    double Blur = 0,
    Spine2dStroke? Stroke = null,
    string? Blend = null);

public sealed record Spine2dTransform(double X = 0, double Y = 0, double ScaleX = 1, double ScaleY = 1, double Rotation = 0);

/// <summary>A solid colour or theme slot, or (spine2d 1.1, feature <c>gradients</c>) a linear or radial gradient.</summary>
public sealed record Spine2dFill(string? Color = null, string? Slot = null, Spine2dLinear? Linear = null, Spine2dRadial? Radial = null, Spine2dSweep? Sweep = null);

/// <summary>An angular gradient around (cx, cy), starting at 3 o'clock and running clockwise; spin the node to swirl it.</summary>
public sealed record Spine2dSweep(double Cx, double Cy, IReadOnlyList<Spine2dStop> Stops);

public sealed record Spine2dLinear(double X0, double Y0, double X1, double Y1, IReadOnlyList<Spine2dStop> Stops);

public sealed record Spine2dRadial(double Cx, double Cy, double R, IReadOnlyList<Spine2dStop> Stops);

public sealed record Spine2dStop(double Offset, string? Slot = null, string? Color = null, double Opacity = 1);

/// <summary>spine2d 1.1, feature <c>strokes</c>.</summary>
public sealed record Spine2dStroke(double Width, string? Slot = null, string? Color = null, string Cap = "round");

public sealed record Spine2dGeometry(double Width = 0, double Height = 0, double Radius = 0, string? Path = null);

public sealed record Spine2dPathPose(string Data, string Interpolation);

public sealed record Spine2dAnimation(double DurationSeconds, bool Loop, IReadOnlyList<Spine2dTrack> Tracks);

public sealed record Spine2dTrack(string Node, string Property, IReadOnlyList<Spine2dKeyframe> Keyframes);

/// <summary><see cref="Value"/> is a number, or a path pose name for <c>pathPose</c> tracks.</summary>
public sealed record Spine2dKeyframe(double Seconds, JsonElement Value, string Easing = "linear");

/// <summary>A pose write in a scene's default pose or a Skia binding: a node property set to a value.</summary>
public sealed record Spine2dWrite(string Node, string Property, JsonElement Value);

/// <summary>A Skia binding parameter: a node property mapped linearly from a normalized level.</summary>
public sealed record Spine2dParameter(string Node, string Property, double Min, double Max);

public static class Spine2dVocabulary
{
    public static readonly string[] SchemaVersions = ["1.0", "1.1"];

    public static readonly string[] Easings = ["linear", "step", "easeIn", "easeOut", "easeInOut", "backOut"];

    public static readonly string[] BlendModes = ["normal", "screen", "multiply", "plus"];

    /// <summary>The spine2d 1.1 features this runtime draws; a representation must declare the ones it uses.</summary>
    public static readonly string[] Features = ["gradients", "blur", "strokes", "blendModes"];

    public static readonly string[] NodeTypes = ["group", "ellipse", "roundedRect", "path"];

    public static readonly string[] Properties = ["x", "y", "scaleX", "scaleY", "rotation", "opacity", "pathPose"];
}
