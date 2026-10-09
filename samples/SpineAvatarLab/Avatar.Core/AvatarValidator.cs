using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// Semantic validation beyond the JSON schemas (authoring profile, "Semantic validation"): every
/// reference resolves in the representation that claims it, ids are unique, paths parse, keyframes
/// are ordered. It says nothing about how the avatar looks; that takes rendered sheets.
/// </summary>
public static partial class AvatarValidator
{
    private static readonly string[] KnownProfiles = ["ambient", "character", "humanoidAdvanced"];
    private static readonly string[] SpeechModes = ["none", "audioReactive", "canonicalVisemes"];
    private static readonly string[] Spine2dParameters = ["inputLevel", "outputLevel", "inputLow", "inputMid", "inputHigh", "outputLow", "outputMid", "outputHigh"];
    private static readonly string[] SupportedRequiredFeatures = Spine2dVocabulary.Features;

    private static readonly Dictionary<string, string[]> Formats = new()
    {
        ["skia"] = ["spine2d"],
        ["native3d"] = ["glb"],
        ["rive"] = ["riv"],
        ["lottie"] = ["lottie-json", "dotlottie"],
        ["vrm"] = ["vrm"],
        ["web3d"] = ["glb", "vrm"],
    };

    // Binding fields the authoring profile defines for every renderer.
    private static readonly HashSet<string> ProfileBindingFields = ["schemaVersion", "renderer", "poses", "animations", "parameters", "channelMasks", "framing"];

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^(?<node>[^.]+)\.(?<part>fill|stroke)(\.(?<kind>linear|radial|sweep)\.stops\[(?<stop>\d+)\])?$")]
    private static partial Regex ThemeBindingPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")]
    private static partial Regex ColorPattern();

    public static void Validate(AvatarPackage package, AvatarValidationReport report)
    {
        ValidateManifest(package, report);
        foreach (var representation in package.Manifest.Representations)
            ValidateRepresentation(package, representation, report);
    }

    private static void ValidateManifest(AvatarPackage package, AvatarValidationReport report)
    {
        const string area = "manifest";
        var m = package.Manifest;
        var failures = report.Failures.Count();

        if (!m.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
            report.Fail(area, "schemaVersion", $"major version of '{m.SchemaVersion}' is not 1");
        else if (m.SchemaVersion != "1.0")
            report.Warn(area, "schemaVersion", $"{m.SchemaVersion}: fields newer than 1.0 are ignored");

        if (!IdPattern().IsMatch(m.Id))
            report.Fail(area, "id", $"'{m.Id}' does not match ^[a-z0-9][a-z0-9-]{{1,63}}$");
        if (!KnownProfiles.Contains(m.Profile))
            report.Fail(area, "profile", $"'{m.Profile}' is not ambient, character or humanoidAdvanced");
        if (m.Representations.Count is < 1 or > 8)
            report.Fail(area, "representations", $"{m.Representations.Count} representations, 1–8 allowed");
        foreach (var duplicate in m.Representations.GroupBy(r => r.Id).Where(g => g.Count() > 1))
            report.Fail(area, "representations", $"representation id '{duplicate.Key}' is used {duplicate.Count()} times");

        foreach (var state in AvatarVocabulary.States)
        {
            if (!m.States.TryGetValue(state, out var profile))
                report.Fail(area, "states", $"state '{state}' is missing");
            else if (profile.TransitionMs is < 0 or > 2000)
                report.Fail(area, "states", $"{state}.transitionMs {profile.TransitionMs} is outside 0–2000");
        }

        foreach (var expression in AvatarVocabulary.Expressions)
        {
            if (!m.Expressions.ContainsKey(expression))
            {
                if (m.Profile == "ambient")
                    report.Warn(area, "expressions", $"expression '{expression}' is missing; the ambient profile allows that");
                else
                    report.Fail(area, "expressions", $"expression '{expression}' is missing; the {m.Profile} profile needs all eight");
            }
        }

        if (!SpeechModes.Contains(m.Speech.Mode))
            report.Fail(area, "speech", $"mode '{m.Speech.Mode}' is not none, audioReactive or canonicalVisemes");
        if (m.Speech.ExpressionMouthScale is < 0 or > 1 || !double.IsFinite(m.Speech.ExpressionMouthScale))
            report.Fail(area, "speech", $"expressionMouthScale {m.Speech.ExpressionMouthScale} is outside 0–1");
        if (m.Speech.ReleaseMs is < 0 or > 1000)
            report.Fail(area, "speech", $"releaseMs {m.Speech.ReleaseMs} is outside 0–1000");
        foreach (var key in m.Speech.Mapping.Keys)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id is < 0 or > 14)
                report.Fail(area, "speech", $"mapping key '{key}' is not a canonical id 0–14");
        }
        if (m.Speech.Mode == "canonicalVisemes")
        {
            var missing = Enumerable.Range(0, 15).Where(i => !m.Speech.Mapping.ContainsKey(i.ToString(CultureInfo.InvariantCulture))).ToList();
            if (missing.Count > 0)
                report.Fail(area, "speech", $"canonicalVisemes leaves ids {string.Join(", ", missing)} unmapped");
            else
            {
                var shared = m.Speech.Mapping.GroupBy(p => p.Value).Where(g => g.Count() > 1).Select(g => $"{g.Key} ← {string.Join("/", g.Select(p => p.Key))}");
                report.Pass(area, "speech", $"all 15 canonical ids mapped; shared poses: {(shared.Any() ? string.Join("; ", shared) : "none")}");
            }
        }

        if (m.Motions.BlinkIntervalSeconds is { } blink && (blink.Count != 2 || blink[0] < 0.1 || blink[0] > blink[1]))
            report.Fail(area, "motions", "blinkIntervalSeconds must be [min, max] with 0.1 ≤ min ≤ max");

        foreach (var (name, slot) in m.Themes.Slots)
        {
            if (!ColorPattern().IsMatch(slot.Light) || !ColorPattern().IsMatch(slot.Dark))
                report.Fail(area, "themes", $"slot '{name}' has a colour that is not #RRGGBB or #RRGGBBAA");
        }

        foreach (var (label, path) in new[] { ("posters.light", m.Posters.Light), ("posters.dark", m.Posters.Dark), ("license", m.License), ("provenance", m.Provenance) })
        {
            if (!package.TryGetFile(path, out _))
                report.Fail(area, label, $"{path} is not in the archive");
        }

        if (report.Failures.Count() == failures)
            report.Pass(area, "manifest", $"{m.DisplayName} {m.AssetVersion}, {m.Profile}, {m.Representations.Count} representation(s)");
    }

    private static void ValidateRepresentation(AvatarPackage package, AvatarRepresentation r, AvatarValidationReport report)
    {
        var area = r.Id;
        var m = package.Manifest;
        var failures = report.Failures.Count();

        if (!Formats.TryGetValue(r.Renderer, out var formats) || !formats.Contains(r.Format))
            report.Fail(area, "format", $"renderer '{r.Renderer}' with format '{r.Format}' is not a known combination");
        foreach (var feature in r.RequiredFeatures ?? [])
        {
            if (!SupportedRequiredFeatures.Contains(feature))
                report.Fail(area, "requiredFeatures", $"'{feature}' is required but not supported by this runtime");
        }
        foreach (var path in new[] { r.Model, r.Bindings })
        {
            if (AvatarArchive.UnsafeName(path) is { } reason)
                report.Fail(area, "path", $"{path}: {reason}");
            else if (!package.TryGetFile(path, out _))
                report.Fail(area, "path", $"{path} is not in the archive");
        }
        if (report.Failures.Count() > failures)
            return;

        AvatarBindings? bindings = null;
        if (!report.Try(area, "bindings", () => bindings = package.GetBindings(r)) || bindings is null)
            return;

        if (bindings.Extra is { Count: > 0 } extra)
            report.Warn(area, "bindings", $"fields outside the authoring profile: {string.Join(", ", extra.Keys.Where(k => !ProfileBindingFields.Contains(k)))}");
        if (bindings.Framing?.Extra is { Count: > 0 } framingExtra)
            report.Warn(area, "bindings", $"framing fields outside the authoring profile: {string.Join(", ", framingExtra.Keys)}");

        // Pose references: what the manifest names must exist in this representation's bindings.
        var poseRefs = new List<(string Owner, string Pose)>();
        foreach (var (state, profile) in m.States)
            if (profile.Pose is { } pose) poseRefs.Add(($"states.{state}", pose));
        foreach (var (expression, profile) in m.Expressions)
            poseRefs.Add(($"expressions.{expression}", profile.Pose));
        poseRefs.Add(("reducedMotion", m.ReducedMotion.Pose));
        if (r.Has("speechArticulation"))
        {
            foreach (var (id, pose) in m.Speech.Mapping)
                poseRefs.Add(($"speech.mapping.{id}", pose));
            if (m.Speech.Mode != "canonicalVisemes")
                report.Fail(area, "capabilities", "claims speechArticulation, but speech.mode is not canonicalVisemes");
        }
        foreach (var (owner, pose) in poseRefs)
        {
            if (!bindings.Poses.ContainsKey(pose))
                report.Fail(area, "poses", $"{owner} names pose '{pose}', which the bindings do not define");
        }

        IReadOnlyDictionary<string, double>? clips = null;
        report.Try(area, "model", () => clips = package.GetClipDurations(r));
        clips ??= new Dictionary<string, double>();

        var animationRefs = new List<(string Owner, string Animation)>();
        foreach (var idle in m.Motions.IdleVariants)
            animationRefs.Add(("motions.idleVariants", idle));
        foreach (var (gesture, animation) in m.Motions.Gestures)
            animationRefs.Add(($"motions.gestures.{gesture}", animation));
        foreach (var (state, profile) in m.States)
            if (profile.Animation is { } animation) animationRefs.Add(($"states.{state}.animation", animation));
        foreach (var (state, animation) in bindings.StateAnimations ?? new Dictionary<string, string>())
            animationRefs.Add(($"bindings.stateAnimations.{state}", animation));
        foreach (var (owner, animation) in animationRefs)
        {
            if (!bindings.Animations.ContainsKey(animation))
                report.Fail(area, "animations", $"{owner} names animation '{animation}', which the bindings do not define");
        }
        foreach (var (name, clip) in bindings.Animations)
        {
            if (!clips.ContainsKey(clip))
                report.Fail(area, "animations", $"bindings animation '{name}' points at clip '{clip}', which the model does not have");
        }

        if (r.Has("blink") && r.BlinkOwner == "scheduler" && !bindings.Animations.ContainsKey("blink") && bindings.Extra?.ContainsKey("blink") != true)
            report.Fail(area, "capabilities", "claims blink owned by the scheduler, but there is neither a blink clip nor a blink binding");
        if (r.Has("gaze") && r.GazeOwner == "scheduler" && bindings.Extra?.ContainsKey("gaze") != true)
            report.Warn(area, "capabilities", "claims gaze owned by the scheduler, but has no gaze binding; gaze comes only from poses and clips");

        switch (r.Format)
        {
            case "spine2d":
                ValidateSpine2d(package, r, bindings, report);
                break;
            case "glb":
                ValidateGlb(package, r, bindings, report);
                break;
            default:
                report.NotMeasured(area, "model", $"no validator for {r.Format} in this runtime");
                break;
        }

        if (report.Failures.Count() == failures)
            report.Pass(area, "references", $"{poseRefs.Count} pose and {animationRefs.Count} animation references resolve");
    }

    private static void ValidateSpine2d(AvatarPackage package, AvatarRepresentation r, AvatarBindings bindings, AvatarValidationReport report)
    {
        var area = r.Id;
        Spine2dScene? scene = null;
        if (!report.Try(area, "scene", () => scene = package.GetScene(r)) || scene is null)
            return;

        var failures = report.Failures.Count();
        var path = r.Model;

        if (!Spine2dVocabulary.SchemaVersions.Contains(scene.SchemaVersion))
            report.Fail(area, "scene", $"{path}: schemaVersion '{scene.SchemaVersion}' is not {string.Join(" or ", Spine2dVocabulary.SchemaVersions)}");

        // A 1.1 feature in the scene must be declared, so an older runtime refuses the file instead of drawing it wrong.
        var declared11 = r.RequiredFeatures ?? [];
        void Uses(string feature, string where)
        {
            if (!declared11.Contains(feature))
                report.Fail(area, "requiredFeatures", $"{path}: {where} uses '{feature}', which the representation does not list in requiredFeatures");
        }
        if (!(scene.Bounds.Width > 0 && scene.Bounds.Height > 0 && double.IsFinite(scene.Bounds.Width) && double.IsFinite(scene.Bounds.Height)))
            report.Fail(area, "scene", $"{path}: bounds must be positive and finite");
        if (scene.Nodes.Count is < 1 or > 128)
            report.Fail(area, "scene", $"{path}: {scene.Nodes.Count} nodes, 1–128 allowed");

        var nodes = new Dictionary<string, Spine2dNode>(StringComparer.Ordinal);
        var nodePaths = new Dictionary<string, AvatarPathData>(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in scene.Nodes)
        {
            if (!nodes.TryAdd(node.Id, node))
            {
                report.Fail(area, "scene", $"{path}: node id '{node.Id}' is used twice");
                continue;
            }
            if (!Spine2dVocabulary.NodeTypes.Contains(node.Type))
                report.Fail(area, "scene", $"{path}: node '{node.Id}' has type '{node.Type}'");
            if (node.Parent is { } parent && !declared.Contains(parent))
                report.Fail(area, "scene", $"{path}: node '{node.Id}' has parent '{parent}', which is not declared before it (or is a cycle)");
            declared.Add(node.Id);

            if (node.Opacity is < 0 or > 1 || !Finite(node.Transform))
                report.Fail(area, "scene", $"{path}: node '{node.Id}' has an opacity outside 0–1 or a non-finite transform");

            switch (node.Type)
            {
                case "ellipse" or "roundedRect" when node.Geometry is not { Width: > 0, Height: > 0 }:
                    report.Fail(area, "scene", $"{path}: {node.Type} '{node.Id}' needs a positive width and height");
                    break;
                case "path" when node.Geometry?.Path is not { } data:
                    report.Fail(area, "scene", $"{path}: path '{node.Id}' has no path data");
                    break;
                case "path":
                    report.Try(area, "scene", () => nodePaths[node.Id] = AvatarPathData.Parse(node.Geometry!.Path!, $"{path}: node {node.Id}"));
                    break;
            }

            if (node.Type != "group")
            {
                void Paint(string what, string? slot, string? color)
                {
                    if (slot is null && color is null)
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' {what} has neither slot nor color");
                    else if (slot is not null && !package.Manifest.Themes.Slots.ContainsKey(slot))
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' {what} uses slot '{slot}', which themes.slots does not define");
                    else if (color is not null && !ColorPattern().IsMatch(color))
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' {what} has colour '{color}'");
                }
                void Stops(string what, IReadOnlyList<Spine2dStop> stops)
                {
                    Uses("gradients", $"node '{node.Id}'");
                    if (stops.Count is < 2 or > 8)
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' {what} has {stops.Count} stops, 2–8 allowed");
                    for (var i = 0; i < stops.Count; i++)
                    {
                        if (stops[i].Offset is < 0 or > 1 || (i > 0 && stops[i].Offset < stops[i - 1].Offset) || stops[i].Opacity is < 0 or > 1)
                            report.Fail(area, "scene", $"{path}: node '{node.Id}' {what} stop {i} is out of order or outside 0–1");
                        Paint($"{what} stop {i}", stops[i].Slot, stops[i].Color);
                    }
                }

                if (node.Fill is { Linear: { } linear })
                    Stops("linear gradient", linear.Stops);
                else if (node.Fill is { Sweep: { } sweep })
                    Stops("sweep gradient", sweep.Stops);
                else if (node.Fill is { Radial: { } radial })
                {
                    Stops("radial gradient", radial.Stops);
                    if (!(radial.R > 0))
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' radial gradient needs a positive r");
                }
                else if (node.Fill is { } fill)
                    Paint("fill", fill.Slot, fill.Color);
                else if (node.Stroke is null)
                    report.Fail(area, "scene", $"{path}: node '{node.Id}' has neither fill nor stroke");

                if (node.Stroke is { } stroke)
                {
                    Uses("strokes", $"node '{node.Id}'");
                    if (!(stroke.Width > 0) || stroke.Cap is not ("butt" or "round" or "square"))
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' stroke needs a positive width and a cap of butt, round or square");
                    Paint("stroke", stroke.Slot, stroke.Color);
                }
                if (node.Blur != 0)
                {
                    Uses("blur", $"node '{node.Id}'");
                    if (node.Blur is < 0 or > 40)
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' blur {node.Blur} is outside 0–40");
                }
                if (node.Blend is { } blend && blend != "normal")
                {
                    Uses("blendModes", $"node '{node.Id}'");
                    if (!Spine2dVocabulary.BlendModes.Contains(blend))
                        report.Fail(area, "scene", $"{path}: node '{node.Id}' blend '{blend}' is not one of {string.Join(", ", Spine2dVocabulary.BlendModes)}");
                }
            }
        }

        var pathPoses = new Dictionary<string, AvatarPathData>(StringComparer.Ordinal);
        foreach (var (name, pose) in scene.PathPoses)
        {
            if (pose.Interpolation is not ("linear" or "crossfade"))
                report.Fail(area, "scene", $"{path}: path pose '{name}' has interpolation '{pose.Interpolation}'");
            report.Try(area, "scene", () => pathPoses[name] = AvatarPathData.Parse(pose.Data, $"{path}: pathPoses.{name}"));
        }

        void CheckWrite(string owner, string nodeId, string property, JsonElement value)
        {
            if (!nodes.TryGetValue(nodeId, out var node))
            {
                report.Fail(area, "writes", $"{owner} writes node '{nodeId}', which the scene does not have");
                return;
            }
            if (!Spine2dVocabulary.Properties.Contains(property))
            {
                report.Fail(area, "writes", $"{owner} writes property '{property}' of '{nodeId}'");
                return;
            }
            if (property == "pathPose")
            {
                if (value.ValueKind != JsonValueKind.String || !pathPoses.TryGetValue(value.GetString()!, out var target))
                    report.Fail(area, "writes", $"{owner} sets '{nodeId}' to path pose {value}, which the scene does not define");
                else if (!nodePaths.TryGetValue(nodeId, out var own))
                    report.Fail(area, "writes", $"{owner} sets a path pose on '{nodeId}', which is not a path");
                else if (scene.PathPoses[value.GetString()!].Interpolation == "linear" && !own.HasSameTopology(target))
                    report.Fail(area, "writes", $"{owner}: path pose {value} has other commands than '{nodeId}', so it cannot be interpolated linearly");
            }
            else if (value.ValueKind != JsonValueKind.Number || !double.IsFinite(value.GetDouble()))
                report.Fail(area, "writes", $"{owner} sets {nodeId}.{property} to {value}, not a finite number");
        }

        foreach (var write in scene.DefaultPose ?? [])
            CheckWrite("defaultPose", write.Node, write.Property, write.Value);

        foreach (var (name, animation) in scene.Animations)
        {
            if (!(animation.DurationSeconds > 0) || !double.IsFinite(animation.DurationSeconds))
                report.Fail(area, "animations", $"{path}: clip '{name}' has duration {animation.DurationSeconds}");
            foreach (var track in animation.Tracks)
            {
                if (track.Keyframes.Count == 0)
                {
                    report.Fail(area, "animations", $"{path}: clip '{name}' has a track on {track.Node}.{track.Property} with no keyframes");
                    continue;
                }
                for (var i = 0; i < track.Keyframes.Count; i++)
                {
                    var key = track.Keyframes[i];
                    if (key.Seconds < 0 || key.Seconds > animation.DurationSeconds + 1e-9 || (i > 0 && key.Seconds <= track.Keyframes[i - 1].Seconds))
                        report.Fail(area, "animations", $"{path}: clip '{name}' {track.Node}.{track.Property} keyframe {i} at {key.Seconds}s is out of order or outside 0–{animation.DurationSeconds}s");
                    if (!Spine2dVocabulary.Easings.Contains(key.Easing))
                        report.Fail(area, "animations", $"{path}: clip '{name}' keyframe {i} has easing '{key.Easing}'");
                    CheckWrite($"clip {name}", track.Node, track.Property, key.Value);
                }
                if (animation.Loop && track.Property != "pathPose" && track.Keyframes[0].Value.ValueKind == JsonValueKind.Number)
                {
                    // A rotation that ends a whole turn on is continuous; anything else must end where it began.
                    var jump = track.Keyframes[^1].Value.GetDouble() - track.Keyframes[0].Value.GetDouble();
                    if (track.Property == "rotation")
                        jump = Math.IEEERemainder(jump, Math.Tau);
                    if (Math.Abs(jump) > 1e-6)
                        report.Warn(area, "animations", $"{path}: looping clip '{name}' jumps by {jump:0.###} on {track.Node}.{track.Property} at the loop boundary");
                }
            }
        }

        foreach (var (pose, element) in bindings.Poses)
        {
            report.Try(area, "poses", () =>
            {
                foreach (var write in AvatarJson.ReadWrites(element, $"{r.Bindings}: poses.{pose}"))
                    CheckWrite($"pose {pose}", write.Node, write.Property, write.Value);
            });
        }

        foreach (var (parameter, element) in bindings.Parameters ?? new Dictionary<string, JsonElement>())
        {
            if (!Spine2dParameters.Contains(parameter))
            {
                report.Fail(area, "parameters", $"parameter '{parameter}' is not one of {string.Join(", ", Spine2dParameters)}");
                continue;
            }
            report.Try(area, "parameters", () =>
            {
                foreach (var write in AvatarJson.ReadParameters(element, $"{r.Bindings}: parameters.{parameter}"))
                {
                    if (!nodes.ContainsKey(write.Node) || write.Property is "pathPose" || !Spine2dVocabulary.Properties.Contains(write.Property))
                        report.Fail(area, "parameters", $"{parameter} maps to {write.Node}.{write.Property}, which cannot take a level");
                    if (!double.IsFinite(write.Min) || !double.IsFinite(write.Max))
                        report.Fail(area, "parameters", $"{parameter} has a non-finite range");
                }
            });
        }

        var channels = scene.Nodes.Select(n => n.Channel).ToHashSet(StringComparer.Ordinal);
        foreach (var (layer, mask) in bindings.ChannelMasks ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            var unknown = mask.Where(c => !channels.Contains(c)).ToList();
            if (unknown.Count > 0)
                report.Warn(area, "channelMasks", $"mask '{layer}' names channels no node has: {string.Join(", ", unknown)}");
        }

        // A pose written outside its layer's mask is dropped at runtime; say so here.
        CheckMask("expression", package.Manifest.Expressions.Values.Select(e => e.Pose));
        CheckMask("speech", package.Manifest.Speech.Mapping.Values);

        void CheckMask(string layer, IEnumerable<string> poses)
        {
            if (bindings.ChannelMasks?.TryGetValue(layer, out var mask) != true)
                return;
            foreach (var pose in poses.Distinct())
            {
                if (!bindings.Poses.TryGetValue(pose, out var element))
                    continue;
                foreach (var write in AvatarJson.ReadWrites(element, pose))
                {
                    if (nodes.TryGetValue(write.Node, out var node) && !mask!.Contains(node.Channel))
                        report.Warn(area, "channelMasks", $"pose '{pose}' writes {write.Node} (channel {node.Channel}) outside the {layer} mask; it will be dropped");
                }
            }
        }

        // Bindings document where a slot is used; the slot named in the fill, stop or stroke is what draws.
        foreach (var (slotName, slot) in package.Manifest.Themes.Slots)
        {
            foreach (var binding in slot.Bindings)
            {
                var match = ThemeBindingPattern().Match(binding);
                if (!match.Success || !nodes.TryGetValue(match.Groups["node"].Value, out var node))
                {
                    report.Fail(area, "themes", $"slot '{slotName}' binds '{binding}', which is not <node>.fill, <node>.fill.linear|radial.stops[i] or <node>.stroke of a scene node");
                    continue;
                }

                var used = match.Groups["part"].Value switch
                {
                    "stroke" => node.Stroke?.Slot,
                    _ when match.Groups["stop"].Success => (match.Groups["kind"].Value switch { "linear" => node.Fill?.Linear?.Stops, "sweep" => node.Fill?.Sweep?.Stops, _ => node.Fill?.Radial?.Stops })
                        ?.ElementAtOrDefault(int.Parse(match.Groups["stop"].Value, CultureInfo.InvariantCulture))?.Slot,
                    _ => node.Fill?.Slot,
                };
                if (used != slotName)
                    report.Warn(area, "themes", $"slot '{slotName}' binds {binding}, but that paint uses '{used ?? "no slot"}'");
            }
        }

        if (report.Failures.Count() == failures)
            report.Pass(area, "scene", $"{scene.Nodes.Count} nodes, {scene.PathPoses.Count} path poses, {scene.Animations.Count} clips, {bindings.Poses.Count} poses");
    }

    private static void ValidateGlb(AvatarPackage package, AvatarRepresentation r, AvatarBindings bindings, AvatarValidationReport report)
    {
        var area = r.Id;
        AvatarGlb? glb = null;
        if (!report.Try(area, "glb", () => glb = package.GetGlb(r)) || glb is null)
            return;

        var failures = report.Failures.Count();
        var root = glb.Root;
        var nodeCount = glb.Count("nodes");
        var meshCount = glb.Count("meshes");

        void CheckMorph(string owner, JsonElement write)
        {
            var node = write.TryGetProperty("node", out var nodeValue) && nodeValue.ValueKind == JsonValueKind.Number ? nodeValue.GetInt32() : -1;
            if (node < 0 || node >= nodeCount)
            {
                report.Fail(area, "writes", $"{owner}: node {(write.TryGetProperty("node", out var n) ? n.ToString() : "missing")} is not one of the {nodeCount} nodes");
                return;
            }

            if (write.TryGetProperty("mesh", out var meshValue))
            {
                var mesh = meshValue.GetInt32();
                if (mesh < 0 || mesh >= meshCount)
                {
                    report.Fail(area, "writes", $"{owner}: mesh {mesh} is not one of the {meshCount} meshes");
                    return;
                }
                var nodeElement = root.GetProperty("nodes")[node];
                if (!nodeElement.TryGetProperty("mesh", out var nodeMesh) || nodeMesh.GetInt32() != mesh)
                    report.Fail(area, "writes", $"{owner}: node {node} does not carry mesh {mesh}");

                var primitives = root.GetProperty("meshes")[mesh].GetProperty("primitives");
                var target = write.TryGetProperty("targetIndex", out var t) ? t.GetInt32() : -1;
                var primitiveList = write.TryGetProperty("primitives", out var p) ? p.EnumerateArray().Select(e => e.GetInt32()).ToList() : [0];
                foreach (var primitive in primitiveList)
                {
                    if (primitive < 0 || primitive >= primitives.GetArrayLength())
                    {
                        report.Fail(area, "writes", $"{owner}: mesh {mesh} has no primitive {primitive}");
                        continue;
                    }
                    var targets = primitives[primitive].TryGetProperty("targets", out var ts) ? ts.GetArrayLength() : 0;
                    if (target < 0 || target >= targets)
                        report.Fail(area, "writes", $"{owner}: morph target {target} is outside mesh {mesh} primitive {primitive}'s {targets} targets");
                }

                if (write.TryGetProperty("expectedName", out var expected)
                    && root.GetProperty("meshes")[mesh].TryGetProperty("extras", out var extras)
                    && extras.TryGetProperty("targetNames", out var names)
                    && target >= 0 && target < names.GetArrayLength()
                    && names[target].GetString() != expected.GetString())
                    report.Fail(area, "writes", $"{owner}: target {target} is named '{names[target].GetString()}', expected '{expected.GetString()}'");
            }
            else if (write.TryGetProperty("property", out var property))
            {
                var expectedLength = property.GetString() switch { "rotation" => 4, "translation" or "scale" => 3, _ => -1 };
                if (expectedLength < 0)
                    report.Fail(area, "writes", $"{owner}: transform property '{property}' is not rotation, translation or scale");
                else if (!write.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expectedLength)
                    report.Fail(area, "writes", $"{owner}: {property} needs {expectedLength} numbers");
            }
        }

        foreach (var (pose, element) in bindings.Poses)
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                report.Fail(area, "poses", $"pose '{pose}' is not a list of writes");
                continue;
            }
            foreach (var write in element.EnumerateArray())
                CheckMorph($"pose {pose}", write);
        }

        foreach (var extra in new[] { "blink", "muteBadge", "gaze" })
        {
            if (bindings.Extra?.TryGetValue(extra, out var element) == true)
            {
                foreach (var entry in element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().ToArray() : [element])
                    CheckMorph($"bindings.{extra}", entry);
            }
        }

        if (report.Failures.Count() == failures)
            report.Pass(area, "glb", $"{nodeCount} nodes, {meshCount} meshes, {glb.AnimationDurations.Count} clips, {bindings.Poses.Count} poses, binary {glb.BinaryLength} bytes");
    }

    private static bool Finite(Spine2dTransform? t) =>
        t is null || (double.IsFinite(t.X) && double.IsFinite(t.Y) && double.IsFinite(t.ScaleX) && double.IsFinite(t.ScaleY) && double.IsFinite(t.Rotation));
}
