using Plugin.Maui.Spine.Controls.Avatar.Core;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Skia;

/// <summary>
/// An <c>sksl</c> representation: one SkSL runtime-effect shader that paints the whole avatar every
/// frame from the uniforms in <see cref="AvatarShaderContract"/>. For avatars that are light and
/// motion rather than shapes (flowing aurora ribbons), which keyframed vector parts cannot do.
/// </summary>
public sealed class AvatarShaderModel : ISkiaAvatarModel
{
    private AvatarShaderModel(SKRuntimeEffect effect, AvatarManifest manifest, Dictionary<string, AvatarThemeSlot> themes)
    {
        Effect = effect;
        Manifest = manifest;
        Themes = themes;
    }

    internal SKRuntimeEffect Effect { get; }

    internal AvatarManifest Manifest { get; }

    /// <summary>Uniform name → the theme slot that colours it.</summary>
    internal Dictionary<string, AvatarThemeSlot> Themes { get; }

    public ISkiaAvatarRenderer CreateRenderer() => new AvatarShaderRenderer(this);

    public static AvatarShaderModel Compile(AvatarPackage package, AvatarRepresentation representation)
    {
        var source = System.Text.Encoding.UTF8.GetString(package.GetFile(representation.Model).Span);
        var effect = SKRuntimeEffect.CreateShader(source, out var errors)
            ?? throw new AvatarFormatException(representation.Model, $"the shader does not compile: {errors}");

        var themes = new Dictionary<string, AvatarThemeSlot>(StringComparer.Ordinal);
        foreach (var slot in package.Manifest.Themes.Slots.Values)
            foreach (var binding in slot.Bindings.Where(b => b.StartsWith("uniform:", StringComparison.Ordinal)))
                themes[binding[8..]] = slot;

        var unknown = effect.Uniforms.Where(u => !AvatarShaderContract.Uniforms.ContainsKey(u) && !themes.ContainsKey(u)).ToList();
        if (unknown.Count > 0 || effect.Children.Any())
        {
            effect.Dispose();
            throw new AvatarFormatException(representation.Model, unknown.Count > 0
                ? $"uniforms {string.Join(", ", unknown)} are not in the runtime's contract"
                : "child shaders are not supported");
        }
        return new AvatarShaderModel(effect, package.Manifest, themes);
    }
}

/// <summary>Fills the shader's uniforms from a frame and paints it over the drawing rectangle.</summary>
/// <remarks>
/// Allocates one <see cref="SKShader"/> wrapper per frame (Skia bakes the uniforms into the shader);
/// the uniform arrays are reused.
/// </remarks>
public sealed class AvatarShaderRenderer : ISkiaAvatarRenderer
{
    // Mouth openness per canonical viseme: sil, PP, FF, TH, DD, kk, CH, SS, nn, RR, aa, E, I, O, U.
    private static readonly float[] Openness = [0, 0, 0.2f, 0.3f, 0.4f, 0.45f, 0.4f, 0.3f, 0.35f, 0.4f, 1, 0.6f, 0.5f, 0.8f, 0.5f];

    private readonly AvatarShaderModel _model;
    private readonly HashSet<string> _declared;
    private readonly SKRuntimeEffectUniforms _uniforms;
    private readonly SKPaint _paint = new() { IsAntialias = false };
    // Pose name → index into _names/_weights: the state and expression uniforms.
    private readonly Dictionary<string, int> _poseUniforms = new(StringComparer.Ordinal);
    private readonly List<string> _names = [];
    private float[] _weights = [];
    private readonly Dictionary<string, float> _visemeOpenness = new(StringComparer.Ordinal);
    private readonly float[] _bands = new float[AvatarShaderContract.BandCount];
    private readonly float[] _resolution = new float[2], _gaze = new float[2], _look = new float[2], _spring = new float[3], _accent = new float[4];
    private readonly Dictionary<string, float[]> _themeValues;
    private double _lastTime = double.NaN;
    private float _flow;
    private bool? _themeDark;

    public AvatarShaderRenderer(AvatarShaderModel model)
    {
        _model = model;
        _declared = [.. model.Effect.Uniforms];
        _uniforms = new SKRuntimeEffectUniforms(model.Effect);
        _themeValues = model.Themes.Keys.ToDictionary(k => k, _ => new float[3], StringComparer.Ordinal);

        foreach (var (state, profile) in model.Manifest.States)
            Uniform(profile.Pose ?? state, "state" + Capitalize(state));
        foreach (var (expression, profile) in model.Manifest.Expressions)
            Uniform(profile.Pose, "expr" + Capitalize(expression));
        _weights = new float[_names.Count];
        foreach (var (id, pose) in model.Manifest.Speech.Mapping)
            if (int.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var i) && i is >= 0 and < 15)
                _visemeOpenness[pose] = Openness[i];

        void Uniform(string pose, string name)
        {
            _poseUniforms[pose] = _names.Count;
            _names.Add(name);
        }
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public void Evaluate(AvatarRenderFrame f)
    {
        Array.Clear(_weights);
        foreach (var p in f.Activity)
            if (_poseUniforms.TryGetValue(p.Pose, out var i)) _weights[i] += p.Weight;
        foreach (var p in f.Expression)
            if (_poseUniforms.TryGetValue(p.Pose, out var i)) _weights[i] += p.Weight;
        var mouth = 0f;
        foreach (var p in f.Speech)
            mouth += _visemeOpenness.GetValueOrDefault(p.Pose) * p.Weight;

        // The flow phase integrates a speed, so a state change speeds the motion up without a jump.
        var dt = double.IsNaN(_lastTime) ? 0 : Math.Clamp(f.ElapsedSeconds - _lastTime, 0, 0.1);
        _lastTime = f.ElapsedSeconds;
        var speed = 1 + 1.4f * W("stateThinking") + 0.5f * W("stateSpeaking") + 0.8f * W("stateConnecting") - 0.5f * W("stateMuted");
        _flow += (float)(dt * speed * (f.ReducedMotion ? 0.15 : 1));

        var input = f.InputLevel * f.InputReactiveWeight;
        var output = f.OutputLevel * f.OutputReactiveWeight;
        var source = input >= output ? f.InputBands : f.OutputBands;
        var perBand = AvatarRenderFrame.BandCount / AvatarShaderContract.BandCount;
        for (var b = 0; b < _bands.Length; b++)
        {
            var sum = 0f;
            for (var k = 0; k < perBand; k++)
                sum += source[b * perBand + k];
            _bands[b] = sum / perBand * Math.Max(f.InputReactiveWeight, f.OutputReactiveWeight);
        }

        Set("iTime", (float)f.ElapsedSeconds);
        Set("flow", _flow);
        Set("inLevel", f.InputLevel);
        Set("outLevel", f.OutputLevel);
        Set("energy", Math.Max(input, output));
        Set("bands", _bands);
        Set("mouth", Math.Min(1, mouth));
        for (var i = 0; i < _names.Count; i++)
            Set(_names[i], Math.Min(1, _weights[i]));
        Set("blink", f.Blink);
        Set("micMuted", f.MicMutedWeight);
        _gaze[0] = f.GazeX; _gaze[1] = f.GazeY;
        _look[0] = f.LookX; _look[1] = f.LookY;
        _spring[0] = f.Squash; _spring[1] = f.Tilt; _spring[2] = f.Lift;
        Set("gaze", _gaze);
        Set("look", _look);
        Set("spring", _spring);

        float W(string name) => _names.IndexOf(name) is >= 0 and var i ? _weights[i] : 0;
    }

    public void Draw(SKCanvas canvas, SKRect bounds, bool dark, SKColor? accent = null)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        _resolution[0] = bounds.Width; _resolution[1] = bounds.Height;
        Set("iResolution", _resolution);
        Set("dark", dark ? 1 : 0);
        var a = accent ?? SKColors.Transparent;
        _accent[0] = a.Red / 255f; _accent[1] = a.Green / 255f; _accent[2] = a.Blue / 255f; _accent[3] = a.Alpha / 255f;
        Set("accent", _accent);
        if (_themeDark != dark)
        {
            foreach (var (uniform, slot) in _model.Themes)
            {
                var c = SKColor.Parse(dark ? slot.Dark : slot.Light);
                var values = _themeValues[uniform];
                values[0] = c.Red / 255f; values[1] = c.Green / 255f; values[2] = c.Blue / 255f;
                Set(uniform, values);
            }
            _themeDark = dark;
        }

        using var shader = _model.Effect.ToShader(_uniforms);
        _paint.Shader = shader;
        canvas.Save();
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.DrawRect(0, 0, bounds.Width, bounds.Height, _paint);
        canvas.Restore();
        _paint.Shader = null;
    }

    private void Set(string name, float value)
    {
        if (_declared.Contains(name)) _uniforms[name] = value;
    }

    private void Set(string name, float[] value)
    {
        if (_declared.Contains(name)) _uniforms[name] = value;
    }

    public void Dispose()
    {
        _paint.Dispose();
        _uniforms.Dispose();
    }
}
