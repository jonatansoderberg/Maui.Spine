using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// The <c>avatar.json</c> at the root of a <c>.spineavatar</c> archive, schema 1.0, with the defaults
/// applied (<see cref="AvatarDefaults"/>): every state, expression and the reduced-motion pose are
/// present whether the author wrote them or not.
/// </summary>
public sealed record AvatarManifest(
    string SchemaVersion,
    string Id,
    string DisplayName,
    string AssetVersion,
    string MinRuntimeVersion,
    string Profile,
    IReadOnlyList<AvatarRepresentation> Representations,
    IReadOnlyDictionary<string, AvatarStateProfile> States,
    IReadOnlyDictionary<string, AvatarExpressionProfile> Expressions,
    AvatarSpeechProfile Speech,
    AvatarMotions Motions,
    AvatarThemes Themes,
    AvatarReducedMotion ReducedMotion,
    AvatarPosters Posters,
    string License,
    string Provenance,
    IReadOnlyList<AvatarFileEntry> Files,
    JsonElement? Extensions = null);

/// <param name="Bindings">The bindings file, or null when the representation has none (an <c>sksl</c> shader needs none).</param>
/// <param name="Capabilities">What the representation can do: computed from its bindings and model at load, or the author's list when the manifest declares one.</param>
/// <param name="DeclaredCapabilities">The list the manifest declares, null when it leaves them to be computed.</param>
public sealed record AvatarRepresentation(
    string Id,
    string Renderer,
    string Format,
    string Model,
    string? Bindings,
    IReadOnlyList<string> Capabilities,
    string IdleOwner = "scheduler",
    string BlinkOwner = "scheduler",
    string GazeOwner = "scheduler",
    IReadOnlyList<string>? RequiredFeatures = null,
    IReadOnlyList<string>? DeclaredCapabilities = null)
{
    public bool Has(string capability) => Capabilities.Contains(capability, StringComparer.Ordinal);
}

/// <param name="Declared">False when the pose is the default (the state's name), so a missing pose is an empty one rather than a broken reference.</param>
public sealed record AvatarStateProfile(
    string? Pose = null,
    string? Animation = null,
    bool InputReactive = false,
    bool OutputReactive = false,
    string Gaze = "engaged",
    int TransitionMs = 220,
    bool Declared = true);

/// <param name="Declared">False when the pose is the default <c>expr_&lt;name&gt;</c>; a missing default pose falls back to neutral with a warning.</param>
public sealed record AvatarExpressionProfile(string Pose, IReadOnlyList<string> Channels, int TransitionMs, bool Declared = true);

/// <param name="MappingDeclared">False when the mapping is the standard <c>viseme_*</c> one implied by <c>oculus15-v1</c>.</param>
public sealed record AvatarSpeechProfile(
    string Mode,
    IReadOnlyDictionary<string, string> Mapping,
    double ExpressionMouthScale,
    int ReleaseMs,
    string? CanonicalProfile = null,
    bool MappingDeclared = true);

/// <param name="IdleVariants">Null: the conventional <c>idle_a</c> and <c>idle_b</c> where the model has them (<see cref="AvatarPackage.GetMotions"/>).</param>
/// <param name="Gestures">Null: the conventional gesture names the model has clips for.</param>
public sealed record AvatarMotions(
    IReadOnlyList<string>? IdleVariants,
    IReadOnlyDictionary<string, string>? Gestures,
    bool Seedable = true,
    IReadOnlyList<double>? BlinkIntervalSeconds = null,
    IReadOnlyList<double>? BreathPeriodSeconds = null);

public sealed record AvatarThemes(IReadOnlyDictionary<string, AvatarThemeSlot> Slots);

public sealed record AvatarThemeSlot(string Light, string Dark, IReadOnlyList<string> Bindings);

public sealed record AvatarReducedMotion(string Pose, bool RetainSpeech, int TransitionMs, bool Declared = true);

public sealed record AvatarPosters(string Light, string Dark, string? States = null, string? Expressions = null, string? Visemes = null);

public sealed record AvatarFileEntry(string Path, string Sha256, long Bytes);

/// <summary>
/// A representation's binding file, with the conventions applied (<see cref="AvatarPackage.GetBindings"/>).
/// Poses and parameters stay raw JSON here: their write format is the renderer's (node ids and path
/// poses for Skia, node, mesh and morph-target indices for GLB).
/// </summary>
/// <remarks>A class rather than a positional record: System.Text.Json cannot fill extension data through a constructor.</remarks>
public sealed class AvatarBindings
{
    /// <summary>The prefix of the <see cref="Extensions"/> keys Spine's own renderers read (<c>spine.lighting</c>).</summary>
    public const string SpineExtensionPrefix = "spine.";

    public required string SchemaVersion { get; init; }

    public required string Renderer { get; init; }

    /// <summary>Poses by name. A pose that is not here is empty: it moves nothing.</summary>
    // The source generator sets every init property at construction, so an absent key arrives as null.
    public IReadOnlyDictionary<string, JsonElement> Poses { get => field ?? s_noPoses; init; }

    /// <summary>Animation name → the model's clip. A clip whose name is the animation's needs no entry.</summary>
    public IReadOnlyDictionary<string, string> Animations { get => field ?? s_noAnimations; init; }

    private static readonly Dictionary<string, JsonElement> s_noPoses = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> s_noAnimations = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ChannelMasks { get; init; }

    public AvatarFraming? Framing { get; init; }

    public IReadOnlyDictionary<string, string>? StateAnimations { get; init; }

    /// <summary>Renderer- or model-specific keys, each <c>&lt;namespace&gt;.&lt;name&gt;</c>; Spine's renderers read <c>spine.*</c>.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Extensions { get; init; }

    /// <summary>The schema's other top-level keys (<c>gaze</c>, <c>blink</c>, <c>headNode</c>…) and, in older files, extension keys written at the top level.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>
    /// A binding the rig or a surface reads by name: a top-level key of the schema (<c>gaze</c>), or
    /// an extension under <c>spine.&lt;name&gt;</c>, which files from before the schema wrote at the top level.
    /// </summary>
    public JsonElement? Find(string name) =>
        Extra?.TryGetValue(name, out var value) == true ? value
        : Extensions?.TryGetValue(SpineExtensionPrefix + name, out var extension) == true ? extension
        : null;

    internal static AvatarBindings Empty(string renderer) => new() { SchemaVersion = "1.0", Renderer = renderer };
}

public sealed class AvatarFraming
{
    public double SafeInset { get; init; }

    public string Fit { get; init; } = "contain";

    /// <summary>The GLB camera keys (<c>cameraPosition</c>, <c>lookAt</c>, <c>verticalFov</c>).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The canonical speech ids 0–14 (oculus15-v1) and the expression and state names of schema 1.0.</summary>
public static class AvatarVocabulary
{
    public static readonly string[] Visemes = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"];

    public const int ClosedViseme = 1;

    public const string CanonicalProfile = "oculus15-v1";

    public static readonly string[] States = ["idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted"];

    public static readonly string[] Expressions = ["neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident"];

    /// <summary>The idle clips picked up by name when the manifest lists none.</summary>
    public static readonly string[] IdleVariants = ["idle_a", "idle_b"];

    /// <summary>Gesture names picked up when the manifest lists none, each with the clip names it is found under, first match wins.</summary>
    public static readonly IReadOnlyList<(string Gesture, string[] Clips)> Gestures =
    [
        ("nod", ["nod"]),
        ("shake", ["shake"]),
        ("lean", ["lean", "lean_in"]),
        ("interrupt", ["interrupt"]),
        ("hop", ["hop"]),
        ("wiggle", ["wiggle"]),
        ("thumbsUp", ["thumbsUp", "thumbs_up"]),
        ("jump", ["jump"]),
        ("dance", ["dance"]),
    ];

    public static string Name(AvatarState state) => States[(int)state];

    public static string Name(AvatarExpression expression) => Expressions[(int)expression];

    /// <summary>The pose a canonical viseme maps to under <c>oculus15-v1</c> when the manifest gives no mapping.</summary>
    public static string VisemePose(int canonicalId) => "viseme_" + Visemes[canonicalId];

    public static string ExpressionPose(string expression) => "expr_" + expression;

    public const string ReducedMotionPose = "reduced_motion";
}
