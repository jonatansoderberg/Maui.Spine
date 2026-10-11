using System.Text.RegularExpressions;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// The uniforms an <c>sksl</c> representation may declare; the runtime fills each one it finds every
/// frame. A shader declares only what it uses. Theme slots add <c>float3</c> colours named by their
/// <c>uniform:&lt;name&gt;</c> bindings. Anything else fails validation, so a shader written for a
/// newer runtime is refused instead of drawn with zeros.
/// </summary>
public static partial class AvatarShaderContract
{
    public const int BandCount = 8;

    /// <summary>Name → SkSL type.</summary>
    public static readonly IReadOnlyDictionary<string, string> Uniforms = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Pixels of the drawing rectangle; the shader is drawn with its origin at the rectangle's corner.
        ["iResolution"] = "float2",
        // Seconds since the scheduler started, and a phase that runs faster while thinking, speaking
        // or connecting and slows under Reduce Motion: animate with flow so speed changes stay smooth.
        ["iTime"] = "float",
        ["flow"] = "float",
        // Smoothed levels 0–1, and the one the current state reacts to (input while listening, output
        // while speaking), with its spectrum folded into eight log bands, low to high.
        ["inLevel"] = "float",
        ["outLevel"] = "float",
        ["energy"] = "float",
        ["bands"] = $"float[{BandCount}]",
        // The same spectrum in three ranges (bands 0–2, 3–5, 6–7), so a shader need not average per pixel.
        ["bandLow"] = "float", ["bandMid"] = "float", ["bandHigh"] = "float",
        // Mouth openness from the visemes, 0 closed to 1 for aa.
        ["mouth"] = "float",
        ["stateIdle"] = "float", ["stateConnecting"] = "float", ["stateListening"] = "float", ["stateThinking"] = "float",
        ["stateSpeaking"] = "float", ["stateInterrupted"] = "float", ["stateMuted"] = "float",
        ["exprNeutral"] = "float", ["exprHappy"] = "float", ["exprCurious"] = "float", ["exprThinking"] = "float",
        ["exprConcerned"] = "float", ["exprSurprised"] = "float", ["exprApologetic"] = "float", ["exprConfident"] = "float",
        ["blink"] = "float",
        ["micMuted"] = "float",
        // Gaze with saccades, and the eased look direction (radians, x to the viewer's right, y up).
        ["gaze"] = "float2",
        ["look"] = "float2",
        // Springs: squash, tilt, lift.
        ["spring"] = "float3",
        ["dark"] = "float",
        // The app's accent colour, or the shader's own when the app has none (alpha 0).
        ["accent"] = "float4",
    };

    public const string ThemeType = "float3";

    /// <summary>The <c>uniform</c> declarations in a source: name, type and array length (0 for none).</summary>
    public static IEnumerable<(string Name, string Type)> Declarations(string source)
    {
        foreach (Match m in UniformPattern().Matches(source))
        {
            var type = m.Groups["type"].Value;
            yield return (m.Groups["name"].Value, m.Groups["length"].Success ? $"{type}[{m.Groups["length"].Value}]" : type);
        }
    }

    [GeneratedRegex(@"(?m)^\s*(?:layout\([^)]*\)\s*)?uniform\s+(?<type>\w+)\s+(?<name>\w+)\s*(?:\[\s*(?<length>\d+)\s*\])?\s*;")]
    private static partial Regex UniformPattern();
}
