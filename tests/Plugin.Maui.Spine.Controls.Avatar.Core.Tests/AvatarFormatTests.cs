using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Format 1.0 with defaults (decision D9): the five reference packages load as written and again
// rewritten with the defaults, to the same avatar.
public class AvatarFormatTests
{
    public static TheoryData<string> KeptAvatars => ["kairo", "nova", "aurora-flow", "plush-puff", "voice-totem"];

    [Theory]
    [MemberData(nameof(AvatarArchiveTests.ReferenceAvatars), MemberType = typeof(AvatarArchiveTests))]
    public void ReferenceAvatarsMatchTheSchemas(string name)
    {
        var report = new AvatarValidationReport();
        AvatarArchive.TryRead(new MemoryStream(AvatarArchiveTests.Reference(name)), report);

        Assert.DoesNotContain(report.Checks, c => c.Name == "schema" && c.Status == AvatarCheckStatus.Fail);
        Assert.Contains(report.Checks, c => c.Area == "manifest" && c.Name == "schema" && c.Status == AvatarCheckStatus.Pass);
    }

    [Theory]
    [MemberData(nameof(KeptAvatars))]
    public void SlimPackageLoadsToTheSameAvatar(string name)
    {
        var original = AvatarArchiveTests.Load(name);
        var report = new AvatarValidationReport();
        var slim = AvatarArchive.TryRead(Slim(original), report);

        Assert.True(slim is not null, string.Join("\n", report.Failures.Select(f => $"{f.Area}/{f.Name}: {f.Detail}")));
        Assert.DoesNotContain(report.Checks, c => c.Status == AvatarCheckStatus.Warning && c.Name is "schema" or "platforms");

        var a = original.Manifest;
        var b = slim.Manifest;
        foreach (var state in AvatarVocabulary.States)
            Assert.Equal(a.States[state] with { Declared = true }, b.States[state] with { Declared = true });
        foreach (var expression in AvatarVocabulary.Expressions)
        {
            Assert.Equal(a.Expressions[expression].Pose, b.Expressions[expression].Pose);
            Assert.Equal(a.Expressions[expression].TransitionMs, b.Expressions[expression].TransitionMs);
            Assert.Equal(a.Expressions[expression].Channels, b.Expressions[expression].Channels);
        }
        Assert.Equal((a.Speech.Mode, a.Speech.ExpressionMouthScale, a.Speech.ReleaseMs), (b.Speech.Mode, b.Speech.ExpressionMouthScale, b.Speech.ReleaseMs));
        Assert.Equal(a.Speech.Mapping.OrderBy(p => p.Key), b.Speech.Mapping.OrderBy(p => p.Key));
        Assert.Equal(a.ReducedMotion with { Declared = true }, b.ReducedMotion with { Declared = true });
        Assert.Equal(a.Themes.Slots.Keys, b.Themes.Slots.Keys);

        var ra = a.Representations[0];
        var rb = b.Representations[0];
        Assert.Equal(ra.Capabilities.Order(), rb.Capabilities.Order());
        Assert.Equal((ra.IdleOwner, ra.BlinkOwner, ra.GazeOwner), (rb.IdleOwner, rb.BlinkOwner, rb.GazeOwner));

        var ma = original.GetMotions(ra);
        var mb = slim.GetMotions(rb);
        Assert.Equal(ma.IdleVariants, mb.IdleVariants);
        Assert.Equal(ma.Gestures!.OrderBy(p => p.Key), mb.Gestures!.OrderBy(p => p.Key));
        Assert.Equal(ma.BlinkIntervalSeconds ?? AvatarDefaults.BlinkIntervalSeconds, mb.BlinkIntervalSeconds ?? AvatarDefaults.BlinkIntervalSeconds);

        var ba = original.GetBindings(ra);
        var bb = slim.GetBindings(rb);
        if (ra.Format != "sksl")
        {
            static IEnumerable<(string, string)> Moving(AvatarBindings b) =>
                b.Poses.Where(p => p.Value.GetArrayLength() > 0).Select(p => (p.Key, JsonNode.Parse(p.Value.GetRawText())!.ToJsonString())).OrderBy(p => p.Key);
            Assert.Equal(Moving(ba), Moving(bb));
            Assert.Equal(ba.Animations.OrderBy(p => p.Key), bb.Animations.OrderBy(p => p.Key));
            foreach (var key in new[] { "lighting", "bloom", "springs", "neckChain", "blinkFrames", "gaze", "blink", "muteBadge", "headNode", "speechMouthMesh" })
                Assert.Equal(Compact(ba.Find(key)), Compact(bb.Find(key)));
        }
    }

    [Fact]
    public void NovasSlimManifestIsUnderOneKilobyteBesidesItsHashTable()
    {
        var manifest = AvatarSlimming.Manifest(AvatarArchiveTests.Load("nova"));
        manifest.Remove("files");

        Assert.True(Encoding.UTF8.GetByteCount(manifest.ToJsonString()) < 1024, manifest.ToJsonString());
    }

    [Fact]
    public void AuroraFlowNeedsNoBindingsFile()
    {
        var original = AvatarArchiveTests.Load("aurora-flow");
        var slim = AvatarArchive.Read(Slim(original));

        Assert.Null(slim.Manifest.Representations[0].Bindings);
        Assert.False(slim.TryGetFile("bindings/skia.json", out _));
    }

    [Fact]
    public void KairosExtensionKeysMoveUnderExtensions()
    {
        var bindings = AvatarSlimming.Bindings(AvatarArchiveTests.Load("kairo"), AvatarArchiveTests.Load("kairo").Manifest.Representations[0])!;

        Assert.Null(bindings["lighting"]);
        Assert.Equal("portrait", bindings["extensions"]!["spine.lighting"]!.GetValue<string>());
        Assert.False(bindings["extensions"]!["spine.springs"]!.GetValue<bool>());
    }

    [Fact]
    public void ExtensionKeysAtTheTopLevelWarnWithWhereTheyBelong()
    {
        var report = new AvatarValidationReport();
        Assert.NotNull(AvatarArchive.TryRead(new MemoryStream(AvatarArchiveTests.Reference("kairo")), report));

        Assert.Contains(report.Checks, c => c.Status == AvatarCheckStatus.Warning && c.Detail == "bindings/native3d.json lighting: is not a key of the schema; write it as extensions.\"spine.lighting\"");
    }

    [Fact]
    public void MinimalManifestGetsEveryDefault()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(AvatarArchiveTests.RewriteManifest("nova", m =>
        {
            foreach (var key in new[] { "states", "expressions", "motions", "reducedMotion", "themes" })
                m.AsObject().Remove(key);
            m["speech"] = new JsonObject { ["canonicalProfile"] = "oculus15-v1" };
            var r = m["representations"]![0]!.AsObject();
            foreach (var key in new[] { "capabilities", "platforms", "idleOwner", "blinkOwner", "gazeOwner" })
                r.Remove(key);
        }), report);

        Assert.True(package is not null, string.Join("\n", report.Failures.Select(f => f.Detail)));
        var m = package.Manifest;
        Assert.Equal(new AvatarStateProfile("listening", null, InputReactive: true, OutputReactive: false, "engaged", 220, Declared: false), m.States["listening"]);
        Assert.Equal("processing", m.States["thinking"].Gaze);
        Assert.Equal(90, m.States["interrupted"].TransitionMs);
        Assert.Equal(("expr_surprised", 100), (m.Expressions["surprised"].Pose, m.Expressions["surprised"].TransitionMs));
        Assert.Equal(("canonicalVisemes", "viseme_PP"), (m.Speech.Mode, m.Speech.Mapping["1"]));
        Assert.Equal("reduced_motion", m.ReducedMotion.Pose);
        Assert.Equal(["idle_a", "idle_b"], package.GetMotions(m.Representations[0]).IdleVariants!);
        Assert.Equal("lean_in", package.GetMotions(m.Representations[0]).Gestures!["lean"]);
        Assert.Equal(
            ["ambient", "expressions", "audioReactive", "speechArticulation", "gaze", "blink", "gestures", "reducedMotion"],
            m.Representations[0].Capabilities);
    }

    [Fact]
    public void MissingExpressionWarnsAndShowsNeutral()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(AvatarArchiveTests.RewriteJson("nova", "bindings/native3d.json",
            b => b["poses"]!.AsObject().Remove("expr_curious")), report);
        Assert.Null(package);

        report = new AvatarValidationReport();
        package = AvatarArchive.TryRead(RewriteBoth("nova", m => m["expressions"]!.AsObject().Remove("curious"), b => b["poses"]!.AsObject().Remove("expr_curious")), report);

        Assert.True(package is not null, string.Join("\n", report.Failures.Select(f => f.Detail)));
        Assert.Contains(report.Checks, c => c.Status == AvatarCheckStatus.Warning && c.Detail == "expression 'curious' has no pose 'expr_curious'; it shows as neutral");

        var scheduler = new AvatarScheduler(package, package.Manifest.Representations[0]);
        scheduler.SetBaseExpression(AvatarExpression.Curious, 1);
        var frame = scheduler.Update();
        Assert.Contains(frame.Expression.ToArray(), w => w.Pose == "expr_neutral");
    }

    [Fact]
    public void DeclaredCapabilitiesAreKeptAndChecked()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(AvatarArchiveTests.RewriteManifest("voice-totem",
            m => m["representations"]![0]!["capabilities"]!.AsArray().Add("blink")), report);

        Assert.NotNull(package);
        Assert.Contains("blink", package.Manifest.Representations[0].Capabilities);
        Assert.Contains(report.Checks, c => c.Status == AvatarCheckStatus.Warning && c.Detail!.StartsWith("declares blink, which the bindings and model do not support", StringComparison.Ordinal));
    }

    [Fact]
    public void PlatformsWarnThatTheyAreNoLongerRead()
    {
        var report = new AvatarValidationReport();
        AvatarArchive.TryRead(new MemoryStream(AvatarArchiveTests.Reference("nova")), report);

        Assert.Contains(report.Checks, c => c.Name == "platforms" && c.Status == AvatarCheckStatus.Warning);
    }

    [Fact]
    public void BindingsOfTheWrongTypeFailWithThePath()
    {
        var report = new AvatarValidationReport();
        var package = AvatarArchive.TryRead(AvatarArchiveTests.RewriteJson("nova", "bindings/native3d.json", b => b["headNode"] = "head"), report);

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Name == "schema" && f.Detail == "bindings/native3d.json headNode: is the string \"head\", expected integer");
    }

    [Fact]
    public void BothSchemasAreEmbedded()
    {
        Assert.Equal("Spine Avatar manifest (avatar.json), format 1.0", AvatarSchemas.Manifest.GetProperty("title").GetString());
        Assert.Equal("Spine Avatar bindings, format 1.0", AvatarSchemas.Bindings.GetProperty("title").GetString());
    }

    private static string? Compact(JsonElement? element) => element is { } e ? JsonNode.Parse(e.GetRawText())?.ToJsonString() ?? "null" : null;

    // The package rewritten with AvatarSlimming, with the hash table recomputed.
    internal static MemoryStream Slim(AvatarPackage package)
    {
        var files = package.FilePaths.Where(p => p != AvatarArchive.ManifestPath).ToDictionary(p => p, p => package.GetFile(p).ToArray());
        foreach (var representation in package.Manifest.Representations)
        {
            if (representation.Bindings is not { } path)
                continue;
            if (AvatarSlimming.Bindings(package, representation) is { } slim)
                files[path] = Encoding.UTF8.GetBytes(slim.ToJsonString());
            else
                files.Remove(path);
        }

        var manifest = AvatarSlimming.Manifest(package);
        manifest["files"] = new JsonArray([.. files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => (JsonNode)new JsonObject
        {
            ["path"] = f.Key,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(f.Value)),
            ["bytes"] = f.Value.Length,
        })]);
        files[AvatarArchive.ManifestPath] = Encoding.UTF8.GetBytes(manifest.ToJsonString());

        var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in files)
            {
                using var write = zip.CreateEntry(path).Open();
                write.Write(bytes);
            }
        }
        output.Position = 0;
        return output;
    }

    private static MemoryStream RewriteBoth(string name, Action<JsonNode> manifest, Action<JsonNode> bindings)
    {
        using var source = new ZipArchive(new MemoryStream(AvatarArchiveTests.Reference(name)), ZipArchiveMode.Read);
        using var read = source.GetEntry("bindings/native3d.json")!.Open();
        var json = JsonNode.Parse(read)!;
        bindings(json);
        var changed = Encoding.UTF8.GetBytes(json.ToJsonString());
        return AvatarArchiveTests.RewriteManifest(name, m =>
        {
            manifest(m);
            var file = m["files"]!.AsArray().First(f => f!["path"]!.GetValue<string>() == "bindings/native3d.json")!;
            file["sha256"] = Convert.ToHexStringLower(SHA256.HashData(changed));
            file["bytes"] = changed.Length;
        }, ("bindings/native3d.json", changed));
    }
}
