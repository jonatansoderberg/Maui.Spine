using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// What a representation can do, computed from its bindings and model (decision D9). A manifest that
/// declares a list keeps it and is checked against the computed one; one that does not gets the
/// computed list. Either way the computed list is in the validation report.
/// </summary>
public static class AvatarCapabilities
{
    public const string Ambient = "ambient";
    public const string Expressions = "expressions";
    public const string AudioReactive = "audioReactive";
    public const string SpeechArticulation = "speechArticulation";
    public const string Gaze = "gaze";
    public const string Blink = "blink";
    public const string Gestures = "gestures";
    public const string ReducedMotion = "reducedMotion";
    public const string ThemeSlots = "themeSlots";

    public static readonly string[] All = [Ambient, Expressions, AudioReactive, SpeechArticulation, Gaze, Blink, Gestures, ReducedMotion, ThemeSlots];

    private static readonly string[] LevelUniforms = ["inLevel", "outLevel", "energy", "bands", "bandLow", "bandMid", "bandHigh"];

    /// <summary>The capabilities the representation's bindings and model support, in <see cref="All"/>'s order.</summary>
    public static IReadOnlyList<string> Compute(AvatarPackage package, AvatarRepresentation representation)
    {
        var manifest = package.Manifest;
        var bindings = package.GetBindings(representation);
        var found = new HashSet<string>(StringComparer.Ordinal) { Ambient, ReducedMotion };

        if (manifest.Themes.Slots.Count > 0)
            found.Add(ThemeSlots);
        if (package.GetMotions(representation).Gestures!.Count > 0)
            found.Add(Gestures);

        if (representation.Format == "sksl")
        {
            var uniforms = AvatarShaderContract.Declarations(System.Text.Encoding.UTF8.GetString(package.GetFile(representation.Model).Span))
                .Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            if (uniforms.Any(u => u.StartsWith("expr", StringComparison.Ordinal)))
                found.Add(Expressions);
            if (uniforms.Overlaps(LevelUniforms))
                found.Add(AudioReactive);
            if (uniforms.Contains("mouth") && manifest.Speech.Mode == "canonicalVisemes")
                found.Add(SpeechArticulation);
            if (uniforms.Contains("gaze") || uniforms.Contains("look"))
                found.Add(Gaze);
            if (uniforms.Contains("blink"))
                found.Add(Blink);
        }
        else
        {
            if (manifest.Expressions.Values.Any(e => Writes(bindings, e.Pose)))
                found.Add(Expressions);
            if (bindings.Parameters?.Any(p => p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0) == true)
                found.Add(AudioReactive);
            if (manifest.Speech.Mode == "canonicalVisemes" && manifest.Speech.Mapping.Values.Any(p => Writes(bindings, p)))
                found.Add(SpeechArticulation);
            if (bindings.Find("gaze") is not null)
                found.Add(Gaze);
            if (bindings.Find("blink") is not null || bindings.Animations.ContainsKey("blink"))
                found.Add(Blink);
        }

        return [.. All.Where(found.Contains)];
    }

    /// <summary>Puts each representation's capabilities into the package's manifest and reports them.</summary>
    internal static void Apply(AvatarPackage package, AvatarValidationReport report)
    {
        var representations = new List<AvatarRepresentation>();
        foreach (var representation in package.Manifest.Representations)
        {
            IReadOnlyList<string>? computed = null;
            report.Try(representation.Id, "capabilities", () => computed = Compute(package, representation));
            if (computed is null)
            {
                representations.Add(representation);
                continue;
            }

            if (representation.DeclaredCapabilities is { } declared)
            {
                var unsupported = declared.Except(computed).ToList();
                var undeclared = computed.Except(declared).ToList();
                if (unsupported.Count > 0)
                    report.Warn(representation.Id, "capabilities", $"declares {string.Join(", ", unsupported)}, which the bindings and model do not support; drop them or let the validator compute the list");
                if (undeclared.Count > 0)
                    report.Warn(representation.Id, "capabilities", $"supports {string.Join(", ", undeclared)} without declaring it; the declared list is used");
                report.Pass(representation.Id, "capabilities", $"declared: {string.Join(", ", declared)}; computed: {string.Join(", ", computed)}");
                representations.Add(representation);
            }
            else
            {
                report.Pass(representation.Id, "capabilities", $"computed: {string.Join(", ", computed)}");
                representations.Add(representation with { Capabilities = computed });
            }
        }
        package.Manifest = package.Manifest with { Representations = representations };
    }

    private static bool Writes(AvatarBindings bindings, string pose) =>
        bindings.Poses.TryGetValue(pose, out var writes) && writes.ValueKind == JsonValueKind.Array && writes.GetArrayLength() > 0;
}
