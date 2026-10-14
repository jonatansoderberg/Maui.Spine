using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>The <c>avatar.json</c> at the root of a <c>.spineavatar</c> archive, schema 1.0.</summary>
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

public sealed record AvatarRepresentation(
    string Id,
    string Renderer,
    string Format,
    string Model,
    string Bindings,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> Platforms,
    string IdleOwner,
    string BlinkOwner,
    string GazeOwner,
    IReadOnlyList<string>? RequiredFeatures = null)
{
    public bool Has(string capability) => Capabilities.Contains(capability, StringComparer.Ordinal);
}

public sealed record AvatarStateProfile(
    string? Pose = null,
    string? Animation = null,
    bool InputReactive = false,
    bool OutputReactive = false,
    string Gaze = "engaged",
    int TransitionMs = 180);

public sealed record AvatarExpressionProfile(string Pose, IReadOnlyList<string> Channels, int TransitionMs);

public sealed record AvatarSpeechProfile(
    string Mode,
    IReadOnlyDictionary<string, string> Mapping,
    double ExpressionMouthScale,
    int ReleaseMs,
    string? CanonicalProfile = null);

public sealed record AvatarMotions(
    IReadOnlyList<string> IdleVariants,
    IReadOnlyDictionary<string, string> Gestures,
    bool Seedable,
    IReadOnlyList<double>? BlinkIntervalSeconds = null,
    IReadOnlyList<double>? BreathPeriodSeconds = null);

public sealed record AvatarThemes(IReadOnlyDictionary<string, AvatarThemeSlot> Slots);

public sealed record AvatarThemeSlot(string Light, string Dark, IReadOnlyList<string> Bindings);

public sealed record AvatarReducedMotion(string Pose, bool RetainSpeech, int TransitionMs);

public sealed record AvatarPosters(string Light, string Dark, string? States = null, string? Expressions = null, string? Visemes = null);

public sealed record AvatarFileEntry(string Path, string Sha256, long Bytes);

/// <summary>
/// A representation's binding file. Poses and parameters stay raw JSON here: their write format is
/// the renderer's (node ids and path poses for Skia, node, mesh and morph-target indices for GLB).
/// </summary>
/// <remarks>A class rather than a positional record: System.Text.Json cannot fill extension data through a constructor.</remarks>
public sealed class AvatarBindings
{
    public required string SchemaVersion { get; init; }

    public required string Renderer { get; init; }

    public required IReadOnlyDictionary<string, JsonElement> Poses { get; init; }

    public required IReadOnlyDictionary<string, string> Animations { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Parameters { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ChannelMasks { get; init; }

    public AvatarFraming? Framing { get; init; }

    public IReadOnlyDictionary<string, string>? StateAnimations { get; init; }

    /// <summary>Fields the authoring profile does not define; kept so validation can name them.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class AvatarFraming
{
    public double SafeInset { get; init; }

    public string Fit { get; init; } = "contain";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>The canonical speech ids 0–14 (oculus15-v1) and the expression and state names of schema 1.0.</summary>
public static class AvatarVocabulary
{
    public static readonly string[] Visemes = ["sil", "PP", "FF", "TH", "DD", "kk", "CH", "SS", "nn", "RR", "aa", "E", "I", "O", "U"];

    public const int ClosedViseme = 1;

    public static readonly string[] States = ["idle", "connecting", "listening", "thinking", "speaking", "interrupted", "muted"];

    public static readonly string[] Expressions = ["neutral", "happy", "curious", "thinking", "concerned", "surprised", "apologetic", "confident"];

    public static string Name(AvatarState state) => States[(int)state];

    public static string Name(AvatarExpression expression) => Expressions[(int)expression];
}
