using System.Globalization;
using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

// The manifest as written. Everything D9 gives a default is optional here; AvatarDefaults turns it into
// an AvatarManifest. Unknown fields are left to the schema check, which names them.
internal sealed record AvatarManifestDocument(
    string SchemaVersion,
    string Id,
    string DisplayName,
    string AssetVersion,
    string MinRuntimeVersion,
    string Profile,
    IReadOnlyList<AvatarRepresentationDocument> Representations,
    AvatarPosters Posters,
    string License,
    string Provenance,
    IReadOnlyList<AvatarFileEntry> Files,
    IReadOnlyDictionary<string, AvatarStateDocument>? States = null,
    IReadOnlyDictionary<string, AvatarExpressionDocument>? Expressions = null,
    AvatarSpeechDocument? Speech = null,
    AvatarMotionsDocument? Motions = null,
    AvatarThemes? Themes = null,
    AvatarReducedMotionDocument? ReducedMotion = null,
    JsonElement? Extensions = null);

internal sealed record AvatarRepresentationDocument(
    string Id,
    string Renderer,
    string Format,
    string Model,
    string? Bindings = null,
    IReadOnlyList<string>? Capabilities = null,
    IReadOnlyList<string>? Platforms = null,
    string? IdleOwner = null,
    string? BlinkOwner = null,
    string? GazeOwner = null,
    IReadOnlyList<string>? RequiredFeatures = null);

internal sealed record AvatarStateDocument(
    string? Pose = null,
    string? Animation = null,
    bool? InputReactive = null,
    bool? OutputReactive = null,
    string? Gaze = null,
    int? TransitionMs = null);

internal sealed record AvatarExpressionDocument(string? Pose = null, IReadOnlyList<string>? Channels = null, int? TransitionMs = null);

internal sealed record AvatarSpeechDocument(
    string? Mode = null,
    IReadOnlyDictionary<string, string>? Mapping = null,
    double? ExpressionMouthScale = null,
    int? ReleaseMs = null,
    string? CanonicalProfile = null);

internal sealed record AvatarMotionsDocument(
    IReadOnlyList<string>? IdleVariants = null,
    IReadOnlyDictionary<string, string>? Gestures = null,
    bool? Seedable = null,
    IReadOnlyList<double>? BlinkIntervalSeconds = null,
    IReadOnlyList<double>? BreathPeriodSeconds = null);

internal sealed record AvatarReducedMotionDocument(string? Pose = null, bool? RetainSpeech = null, int? TransitionMs = null);

/// <summary>
/// The defaults of format 1.0 (decision D9): an author writes only what differs from them, and a
/// fully explicit manifest means exactly what it says. They are listed in the authoring profile,
/// <c>docs/proposals/spine-avatar.md</c>.
/// </summary>
public static class AvatarDefaults
{
    public const int TransitionMs = 220;
    public const int InterruptedTransitionMs = 90;
    public const int SurprisedTransitionMs = 100;
    public const double ExpressionMouthScale = 0.45;
    public const int ReleaseMs = 80;
    public const int ReducedMotionTransitionMs = 180;
    public static readonly IReadOnlyList<double> BlinkIntervalSeconds = [2.8, 6.5];

    /// <summary>The channels an expression is documented to write when the manifest names none, by profile.</summary>
    public static IReadOnlyList<string> ExpressionChannels(string profile) => profile == "ambient" ? ["ambientPose"] : ["face", "body"];

    public static AvatarStateProfile State(string state) => new(
        Pose: state,
        InputReactive: state == "listening",
        OutputReactive: state == "speaking",
        Gaze: state == "thinking" ? "processing" : "engaged",
        TransitionMs: state == "interrupted" ? InterruptedTransitionMs : TransitionMs,
        Declared: false);

    internal static AvatarManifest Resolve(AvatarManifestDocument d, AvatarValidationReport report)
    {
        var states = new Dictionary<string, AvatarStateProfile>(StringComparer.Ordinal);
        foreach (var name in AvatarVocabulary.States)
        {
            var fallback = State(name);
            states[name] = d.States?.GetValueOrDefault(name) is { } s
                ? new AvatarStateProfile(
                    s.Pose ?? fallback.Pose,
                    s.Animation,
                    s.InputReactive ?? fallback.InputReactive,
                    s.OutputReactive ?? fallback.OutputReactive,
                    s.Gaze ?? fallback.Gaze,
                    s.TransitionMs ?? fallback.TransitionMs,
                    Declared: s.Pose is not null)
                : fallback;
        }

        var channels = ExpressionChannels(d.Profile);
        var expressions = new Dictionary<string, AvatarExpressionProfile>(StringComparer.Ordinal);
        foreach (var name in AvatarVocabulary.Expressions)
        {
            var transition = name == "surprised" ? SurprisedTransitionMs : TransitionMs;
            var e = d.Expressions?.GetValueOrDefault(name);
            expressions[name] = new AvatarExpressionProfile(
                e?.Pose ?? AvatarVocabulary.ExpressionPose(name),
                e?.Channels ?? channels,
                e?.TransitionMs ?? transition,
                Declared: e?.Pose is not null);
        }

        var speech = d.Speech;
        var canonical = speech?.CanonicalProfile;
        var mode = speech?.Mode ?? (canonical is not null || speech?.Mapping is not null ? "canonicalVisemes" : "none");
        var mapping = speech?.Mapping
            ?? (mode == "canonicalVisemes" && (canonical ?? AvatarVocabulary.CanonicalProfile) == AvatarVocabulary.CanonicalProfile
                ? Enumerable.Range(0, 15).ToDictionary(i => i.ToString(CultureInfo.InvariantCulture), AvatarVocabulary.VisemePose)
                : new Dictionary<string, string>());

        var motions = d.Motions;
        var reduced = d.ReducedMotion;

        return new AvatarManifest(
            d.SchemaVersion, d.Id, d.DisplayName, d.AssetVersion, d.MinRuntimeVersion, d.Profile,
            [.. d.Representations.Select(r => Resolve(r, report))],
            states,
            expressions,
            new AvatarSpeechProfile(
                mode,
                mapping,
                speech?.ExpressionMouthScale ?? ExpressionMouthScale,
                speech?.ReleaseMs ?? ReleaseMs,
                canonical ?? (mode == "canonicalVisemes" ? AvatarVocabulary.CanonicalProfile : null),
                MappingDeclared: speech?.Mapping is not null),
            new AvatarMotions(
                motions?.IdleVariants,
                motions?.Gestures,
                motions?.Seedable ?? true,
                motions?.BlinkIntervalSeconds,
                motions?.BreathPeriodSeconds),
            d.Themes ?? new AvatarThemes(new Dictionary<string, AvatarThemeSlot>()),
            new AvatarReducedMotion(
                reduced?.Pose ?? AvatarVocabulary.ReducedMotionPose,
                reduced?.RetainSpeech ?? true,
                reduced?.TransitionMs ?? ReducedMotionTransitionMs,
                Declared: reduced?.Pose is not null),
            d.Posters, d.License, d.Provenance, d.Files, d.Extensions);
    }

    private static AvatarRepresentation Resolve(AvatarRepresentationDocument r, AvatarValidationReport report)
    {
        if (r.Platforms is not null)
            report.Warn(r.Id, "platforms", "platforms is no longer read: the renderer decides where a representation is drawn");
        return new AvatarRepresentation(
            r.Id, r.Renderer, r.Format, r.Model, r.Bindings,
            r.Capabilities ?? [],
            r.IdleOwner ?? "scheduler",
            r.BlinkOwner ?? "scheduler",
            r.GazeOwner ?? "scheduler",
            r.RequiredFeatures,
            r.Capabilities);
    }
}
