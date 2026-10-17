using System.Collections.Concurrent;
using System.Text.Json;

namespace Plugin.Maui.Spine.Controls.Avatar;

/// <summary>
/// A loaded, verified avatar. Immutable and safe to share between views: every view keeps its own
/// scheduler and renderer state, never this package's data.
/// </summary>
public sealed class AvatarPackage
{
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    private readonly ConcurrentDictionary<string, object> _parsed = new(StringComparer.Ordinal);

    internal AvatarPackage(AvatarManifest manifest, IReadOnlyDictionary<string, byte[]> files, AvatarValidationReport report)
    {
        Manifest = manifest;
        _files = files;
        Report = report;
    }

    /// <summary>The manifest with its defaults applied and each representation's capabilities computed.</summary>
    public AvatarManifest Manifest { get; internal set; }

    /// <summary>What loading found, every check with its result.</summary>
    public AvatarValidationReport Report { get; }

    /// <summary>The paths of every file in the archive.</summary>
    public IEnumerable<string> FilePaths => _files.Keys;

    /// <summary>A file's bytes.</summary>
    /// <exception cref="AvatarFormatException">The archive has no such file.</exception>
    public ReadOnlyMemory<byte> GetFile(string path) =>
        _files.TryGetValue(path, out var bytes) ? bytes : throw new AvatarFormatException(path, "is not in the archive");

    /// <summary>A file's bytes, if the archive has it.</summary>
    public bool TryGetFile(string path, out ReadOnlyMemory<byte> bytes)
    {
        var found = _files.TryGetValue(path, out var value);
        bytes = value;
        return found;
    }

    /// <summary>
    /// The representation's bindings with the conventions applied: a clip whose name is an animation's
    /// is that animation, and in a GLB a morph target named like a pose (<c>viseme_PP</c>,
    /// <c>expr_happy</c>) is that pose when the file does not define it. No bindings file: empty bindings.
    /// </summary>
    public AvatarBindings GetBindings(AvatarRepresentation representation) =>
        Parsed("bindings:" + representation.Id, () => AvatarConventions.Apply(this, representation, GetDeclaredBindings(representation)));

    /// <summary>The bindings file as written, without the conventions.</summary>
    public AvatarBindings GetDeclaredBindings(AvatarRepresentation representation) =>
        Parsed("declared-bindings:" + representation.Id, () =>
        {
            if (representation.Bindings is not { } path)
                return AvatarBindings.Empty(representation.Renderer);
            var bindings = AvatarJson.ReadBindings(GetFile(path).Span, path);
            return bindings.Renderer == representation.Renderer
                ? bindings
                : throw new AvatarFormatException(path, $"renderer is '{bindings.Renderer}', the representation's is '{representation.Renderer}'");
        });

    /// <summary>The idle variants and gestures this representation plays: the manifest's, or by convention the clips it has.</summary>
    public AvatarMotions GetMotions(AvatarRepresentation representation) =>
        Parsed("motions:" + representation.Id, () =>
        {
            var motions = Manifest.Motions;
            if (motions.IdleVariants is not null && motions.Gestures is not null)
                return motions;
            var animations = GetBindings(representation).Animations;
            var gestures = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (gesture, clips) in AvatarVocabulary.Gestures)
            {
                if (clips.FirstOrDefault(animations.ContainsKey) is { } animation)
                    gestures[gesture] = animation;
            }
            return motions with
            {
                IdleVariants = motions.IdleVariants ?? [.. AvatarVocabulary.IdleVariants.Where(animations.ContainsKey)],
                Gestures = motions.Gestures ?? gestures,
            };
        });

    internal Spine2dScene GetScene(AvatarRepresentation representation) =>
        representation.Format == "spine2d"
            ? Parsed("scene:" + representation.Id, () => AvatarJson.ReadScene(GetFile(representation.Model).Span, representation.Model))
            : throw new InvalidOperationException($"{representation.Id} is {representation.Format}, not spine2d");

    internal AvatarGlb GetGlb(AvatarRepresentation representation) =>
        representation.Format == "glb"
            ? Parsed("glb:" + representation.Id, () => AvatarGlb.Read(GetFile(representation.Model), representation.Model))
            : throw new InvalidOperationException($"{representation.Id} is {representation.Format}, not glb");

    /// <summary>Clip durations by clip name, from the model itself.</summary>
    public IReadOnlyDictionary<string, double> GetClipDurations(AvatarRepresentation representation) =>
        Parsed("clips:" + representation.Id, () => representation.Format switch
        {
            "spine2d" => GetScene(representation).Animations.ToDictionary(a => a.Key, a => a.Value.DurationSeconds, StringComparer.Ordinal),
            "glb" => GetGlb(representation).AnimationDurations,
            _ => (IReadOnlyDictionary<string, double>)new Dictionary<string, double>(),
        });

    /// <summary>
    /// The first representation whose renderer is available: the preferred one if it is listed,
    /// otherwise the manifest's order. Null when none can be shown.
    /// </summary>
    public AvatarRepresentation? SelectRepresentation(IReadOnlyCollection<string> renderers, string? preferredId = null)
    {
        if (preferredId is not null
            && Manifest.Representations.FirstOrDefault(r => r.Id == preferredId) is { } preferred
            && renderers.Contains(preferred.Renderer))
            return preferred;

        return Manifest.Representations.FirstOrDefault(r => renderers.Contains(r.Renderer));
    }

    private T Parsed<T>(string key, Func<T> parse) where T : notnull => (T)_parsed.GetOrAdd(key, _ => parse());
}

/// <summary>The JSON chunk of a GLB 2.0 file, read only far enough to check bindings against it.</summary>
internal sealed class AvatarGlb
{
    private AvatarGlb(JsonDocument json, int binaryLength)
    {
        Json = json;
        BinaryLength = binaryLength;
        AnimationDurations = ReadAnimationDurations(json.RootElement);
    }

    public JsonDocument Json { get; }

    public int BinaryLength { get; }

    public IReadOnlyDictionary<string, double> AnimationDurations { get; }

    public JsonElement Root => Json.RootElement;

    public int Count(string array) => Root.TryGetProperty(array, out var items) && items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0;

    public static AvatarGlb Read(ReadOnlyMemory<byte> bytes, string path)
    {
        var span = bytes.Span;
        if (span.Length < 20 || BitConverter.ToUInt32(span) != 0x46546C67)
            throw new AvatarFormatException(path, "not a GLB file (no glTF magic)");
        if (BitConverter.ToUInt32(span[4..]) != 2)
            throw new AvatarFormatException(path, $"GLB version {BitConverter.ToUInt32(span[4..])}, only 2 is supported");
        if (BitConverter.ToUInt32(span[8..]) != span.Length)
            throw new AvatarFormatException(path, $"GLB header says {BitConverter.ToUInt32(span[8..])} bytes, the file has {span.Length}");

        var jsonLength = (int)BitConverter.ToUInt32(span[12..]);
        if (BitConverter.ToUInt32(span[16..]) != 0x4E4F534A || 20 + jsonLength > span.Length)
            throw new AvatarFormatException(path, "the first GLB chunk is not a JSON chunk");

        var binaryLength = 0;
        var next = 20 + jsonLength;
        if (next + 8 <= span.Length && BitConverter.ToUInt32(span[(next + 4)..]) == 0x004E4942)
            binaryLength = (int)BitConverter.ToUInt32(span[next..]);

        try
        {
            var json = JsonDocument.Parse(bytes.Slice(20, jsonLength), new JsonDocumentOptions { MaxDepth = 64 });
            var glb = new AvatarGlb(json, binaryLength);
            if (glb.Root.TryGetProperty("extensionsRequired", out var required) && required.GetArrayLength() > 0)
                throw new AvatarFormatException(path, $"requires glTF extensions the lab does not support: {required}");
            return glb;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException or FormatException)
        {
            throw new AvatarFormatException(path, e.Message, e);
        }
    }

    private static Dictionary<string, double> ReadAnimationDurations(JsonElement root)
    {
        var durations = new Dictionary<string, double>(StringComparer.Ordinal);
        if (!root.TryGetProperty("animations", out var animations) || !root.TryGetProperty("accessors", out var accessors))
            return durations;

        foreach (var animation in animations.EnumerateArray())
        {
            var name = animation.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null)
                continue;

            double duration = 0;
            foreach (var sampler in animation.GetProperty("samplers").EnumerateArray())
            {
                var input = accessors[sampler.GetProperty("input").GetInt32()];
                if (input.TryGetProperty("max", out var max))
                    duration = Math.Max(duration, max[0].GetDouble());
            }
            durations[name] = duration;
        }
        return durations;
    }
}
