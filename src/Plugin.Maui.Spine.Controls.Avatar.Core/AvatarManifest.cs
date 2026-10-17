using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Controls.Avatar;

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

/// <summary>One way to draw the avatar: a renderer, a model in a format, and what its poses move.</summary>
/// <param name="Id">Unique within the package.</param>
/// <param name="Renderer"><c>native3d</c> or <c>skia</c> in format 1.0.</param>
/// <param name="Format"><c>glb</c> on native3d; <c>sksl</c> or <c>spine2d</c> on skia.</param>
/// <param name="Model">The model's path in the archive.</param>
/// <param name="Bindings">The bindings file, or null when the representation has none (an <c>sksl</c> shader needs none).</param>
/// <param name="Capabilities">What the representation can do: computed from its bindings and model at load, or the author's list when the manifest declares one.</param>
/// <param name="IdleOwner">Who plays the idle motion: <c>scheduler</c>, <c>model</c> or <c>none</c>.</param>
/// <param name="BlinkOwner">Who blinks: <c>scheduler</c>, <c>model</c> or <c>none</c>.</param>
/// <param name="GazeOwner">Who moves the gaze: <c>scheduler</c>, <c>model</c> or <c>none</c>.</param>
/// <param name="RequiredFeatures">spine2d 1.1 features the scene uses; a runtime without one refuses the representation.</param>
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
    /// <summary>Whether the representation has <paramref name="capability"/> (<see cref="AvatarCapabilities"/>).</summary>
    public bool Has(string capability) => Capabilities.Contains(capability, StringComparer.Ordinal);
}

/// <summary>How a state looks and moves, with the defaults applied.</summary>
/// <param name="Pose">The pose shown in the state; the state's name by default.</param>
/// <param name="Animation">An animation played in the state, if any.</param>
/// <param name="InputReactive">Whether the state follows the input level (listening by default).</param>
/// <param name="OutputReactive">Whether the state follows the output level (speaking by default).</param>
/// <param name="Gaze"><c>engaged</c>, <c>processing</c> (thinking by default) or <c>none</c>.</param>
/// <param name="TransitionMs">The crossfade into the state; 220 ms, 90 for interrupted.</param>
/// <param name="Declared">False when the pose is the default (the state's name), so a missing pose is an empty one rather than a broken reference.</param>
public sealed record AvatarStateProfile(
    string? Pose = null,
    string? Animation = null,
    bool InputReactive = false,
    bool OutputReactive = false,
    string Gaze = "engaged",
    int TransitionMs = 220,
    bool Declared = true);

/// <summary>How an expression looks, with the defaults applied.</summary>
/// <param name="Pose">The pose; <c>expr_&lt;name&gt;</c> by default.</param>
/// <param name="Channels">What the pose is documented to move; the bindings' channel masks decide.</param>
/// <param name="TransitionMs">The crossfade; 220 ms, 100 for surprised.</param>
/// <param name="Declared">False when the pose is the default <c>expr_&lt;name&gt;</c>; a missing default pose falls back to neutral with a warning.</param>
public sealed record AvatarExpressionProfile(string Pose, IReadOnlyList<string> Channels, int TransitionMs, bool Declared = true);

/// <summary>How the avatar speaks, with the defaults applied.</summary>
/// <param name="Mode"><c>none</c>, <c>audioReactive</c> or <c>canonicalVisemes</c>.</param>
/// <param name="Mapping">Canonical viseme id ("0"–"14") → pose.</param>
/// <param name="ExpressionMouthScale">How much of an expression's mouth survives while speaking, 0–1.</param>
/// <param name="ReleaseMs">How long a viseme takes to let go.</param>
/// <param name="CanonicalProfile"><c>oculus15-v1</c> when the mode is canonicalVisemes.</param>
/// <param name="MappingDeclared">False when the mapping is the standard <c>viseme_*</c> one implied by <c>oculus15-v1</c>.</param>
public sealed record AvatarSpeechProfile(
    string Mode,
    IReadOnlyDictionary<string, string> Mapping,
    double ExpressionMouthScale,
    int ReleaseMs,
    string? CanonicalProfile = null,
    bool MappingDeclared = true);

/// <summary>Idle variants, gestures and timing.</summary>
/// <param name="IdleVariants">Null: the conventional <c>idle_a</c> and <c>idle_b</c> where the model has them (<see cref="AvatarPackage.GetMotions"/>).</param>
/// <param name="Gestures">Null: the conventional gesture names the model has clips for.</param>
/// <param name="Seedable">Whether the random motion can be seeded for a repeatable run.</param>
/// <param name="BlinkIntervalSeconds">[min, max] seconds between blinks; null for 2.8–6.5.</param>
/// <param name="BreathPeriodSeconds">[min, max] seconds of a breath, for renderers that breathe.</param>
public sealed record AvatarMotions(
    IReadOnlyList<string>? IdleVariants,
    IReadOnlyDictionary<string, string>? Gestures,
    bool Seedable = true,
    IReadOnlyList<double>? BlinkIntervalSeconds = null,
    IReadOnlyList<double>? BreathPeriodSeconds = null);

/// <summary>The colours an app may restyle.</summary>
/// <param name="Slots">Slot name → its colours and where they are bound.</param>
public sealed record AvatarThemes(IReadOnlyDictionary<string, AvatarThemeSlot> Slots);

/// <summary>A themable colour.</summary>
/// <param name="Light">#RRGGBB or #RRGGBBAA in light mode.</param>
/// <param name="Dark">The same in dark mode.</param>
/// <param name="Bindings"><c>material:&lt;i&gt;</c> (glb), <c>uniform:&lt;name&gt;</c> (sksl) or a node's paint (spine2d).</param>
public sealed record AvatarThemeSlot(string Light, string Dark, IReadOnlyList<string> Bindings);

/// <summary>What the avatar does under Reduce Motion.</summary>
/// <param name="Pose">The still pose; <c>reduced_motion</c> by default.</param>
/// <param name="RetainSpeech">Whether the mouth still moves.</param>
/// <param name="TransitionMs">The crossfade into it.</param>
/// <param name="Declared">False when the pose is the default, so a missing pose is an empty one.</param>
public sealed record AvatarReducedMotion(string Pose, bool RetainSpeech, int TransitionMs, bool Declared = true);

/// <summary>The package's pictures: posters (mandatory) and optional sheets.</summary>
/// <param name="Light">The poster in light mode.</param>
/// <param name="Dark">The poster in dark mode.</param>
/// <param name="States">A sheet of the seven states.</param>
/// <param name="Expressions">A sheet of the eight expressions.</param>
/// <param name="Visemes">A sheet of the visemes.</param>
public sealed record AvatarPosters(string Light, string Dark, string? States = null, string? Expressions = null, string? Visemes = null);

/// <summary>A file of the archive with its checksum.</summary>
/// <param name="Path">The path in the archive.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of its bytes.</param>
/// <param name="Bytes">Its size.</param>
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

    /// <summary>The bindings format version, <c>1.0</c>.</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>The renderer the bindings are for; the representation's.</summary>
    public required string Renderer { get; init; }

    /// <summary>Poses by name. A pose that is not here is empty: it moves nothing.</summary>
    // The source generator sets every init property at construction, so an absent key arrives as null.
    public IReadOnlyDictionary<string, JsonElement> Poses { get => field ?? s_noPoses; init; }

    /// <summary>Animation name → the model's clip. A clip whose name is the animation's needs no entry.</summary>
    public IReadOnlyDictionary<string, string> Animations { get => field ?? s_noAnimations; init; }

    private static readonly Dictionary<string, JsonElement> s_noPoses = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> s_noAnimations = new(StringComparer.Ordinal);

    /// <summary>Levels (<c>inputLevel</c>, <c>outputLevel</c>, the bands) → writes from <c>min</c> at 0 to <c>max</c> at 1.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }

    /// <summary>The channels each layer (<c>expression</c>, <c>speech</c>, <c>idle</c>, <c>reflex</c>) may write.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ChannelMasks { get; init; }

    /// <summary>How the model sits in the view.</summary>
    public AvatarFraming? Framing { get; init; }

    /// <summary>A looping animation per state, replacing the idle loop while it shows.</summary>
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

/// <summary>How the model sits in the view.</summary>
public sealed class AvatarFraming
{
    /// <summary>A margin round the model, as a fraction of the view's shorter side.</summary>
    public double SafeInset { get; init; }

    /// <summary><c>contain</c> or <c>cover</c>.</summary>
    public string Fit { get; init; } = "contain";

    /// <summary>The GLB camera keys (<c>cameraPosition</c>, <c>lookAt</c>, <c>verticalFov</c>).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The canonical speech ids 0–14 (oculus15-v1) and the expression and state names of schema 1.0.</summary>
public static class AvatarVocabulary
{
    /// <summary>The canonical viseme names by id.</summary>
    public static readonly string[] Visemes = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"];

    /// <summary>PP, the closure the scheduler holds over its whole interval.</summary>
    public const int ClosedViseme = 1;

    /// <summary>The canonical viseme profile of format 1.0.</summary>
    public const string CanonicalProfile = "oculus15-v1";

    /// <summary>The state names by <see cref="AvatarState"/>.</summary>
    public static readonly string[] States = ["idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted"];

    /// <summary>The expression names by <see cref="AvatarExpression"/>.</summary>
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

    /// <summary>The state's name in the format.</summary>
    public static string Name(AvatarState state) => States[(int)state];

    /// <summary>The expression's name in the format.</summary>
    public static string Name(AvatarExpression expression) => Expressions[(int)expression];

    /// <summary>The pose a canonical viseme maps to under <c>oculus15-v1</c> when the manifest gives no mapping.</summary>
    public static string VisemePose(int canonicalId) => "viseme_" + Visemes[canonicalId];

    /// <summary>The default pose of an expression, <c>expr_&lt;name&gt;</c>.</summary>
    public static string ExpressionPose(string expression) => "expr_" + expression;

    /// <summary>The default reduced-motion pose.</summary>
    public const string ReducedMotionPose = "reduced_motion";
}
