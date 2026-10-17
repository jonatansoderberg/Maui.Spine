using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// The names that need no bindings entry (decision D9): an animation is the model's clip of the same
/// name, and in a GLB a pose the manifest names is the morph target of that name. What the bindings
/// file writes always wins.
/// </summary>
internal static class AvatarConventions
{
    public static AvatarBindings Apply(AvatarPackage package, AvatarRepresentation representation, AvatarBindings declared)
    {
        var animations = new Dictionary<string, string>(declared.Animations, StringComparer.Ordinal);
        foreach (var clip in package.GetClipDurations(representation).Keys)
            animations.TryAdd(clip, clip);

        var poses = new Dictionary<string, JsonElement>(declared.Poses, StringComparer.Ordinal);
        if (representation.Format == "glb")
        {
            var glb = package.GetGlb(representation);
            foreach (var pose in PoseNames(package.Manifest))
            {
                if (!poses.ContainsKey(pose) && MorphPose(glb, pose) is { } writes)
                    poses[pose] = writes;
            }
        }

        return new AvatarBindings
        {
            SchemaVersion = declared.SchemaVersion,
            Renderer = declared.Renderer,
            Poses = poses,
            Animations = animations,
            Parameters = declared.Parameters,
            ChannelMasks = declared.ChannelMasks,
            Framing = declared.Framing,
            StateAnimations = declared.StateAnimations,
            Extensions = declared.Extensions,
            Extra = declared.Extra,
        };
    }

    /// <summary>Every pose the manifest refers to, by default or by name.</summary>
    public static IEnumerable<string> PoseNames(AvatarManifest manifest) =>
        manifest.States.Select(s => s.Value.Pose ?? s.Key)
            .Concat(manifest.Expressions.Values.Select(e => e.Pose))
            .Concat(manifest.Speech.Mapping.Values)
            .Append(manifest.ReducedMotion.Pose)
            .Distinct(StringComparer.Ordinal);

    // Weight 1 on the target of that name, on every node that carries the mesh and every primitive with the target.
    private static JsonElement? MorphPose(AvatarGlb glb, string pose)
    {
        var root = glb.Root;
        if (!root.TryGetProperty("meshes", out var meshes) || !root.TryGetProperty("nodes", out var nodes))
            return null;

        var json = new StringBuilder("[");
        var meshIndex = 0;
        foreach (var mesh in meshes.EnumerateArray())
        {
            if (mesh.TryGetProperty("extras", out var extras) && extras.TryGetProperty("targetNames", out var names))
            {
                var target = names.EnumerateArray().Select((n, i) => (n, i)).FirstOrDefault(t => t.n.GetString() == pose, (default, -1)).Item2;
                if (target >= 0)
                {
                    var primitives = mesh.GetProperty("primitives").EnumerateArray()
                        .Select((p, i) => (p, i))
                        .Where(p => p.p.TryGetProperty("targets", out var targets) && targets.GetArrayLength() > target)
                        .Select(p => p.i.ToString(CultureInfo.InvariantCulture));
                    var nodeIndex = 0;
                    foreach (var node in nodes.EnumerateArray())
                    {
                        if (node.TryGetProperty("mesh", out var m) && m.GetInt32() == meshIndex)
                        {
                            if (json.Length > 1)
                                json.Append(',');
                            json.Append(CultureInfo.InvariantCulture, $$"""{"node":{{nodeIndex}},"mesh":{{meshIndex}},"primitives":[{{string.Join(",", primitives)}}],"targetIndex":{{target}},"value":1}""");
                        }
                        nodeIndex++;
                    }
                }
            }
            meshIndex++;
        }
        if (json.Length == 1)
            return null;
        using var document = JsonDocument.Parse(json.Append(']').ToString());
        return document.RootElement.Clone();
    }
}
