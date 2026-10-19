using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Avatar.Tool;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Step C1 (issue #621): the spine-avatar tool on the reference packages, written to a temporary folder.
[Collection(Measurements.Name)]
public sealed class AvatarToolTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("spine-avatar-").FullName;

    private static string Reference(string name) => Path.Combine(AppContext.BaseDirectory, "Avatars", name + ".spineavatar");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("nova")]
    [InlineData("plush-puff")]
    [InlineData("voice-totem")]
    [InlineData("aurora-flow")]
    public void Pack_renders_real_posters_and_ships(string name)
    {
        var output = Path.Combine(_folder, name + ".spineavatar");

        Assert.Equal(0, Commands.Pack(Reference(name), new Options(output, null, null, null, 256, Strip: true)));

        var (package, report) = AvatarFiles.Read(output);
        Assert.NotNull(package);
        Assert.False(report.HasFailures);
        using var poster = SKBitmap.Decode(package.GetFile(package.Manifest.Posters.Dark).ToArray());
        Assert.Equal(256, poster.Width);
        Assert.Contains(poster.Pixels, p => p.Alpha > 0);
        Assert.Equal(0, Commands.Validate(output, new Options(null, "shipping", null, null, 512, false)));
    }

    [Fact]
    public void The_shipping_profile_turns_down_placeholders_and_source()
    {
        // Pebble Bot as a generator writes it (its generator under source/), with 1×1 placeholder posters.
        var lab = Placeholders("pebble-bot");
        Assert.Equal(1, Commands.Validate(lab, new Options(null, "shipping", null, null, 512, false)));

        var (package, _) = AvatarFiles.Read(lab);
        var report = new AvatarValidationReport();
        Commands.Shipping(package!, new FileInfo(lab).Length, report);
        Assert.Equal(["poster-light", "poster-dark", "source"], report.Checks.Where(c => c.Status == AvatarCheckStatus.Fail).Select(c => c.Name));
    }

    [Fact]
    public void Strip_removes_source_and_keeps_the_hashes_right()
    {
        var output = Path.Combine(_folder, "pebble-bot.spineavatar");

        Assert.Equal(0, Commands.Strip(Reference("pebble-bot"), new Options(output, null, null, null, 512, false)));

        // The reader checks every file against the table, so a package that loads has the hashes right.
        var (package, report) = AvatarFiles.Read(output);
        Assert.NotNull(package);
        Assert.False(report.HasFailures);
        Assert.DoesNotContain(package.FilePaths, p => p.StartsWith("source/", StringComparison.Ordinal));
        Assert.Equal(AvatarFiles.Read(Reference("pebble-bot")).Package!.FilePaths.Count(p => !p.StartsWith("source/", StringComparison.Ordinal)), package.FilePaths.Count());
    }

    [Fact]
    public void A_model_only_SceneKit_draws_keeps_its_posters_unless_given_some()
    {
        // mpfb: alpha-masked lashes and hair. (Kairo's hair is opaque since tripo45, and Skia draws Kairo.)
        var output = Path.Combine(_folder, "mpfb.spineavatar");
        Assert.Null(AvatarPainter.Create(AvatarFiles.Read(Reference("mpfb")).Package!, out var reason));
        Assert.Contains("--poster-light", reason);

        var light = Path.Combine(_folder, "light.png");
        var dark = Path.Combine(_folder, "dark.png");
        foreach (var path in new[] { light, dark })
        {
            using var bitmap = new SKBitmap(300, 300);
            bitmap.Erase(SKColors.Orange);
            File.WriteAllBytes(path, bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray());
        }

        Assert.Equal(0, Commands.Pack(Reference("mpfb"), new Options(output, null, light, dark, 512, Strip: true)));
        var (package, _) = AvatarFiles.Read(output);
        Assert.Equal(File.ReadAllBytes(light), package!.GetFile(package.Manifest.Posters.Light).ToArray());
    }

    // A copy of a reference package with 1×1 posters and the hash table updated, as a generator's placeholders.
    private string Placeholders(string name)
    {
        using var pixel = new SKBitmap(1, 1);
        var png = pixel.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        var path = Path.Combine(_folder, name + "-lab.spineavatar");
        using var source = new ZipArchive(new MemoryStream(File.ReadAllBytes(Reference(name))), ZipArchiveMode.Read);
        var files = source.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
        var manifest = JsonNode.Parse(files["avatar.json"])!;
        foreach (var file in manifest["files"]!.AsArray())
        {
            var filePath = file!["path"]!.GetValue<string>();
            if (!filePath.StartsWith("previews/poster-", StringComparison.Ordinal))
                continue;
            files[filePath] = png;
            file["sha256"] = Convert.ToHexStringLower(SHA256.HashData(png));
            file["bytes"] = png.Length;
        }
        files["avatar.json"] = System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString());
        using (var target = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
            foreach (var (entry, bytes) in files)
            {
                using var write = target.CreateEntry(entry).Open();
                write.Write(bytes);
            }
        return path;
    }
}
