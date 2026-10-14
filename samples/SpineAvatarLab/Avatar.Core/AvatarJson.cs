using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

// Required constructor parameters and nullable annotations are enforced, so a manifest missing a
// field fails with its JSON path instead of loading with a null that breaks a frame later.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true,
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    MaxDepth = 32,
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(AvatarManifest))]
[JsonSerializable(typeof(AvatarBindings))]
[JsonSerializable(typeof(Spine2dScene))]
[JsonSerializable(typeof(Spine2dWrite[]))]
[JsonSerializable(typeof(Spine2dParameter[]))]
[JsonSerializable(typeof(AvatarCueFixture))]
internal sealed partial class AvatarJsonContext : JsonSerializerContext;

/// <summary>The <c>timed-cues.json</c> fixture format of the reference bundle.</summary>
public sealed record AvatarCueFixture(
    string AudioFile,
    int SampleRate,
    string Format,
    int Channels,
    double DurationSeconds,
    IReadOnlyList<AvatarFixtureCue> Cues,
    string? Description = null);

public sealed record AvatarFixtureCue(string Segment, long Generation, double OffsetSeconds, double DurationSeconds, int CanonicalViseme, float Strength);

public static class AvatarJson
{
    public static AvatarManifest ReadManifest(ReadOnlySpan<byte> utf8) => Read(utf8, AvatarJsonContext.Default.AvatarManifest, "avatar.json");

    public static AvatarBindings ReadBindings(ReadOnlySpan<byte> utf8, string path) => Read(utf8, AvatarJsonContext.Default.AvatarBindings, path);

    public static Spine2dScene ReadScene(ReadOnlySpan<byte> utf8, string path) => Read(utf8, AvatarJsonContext.Default.Spine2dScene, path);

    public static Spine2dWrite[] ReadWrites(JsonElement element, string path) =>
        element.Deserialize(AvatarJsonContext.Default.Spine2dWriteArray) ?? throw new AvatarFormatException(path, "is null");

    public static Spine2dParameter[] ReadParameters(JsonElement element, string path) =>
        element.Deserialize(AvatarJsonContext.Default.Spine2dParameterArray) ?? throw new AvatarFormatException(path, "is null");

    public static AvatarCueFixture ReadCueFixture(ReadOnlySpan<byte> utf8) => Read(utf8, AvatarJsonContext.Default.AvatarCueFixture, "timed-cues.json");

    private static T Read<T>(ReadOnlySpan<byte> utf8, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, string path)
    {
        try
        {
            return JsonSerializer.Deserialize(utf8, info) ?? throw new AvatarFormatException(path, "is null");
        }
        catch (JsonException e)
        {
            throw new AvatarFormatException(path, e.Message, e);
        }
    }
}

public sealed class AvatarFormatException(string path, string message, Exception? inner = null)
    : Exception($"{path}: {message}", inner)
{
    public string Path { get; } = path;
}
