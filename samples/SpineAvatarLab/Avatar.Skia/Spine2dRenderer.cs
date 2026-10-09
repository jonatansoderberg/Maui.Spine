using Plugin.Maui.Spine.Controls.Avatar.Core;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Skia;

/// <summary>
/// Draws a <see cref="Spine2dModel"/> for one view. Each frame is evaluated from the stored neutral
/// baseline, so nothing drifts, into buffers allocated once.
/// </summary>
/// <remarks>
/// Composition, which the authoring profile leaves open and the lab settles: a pose moves a property
/// toward its target by its weight (poses in one layer crossfade); a clip or a level parameter is
/// relative to the node's baseline (offsets for x, y and rotation, factors for scale and opacity), so
/// idle breathing, a nod and a blink add up instead of overwriting each other. Layers run neutral →
/// activity → mic muted → expression → speech → levels → clips, each writing only the nodes its
/// channel mask allows. Expression writes to speech channels are damped while speaking.
/// </remarks>
public sealed class Spine2dRenderer : IDisposable
{
    private const int Stride = 6;

    private readonly Spine2dModel _model;
    private readonly float[] _values;
    private readonly float[] _before;
    private readonly float[] _path;
    private readonly float[] _pathBefore;
    private readonly SKPath?[] _paths;
    private readonly SKMatrix[] _world;
    private readonly float[] _alpha;
    private readonly bool[] _all;
    private readonly bool[] _muteMask;
    private readonly SKPaint _paint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };

    public Spine2dRenderer(Spine2dModel model)
    {
        _model = model;
        var n = model.NodeCount;
        _values = new float[n * Stride];
        _before = new float[n * Stride];
        _path = new float[model.BasePathPoints.Length];
        _pathBefore = new float[model.BasePathPoints.Length];
        _paths = new SKPath?[n];
        for (var i = 0; i < n; i++)
            _paths[i] = model.Kinds[i] == NodeKind.Path ? new SKPath() : null;
        _world = new SKMatrix[n];
        _alpha = new float[n];
        _all = [.. Enumerable.Repeat(true, n)];
        _muteMask = [.. model.Channels.Select(c => c == "muteIndicator")];
        Reset();
    }

    public Spine2dModel Model => _model;

    public void Evaluate(AvatarRenderFrame frame)
    {
        Reset();
        foreach (var write in _model.DefaultPose)
            Set(write, 1);

        BeginLayer();
        foreach (var pose in frame.Activity)
            ApplyPose(pose.Pose, pose.Weight, Mask("activity"));

        BeginLayer();
        ApplyPose(frame.MicMutedPose, frame.MicMutedWeight, _muteMask);

        BeginLayer();
        var damping = 1 + (frame.ExpressionMouthScale - 1) * frame.SpeakingWeight;
        foreach (var pose in frame.Expression)
            ApplyPose(pose.Pose, pose.Weight, Mask("expression"), damping);

        BeginLayer();
        foreach (var pose in frame.Speech)
            ApplyPose(pose.Pose, pose.Weight, Mask("speech"));

        ApplyParameter("inputLevel", frame.InputLevel, frame.InputReactiveWeight);
        ApplyParameter("outputLevel", frame.OutputLevel, frame.OutputReactiveWeight);
        ApplyBands("input", frame.InputBands, frame.InputReactiveWeight);
        ApplyBands("output", frame.OutputBands, frame.OutputReactiveWeight);

        foreach (var clip in frame.Clips)
            ApplyClip(clip);

        Compose();
    }

    public void Draw(SKCanvas canvas, SKRect bounds, bool dark, SKColor? accent = null)
    {
        var inset = _model.SafeInset * Math.Min(bounds.Width, bounds.Height);
        var area = SKRect.Inflate(bounds, -inset, -inset);
        var scale = Math.Min(area.Width / _model.Width, area.Height / _model.Height);
        var fit = SKMatrix.CreateScaleTranslation(
            scale, scale,
            area.MidX - _model.Width * scale / 2,
            area.MidY - _model.Height * scale / 2);

        var save = canvas.Save();
        for (var i = 0; i < _model.NodeCount; i++)
        {
            var kind = _model.Kinds[i];
            if (kind == NodeKind.Group || _alpha[i] <= 0.001f)
                continue;

            var color = Fill(i, dark, accent);
            _paint.Color = color.WithAlpha((byte)Math.Clamp(color.Alpha * _alpha[i], 0, 255));

            canvas.Save();
            var matrix = fit.PreConcat(_world[i]);
            canvas.Concat(in matrix);

            switch (kind)
            {
                case NodeKind.Ellipse:
                    canvas.DrawOval(_model.Bounds[i], _paint);
                    break;
                case NodeKind.RoundedRect:
                    canvas.DrawRoundRect(_model.Bounds[i], _model.Radii[i], _model.Radii[i], _paint);
                    break;
                case NodeKind.Path:
                    canvas.DrawPath(BuildPath(i), _paint);
                    break;
            }
            canvas.Restore();
        }
        canvas.RestoreToCount(save);
    }

    /// <summary>The current value of a node property after <see cref="Evaluate"/>; for tests and the inspector.</summary>
    public float Value(string node, string property)
    {
        var i = Array.IndexOf(_model.NodeIds, node);
        var p = property switch { "x" => 0, "y" => 1, "scaleX" => 2, "scaleY" => 3, "rotation" => 4, "opacity" => 5, _ => throw new ArgumentException(property) };
        return _values[i * Stride + p];
    }

    /// <summary>The evaluated outline of a path node in its own coordinates.</summary>
    public SKRect PathBounds(string node) => BuildPath(Array.IndexOf(_model.NodeIds, node)).TightBounds;

    public void Dispose()
    {
        _paint.Dispose();
        foreach (var path in _paths)
            path?.Dispose();
    }

    private void Reset()
    {
        _model.Base.CopyTo(_values, 0);
        _model.BasePathPoints.CopyTo(_path, 0);
    }

    private void BeginLayer()
    {
        _values.CopyTo(_before, 0);
        _path.CopyTo(_pathBefore, 0);
    }

    private bool[] Mask(string layer) => _model.Masks.TryGetValue(layer, out var mask) ? mask : _all;

    /// <param name="speechDamping">The weight factor for nodes the speech mask covers; an avatar without a speech mask is never damped.</param>
    private void ApplyPose(string name, float weight, bool[] mask, float speechDamping = 1)
    {
        if (weight <= 0 || !_model.Poses.TryGetValue(name, out var writes))
            return;

        var speech = _model.Masks.GetValueOrDefault("speech");
        foreach (var write in writes)
        {
            if (!mask[write.Node])
                continue;
            var w = speech is not null && speech[write.Node] ? weight * speechDamping : weight;
            if (write.Property == NodeProperty.PathPose)
                MorphPath(write.Node, write.PathPose, w, _pathBefore);
            else
            {
                var at = write.Node * Stride + (int)write.Property;
                _values[at] += w * (write.Value - _before[at]);
            }
        }
    }

    private void Set(CompiledWrite write, float weight)
    {
        if (write.Property == NodeProperty.PathPose)
            MorphPath(write.Node, write.PathPose, weight, _path);
        else
            _values[write.Node * Stride + (int)write.Property] = write.Value;
    }

    private void MorphPath(int node, int pose, float weight, float[] from)
    {
        var offset = _model.PathOffsets[node];
        if (offset < 0 || pose < 0)
            return;

        var target = _model.PathPoses[pose].Points;
        if (_model.PathPoseLinear[pose] && target.Length <= _path.Length - offset && _model.PathPoses[pose].Commands.AsSpan().SequenceEqual(_model.PathCommands[node]))
        {
            for (var i = 0; i < target.Length; i++)
                _path[offset + i] += weight * (target[i] - from[offset + i]);
        }
        else if (weight >= 0.5f && target.Length <= _path.Length - offset)
        {
            // Crossfade topology is not drawn as two paths yet; the stronger pose wins.
            target.CopyTo(_path, offset);
        }
    }

    private void ApplyRelative(int node, NodeProperty property, float value, float weight)
    {
        if (property == NodeProperty.PathPose)
            return;

        var at = node * Stride + (int)property;
        var baseline = _model.Base[at];
        switch (property)
        {
            case NodeProperty.X or NodeProperty.Y or NodeProperty.Rotation:
                _values[at] += weight * (value - baseline);
                break;
            case NodeProperty.ScaleX or NodeProperty.ScaleY or NodeProperty.Opacity when Math.Abs(baseline) > 0.001f:
                _values[at] *= 1 + weight * (value / baseline - 1);
                break;
            default:
                _values[at] += weight * (value - _values[at]);
                break;
        }
    }

    private void ApplyParameter(string name, float level, float weight)
    {
        if (weight <= 0 || !_model.Parameters.TryGetValue(name, out var parameters))
            return;
        foreach (var p in parameters)
            ApplyRelative(p.Node, p.Property, p.Min + (p.Max - p.Min) * level, weight);
    }

    private void ApplyBands(string prefix, ReadOnlySpan<float> bands, float weight)
    {
        if (weight <= 0)
            return;
        // Low, mid and high are the averages of the lower, middle and upper eight of the 24 bands.
        ApplyParameter(prefix + "Low", Average(bands[..8]), weight);
        ApplyParameter(prefix + "Mid", Average(bands[8..16]), weight);
        ApplyParameter(prefix + "High", Average(bands[16..]), weight);

        static float Average(ReadOnlySpan<float> values)
        {
            float sum = 0;
            foreach (var v in values) sum += v;
            return sum / values.Length;
        }
    }

    private void ApplyClip(in AvatarClipSample sample)
    {
        if (!_model.Clips.TryGetValue(sample.Clip, out var tracks))
            return;

        var mask = Mask(sample.Layer switch
        {
            AvatarClipLayer.Idle => "idle",
            AvatarClipLayer.Reflex => "reflex",
            AvatarClipLayer.Gesture => "gesture",
            _ => "activity",
        });

        var t = (float)sample.Time;
        foreach (var track in tracks)
        {
            if (!mask[track.Node])
                continue;

            var times = track.Times;
            var last = times.Length - 1;
            int a = 0;
            while (a < last && t > times[a + 1])
                a++;

            if (track.Property == NodeProperty.PathPose)
            {
                var k = a < last && times[a + 1] > times[a] ? (t - times[a]) / (times[a + 1] - times[a]) : 0;
                MorphPath(track.Node, track.PathPoses[k >= 0.5f && a < last ? a + 1 : a], sample.Weight, _path);
                continue;
            }

            float value;
            if (t <= times[0] || last == 0)
                value = track.Values[0];
            else if (a >= last)
                value = track.Values[last];
            else
            {
                var k = (t - times[a]) / (times[a + 1] - times[a]);
                k = track.Easings[a] switch { 1 => k * k * (3 - 2 * k), 2 => 0, _ => k };
                value = track.Values[a] + (track.Values[a + 1] - track.Values[a]) * k;
            }
            ApplyRelative(track.Node, track.Property, value, sample.Weight);
        }
    }

    private void Compose()
    {
        for (var i = 0; i < _model.NodeCount; i++)
        {
            var at = i * Stride;
            var local = SKMatrix.CreateTranslation(_values[at], _values[at + 1])
                .PreConcat(SKMatrix.CreateRotation(_values[at + 4]))
                .PreConcat(SKMatrix.CreateScale(_values[at + 2], _values[at + 3]));
            var parent = _model.Parents[i];
            var opacity = Math.Clamp(_values[at + 5], 0, 1);
            if (parent >= 0)
            {
                _world[i] = _world[parent].PreConcat(local);
                _alpha[i] = _alpha[parent] * opacity;
            }
            else
            {
                _world[i] = local;
                _alpha[i] = opacity;
            }
        }
    }

    private SKColor Fill(int node, bool dark, SKColor? accent)
    {
        var slot = _model.FillSlots[node];
        if (slot < 0)
            return _model.FillColors[node];
        if (slot == _model.AccentSlot && accent is { } a)
            return a;
        return dark ? _model.SlotDark[slot] : _model.SlotLight[slot];
    }

    private SKPath BuildPath(int node)
    {
        var path = _paths[node]!;
        path.Rewind();
        var p = _model.PathOffsets[node];
        foreach (var command in _model.PathCommands[node]!)
        {
            switch (command)
            {
                case AvatarPathCommand.MoveTo:
                    path.MoveTo(_path[p], _path[p + 1]);
                    break;
                case AvatarPathCommand.LineTo:
                    path.LineTo(_path[p], _path[p + 1]);
                    break;
                case AvatarPathCommand.CubicTo:
                    path.CubicTo(_path[p], _path[p + 1], _path[p + 2], _path[p + 3], _path[p + 4], _path[p + 5]);
                    break;
                case AvatarPathCommand.QuadTo:
                    path.QuadTo(_path[p], _path[p + 1], _path[p + 2], _path[p + 3]);
                    break;
                case AvatarPathCommand.Close:
                    path.Close();
                    break;
            }
            p += AvatarPathData.Arity(command);
        }
        return path;
    }
}
