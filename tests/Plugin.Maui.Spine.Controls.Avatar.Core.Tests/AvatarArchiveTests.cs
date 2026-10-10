using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

public class AvatarArchiveTests
{
    public static TheoryData<string> ReferenceAvatars => ["dotling", "voice-totem", "pebble-bot", "pip", "aurora", "aurora-motion", "robot-expressive", "mpfb", "plush-mochi", "plush-sprig", "plush-bean", "plush-puff", "aurora-flow"];

    internal static byte[] Reference(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Avatars", name + ".spineavatar"));

    internal static AvatarPackage Load(string name) => AvatarArchive.Read(new MemoryStream(Reference(name)));

    [Theory]
    [MemberData(nameof(ReferenceAvatars))]
    public void ReferenceAvatarsLoadAndValidate(string name)
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(new MemoryStream(Reference(name)), report);

        Assert.True(package is not null, string.Join("\n", report.Failures.Select(f => $"{f.Area}/{f.Name}: {f.Detail}")));
        Assert.Equal(name, package.Manifest.Id);
    }

    [Theory]
    [InlineData("../evil.json", "'..' segment")]
    [InlineData("/etc/passwd", "absolute path")]
    [InlineData("models\\x.json", "backslash")]
    [InlineData("C:/x.json", "drive prefix")]
    [InlineData("a//b.json", "'' segment")]
    public void UnsafeNamesAreRejectedBeforeReading(string entry, string reason)
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(Rezip("dotling", add: (entry, "x"u8.ToArray())), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Name == "path" && f.Detail!.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void NamesThatDifferOnlyInCaseCollide()
    {
        var report = new AvatarValidationReport();
        Assert.Null(AvatarArchive.TryRead(Rezip("dotling", add: ("LICENSE.TXT", "x"u8.ToArray())), report));
        Assert.Contains(report.Failures, f => f.Detail!.Contains("collides", StringComparison.Ordinal));
    }

    [Fact]
    public void ChangedBytesFailTheHash()
    {
        var report = new AvatarValidationReport();
        Assert.Null(AvatarArchive.TryRead(Rezip("dotling", replace: ("LICENSE.txt", bytes => { bytes[0] ^= 1; return bytes; })), report));
        Assert.Contains(report.Failures, f => f.Name == "sha256" && f.Detail!.StartsWith("LICENSE.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlistedFilesFail()
    {
        var report = new AvatarValidationReport();
        Assert.Null(AvatarArchive.TryRead(Rezip("dotling", add: ("extra.txt", "x"u8.ToArray())), report));
        Assert.Contains(report.Failures, f => f.Detail == "extra.txt is in the archive but not listed in files");
    }

    [Fact]
    public void ZipBombIsStoppedAtTheEntryLimit()
    {
        var limits = new AvatarArchiveLimits(MaxEntryBytes: 1024 * 1024);
        var report = new AvatarValidationReport();
        Assert.Null(AvatarArchive.TryRead(Rezip("dotling", add: ("zeros.bin", new byte[2 * 1024 * 1024])), report, limits));
        Assert.Contains(report.Failures, f => f.Name is "size" or "ratio");
    }

    [Fact]
    public void TooManyEntriesAreRejected()
    {
        var report = new AvatarValidationReport();
        Assert.Null(AvatarArchive.TryRead(new MemoryStream(Reference("dotling")), report, new AvatarArchiveLimits(MaxEntries: 4)));
        Assert.Contains(report.Failures, f => f.Name == "entries");
    }

    [Fact]
    public void NotAZipFailsWithAMessage()
    {
        var report = new AvatarValidationReport();
        Assert.Null(AvatarArchive.TryRead(new MemoryStream("hello"u8.ToArray()), report));
        Assert.Contains(report.Failures, f => f.Name == "zip");
    }

    [Fact]
    public void MissingPoseFailsWithItsOwner()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(RewriteJson("dotling", "bindings/skia.json", json => json["poses"]!.AsObject().Remove("viseme_PP")), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Detail == "speech.mapping.1 names pose 'viseme_PP', which the bindings do not define");
    }

    [Fact]
    public void KeyframesOutOfOrderFail()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(RewriteJson("dotling", "models/dotling.avatar2d.json", json =>
        {
            var keys = json["animations"]!["nod"]!["tracks"]![0]!["keyframes"]!.AsArray();
            keys[1]!["seconds"] = 0.0;
        }), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Name == "animations" && f.Detail!.Contains("'nod'", StringComparison.Ordinal));
    }

    [Fact]
    public void PathPoseWithOtherTopologyFails()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(RewriteJson("dotling", "models/dotling.avatar2d.json", json =>
            json["pathPoses"]!["viseme_aa"]!["data"] = "M 0 0 L 10 0 L 10 10 Z"), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Detail!.Contains("cannot be interpolated linearly", StringComparison.Ordinal));
    }

    [Fact]
    public void GlbMorphTargetOutOfRangeFails()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(RewriteJson("pebble-bot", "bindings/native3d.json", json =>
            json["poses"]!["viseme_aa"]![0]!["targetIndex"] = 99), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Detail!.Contains("morph target 99", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownRequiredFeatureFails()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(RewriteManifest("dotling", json =>
            json["representations"]![0]!["requiredFeatures"] = new JsonArray("meshDeform")), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Name == "requiredFeatures");
    }

    [Fact]
    public void Spine2d11FeaturesMustBeDeclared()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(RewriteJson("dotling", "models/dotling.avatar2d.json", Upgrade), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Name == "requiredFeatures" && f.Detail!.Contains("'gradients'", StringComparison.Ordinal));
    }

    [Fact]
    public void DeclaredSpine2d11FeaturesLoad()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(Upgraded(), report);

        Assert.True(package is not null, string.Join("\n", report.Failures.Select(f => f.Detail)));
    }

    // Dotling with a radial-gradient body, a blurred screen-blended shine and stroked brows.
    internal static MemoryStream Upgraded()
    {
        using var source = new ZipArchive(new MemoryStream(Reference("dotling")), ZipArchiveMode.Read);
        using var read = source.GetEntry("models/dotling.avatar2d.json")!.Open();
        var scene = JsonNode.Parse(read)!;
        Upgrade(scene);
        var changed = Encoding.UTF8.GetBytes(scene.ToJsonString());

        return RewriteManifest("dotling", manifest =>
        {
            manifest["representations"]![0]!["requiredFeatures"] = new JsonArray("gradients", "blur", "strokes", "blendModes");
            var file = manifest["files"]!.AsArray().First(f => f!["path"]!.GetValue<string>() == "models/dotling.avatar2d.json")!;
            file["sha256"] = Convert.ToHexStringLower(SHA256.HashData(changed));
            file["bytes"] = changed.Length;
        }, ("models/dotling.avatar2d.json", changed));
    }

    private static void Upgrade(JsonNode scene)
    {
        scene["schemaVersion"] = "1.1";
        foreach (var node in scene["nodes"]!.AsArray())
        {
            switch (node!["id"]!.GetValue<string>())
            {
                case "body":
                    node["fill"] = JsonNode.Parse("""{"radial":{"cx":-30,"cy":-50,"r":170,"stops":[{"offset":0,"slot":"shine"},{"offset":0.55,"slot":"body"},{"offset":1,"slot":"halo"}]}}""");
                    break;
                case "shine":
                    node["blur"] = 6;
                    node["blend"] = "screen";
                    break;
                case "browLeft" or "browRight":
                    node["stroke"] = JsonNode.Parse("""{"width":1.5,"slot":"ink","cap":"round"}""");
                    break;
            }
        }
    }

    [Fact]
    public void MissingManifestFieldNamesTheField()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(RewriteManifest("dotling", json => json.AsObject().Remove("speech")), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Area == "manifest" && f.Detail!.Contains("speech", StringComparison.Ordinal));
    }

    // A copy of a reference archive with one entry added or changed. The manifest is left alone, so
    // its hashes describe the original files.
    internal static MemoryStream Rezip(string name, (string Path, byte[] Bytes)? add = null, (string Path, Func<byte[], byte[]> Change)? replace = null)
    {
        var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(Reference(name)), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var read = entry.Open();
                var bytes = new MemoryStream();
                read.CopyTo(bytes);
                var content = bytes.ToArray();
                if (replace is { } r && r.Path == entry.FullName)
                    content = r.Change(content);
                using var write = target.CreateEntry(entry.FullName).Open();
                write.Write(content);
            }
            if (add is { } a)
            {
                using var write = target.CreateEntry(a.Path).Open();
                write.Write(a.Bytes);
            }
        }
        output.Position = 0;
        return output;
    }

    // A copy with one JSON file edited and the manifest's hash table updated to match, so only the
    // edit itself can fail validation.
    internal static MemoryStream RewriteJson(string name, string path, Action<JsonNode> edit)
    {
        using var source = new ZipArchive(new MemoryStream(Reference(name)), ZipArchiveMode.Read);
        using var read = source.GetEntry(path)!.Open();
        var json = JsonNode.Parse(read)!;
        edit(json);
        var changed = Encoding.UTF8.GetBytes(json.ToJsonString());

        return RewriteManifest(name, manifest =>
        {
            var file = manifest["files"]!.AsArray().First(f => f!["path"]!.GetValue<string>() == path)!;
            file["sha256"] = Convert.ToHexStringLower(SHA256.HashData(changed));
            file["bytes"] = changed.Length;
        }, (path, changed));
    }

    internal static MemoryStream RewriteManifest(string name, Action<JsonNode> edit, (string Path, byte[] Bytes)? alsoReplace = null)
    {
        var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(Reference(name)), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var read = entry.Open();
                var bytes = new MemoryStream();
                read.CopyTo(bytes);
                var content = bytes.ToArray();
                if (entry.FullName == AvatarArchive.ManifestPath)
                {
                    var json = JsonNode.Parse(content)!;
                    edit(json);
                    content = Encoding.UTF8.GetBytes(json.ToJsonString());
                }
                else if (alsoReplace is { } r && r.Path == entry.FullName)
                    content = r.Bytes;
                using var write = target.CreateEntry(entry.FullName).Open();
                write.Write(content);
            }
        }
        output.Position = 0;
        return output;
    }
}
