using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// Rewrites a package's manifest and bindings with the defaults (decision D9): what the defaults and
/// conventions already say is left out, extension keys move under <c>extensions</c>, an <c>sksl</c>
/// representation loses its bindings file. The result loads to the same avatar; the tool's
/// <c>pack</c> writes it, with the hash table recomputed.
/// </summary>
public static class AvatarSlimming
{
    private static readonly string[] FormerTopLevelExtensions = ["lighting", "bloom", "springs", "neckChain", "blinkFrames"];

    /// <summary>The slim <c>avatar.json</c>, with the <c>files</c> table as it was; the caller recomputes it.</summary>
    public static JsonObject Manifest(AvatarPackage package)
    {
        var m = JsonNode.Parse(package.GetFile(AvatarArchive.ManifestPath).Span)!.AsObject();
        var resolved = package.Manifest;

        foreach (var (node, representation) in m["representations"]!.AsArray().Zip(resolved.Representations))
        {
            var r = node!.AsObject();
            r.Remove("platforms");
            if (r["capabilities"] is JsonArray declared
                && declared.Select(c => c!.GetValue<string>()).ToHashSet().SetEquals(AvatarCapabilities.Compute(package, representation)))
                r.Remove("capabilities");
            foreach (var owner in new[] { "idleOwner", "blinkOwner", "gazeOwner" })
                RemoveIf(r, owner, "scheduler");
            if (representation.Format == "sksl")
                r.Remove("bindings");
        }

        if (m["states"] is JsonObject states)
        {
            foreach (var name in AvatarVocabulary.States)
            {
                if (states[name] is not JsonObject state)
                    continue;
                var fallback = AvatarDefaults.State(name);
                RemoveIf(state, "pose", fallback.Pose!);
                RemoveIf(state, "inputReactive", fallback.InputReactive);
                RemoveIf(state, "outputReactive", fallback.OutputReactive);
                RemoveIf(state, "gaze", fallback.Gaze);
                RemoveIf(state, "transitionMs", fallback.TransitionMs);
                RemoveIfEmpty(states, name);
            }
            RemoveIfEmpty(m, "states");
        }

        if (m["expressions"] is JsonObject expressions)
        {
            var channels = AvatarDefaults.ExpressionChannels(resolved.Profile);
            foreach (var name in AvatarVocabulary.Expressions)
            {
                if (expressions[name] is not JsonObject expression)
                    continue;
                RemoveIf(expression, "pose", AvatarVocabulary.ExpressionPose(name));
                if (expression["channels"] is JsonArray written && written.Select(c => c!.GetValue<string>()).SequenceEqual(channels))
                    expression.Remove("channels");
                RemoveIf(expression, "transitionMs", name == "surprised" ? AvatarDefaults.SurprisedTransitionMs : AvatarDefaults.TransitionMs);
                RemoveIfEmpty(expressions, name);
            }
            RemoveIfEmpty(m, "expressions");
        }

        if (m["speech"] is JsonObject speech)
        {
            var mode = speech["mode"]?.GetValue<string>();
            if (mode == "canonicalVisemes" && speech["mapping"] is JsonObject mapping
                && mapping.Count == 15 && Enumerable.Range(0, 15).All(i => mapping[i.ToString(CultureInfo.InvariantCulture)]?.GetValue<string>() == AvatarVocabulary.VisemePose(i)))
            {
                speech.Remove("mapping");
                speech["canonicalProfile"] = AvatarVocabulary.CanonicalProfile;
            }
            if (mode == "none" && speech["mapping"] is JsonObject { Count: 0 })
                speech.Remove("mapping");
            if ((mode == "canonicalVisemes" && speech["canonicalProfile"] is not null) || (mode == "none" && speech["mapping"] is null && speech["canonicalProfile"] is null))
                speech.Remove("mode");
            RemoveIf(speech, "expressionMouthScale", AvatarDefaults.ExpressionMouthScale);
            RemoveIf(speech, "releaseMs", AvatarDefaults.ReleaseMs);
            RemoveIfEmpty(m, "speech");
        }

        if (m["motions"] is JsonObject motions)
        {
            var conventional = Conventional(package, resolved);
            if (motions["idleVariants"] is JsonArray idle && conventional is { } c
                && idle.Select(i => i!.GetValue<string>()).SequenceEqual(c.IdleVariants!))
                motions.Remove("idleVariants");
            if (motions["gestures"] is JsonObject gestures && conventional is { } g
                && gestures.Count == g.Gestures!.Count && gestures.All(p => g.Gestures.GetValueOrDefault(p.Key) == p.Value?.GetValue<string>()))
                motions.Remove("gestures");
            RemoveIf(motions, "seedable", true);
            if (motions["blinkIntervalSeconds"] is JsonArray blink && blink.Select(b => b!.GetValue<double>()).SequenceEqual(AvatarDefaults.BlinkIntervalSeconds))
                motions.Remove("blinkIntervalSeconds");
            RemoveIfEmpty(m, "motions");
        }

        if (m["reducedMotion"] is JsonObject reduced)
        {
            RemoveIf(reduced, "pose", AvatarVocabulary.ReducedMotionPose);
            RemoveIf(reduced, "retainSpeech", true);
            RemoveIf(reduced, "transitionMs", AvatarDefaults.ReducedMotionTransitionMs);
            RemoveIfEmpty(m, "reducedMotion");
        }

        if (m["themes"] is JsonObject { } themes && themes["slots"] is JsonObject { Count: 0 })
            m.Remove("themes");

        return m;
    }

    /// <summary>The slim bindings file of <paramref name="representation"/>, or null when it needs none.</summary>
    public static JsonObject? Bindings(AvatarPackage package, AvatarRepresentation representation)
    {
        if (representation.Format == "sksl" || representation.Bindings is not { } path)
            return null;

        var b = JsonNode.Parse(package.GetFile(path).Span)!.AsObject();
        var clips = package.GetClipDurations(representation);

        if (b["poses"] is JsonObject poses)
        {
            foreach (var name in poses.Where(p => p.Value is JsonArray { Count: 0 }).Select(p => p.Key).ToList())
                poses.Remove(name);
            RemoveIfEmpty(b, "poses");
        }
        if (b["animations"] is JsonObject animations)
        {
            foreach (var name in animations.Where(a => a.Value?.GetValue<string>() == a.Key && clips.ContainsKey(a.Key)).Select(a => a.Key).ToList())
                animations.Remove(name);
            RemoveIfEmpty(b, "animations");
        }
        RemoveIfEmpty(b, "parameters");
        RemoveIfEmpty(b, "channelMasks");

        foreach (var key in FormerTopLevelExtensions)
        {
            if (b[key] is not { } value)
                continue;
            b.Remove(key);
            var extensions = b["extensions"] as JsonObject ?? new JsonObject();
            b["extensions"] = extensions;
            extensions[AvatarBindings.SpineExtensionPrefix + key] = value;
        }
        return b;
    }

    // The idle variants and gestures the conventions alone would give.
    private static AvatarMotions? Conventional(AvatarPackage package, AvatarManifest manifest)
    {
        if (manifest.Representations.Count != 1)
            return null;
        var animations = package.GetBindings(manifest.Representations[0]).Animations;
        var gestures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (gesture, clipNames) in AvatarVocabulary.Gestures)
        {
            if (clipNames.FirstOrDefault(animations.ContainsKey) is { } animation)
                gestures[gesture] = animation;
        }
        return new AvatarMotions([.. AvatarVocabulary.IdleVariants.Where(animations.ContainsKey)], gestures);
    }

    private static void RemoveIf<T>(JsonObject o, string key, T value)
    {
        if (o[key] is JsonValue v && Equal(v, value))
            o.Remove(key);
    }

    private static bool Equal<T>(JsonValue v, T value) => value switch
    {
        string s => v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == s,
        bool b => v.GetValueKind() is JsonValueKind.True or JsonValueKind.False && v.GetValue<bool>() == b,
        int i => v.GetValueKind() == JsonValueKind.Number && v.GetValue<double>() == i,
        double d => v.GetValueKind() == JsonValueKind.Number && v.GetValue<double>() == d,
        _ => false,
    };

    private static void RemoveIfEmpty(JsonObject o, string key)
    {
        if (o[key] is JsonObject { Count: 0 })
            o.Remove(key);
    }
}
