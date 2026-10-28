using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Controls.Avatar.Skia;
using SkiaSharp;

namespace Plugin.Maui.Spine.Controls.Avatar.Core.Tests;

// Format 1.1: options and presets, and the runtime version a package may require (#697).
[Collection(Measurements.Name)]
public class AvatarOptionsTests
{
    private const string Model = "models/mini-red.sksl";

    // mini-red as a format 1.1 package: an option `shape`, bound to the uniform `figure`, whose index the shader
    // paints into the red channel (index / 3), and two presets.
    private static MemoryStream WithOptions(Action<JsonObject>? edit = null)
    {
        string source;
        using (var zip = new ZipArchive(new MemoryStream(AvatarArchiveTests.Reference("mini-red")), ZipArchiveMode.Read))
        using (var read = new StreamReader(zip.GetEntry(Model)!.Open()))
            source = read.ReadToEnd();
        var shader = source
            .Replace("uniform float dark;", "uniform float dark;\nuniform float figure;")
            .Replace("return half4(half3(c * alpha), half(alpha));", "return half4(half(figure / 3.0), 0.0, 0.0, 1.0);");
        Assert.NotEqual(source, shader);
        Assert.Contains("figure / 3.0", shader);
        var bytes = Encoding.UTF8.GetBytes(shader);

        return AvatarArchiveTests.RewriteManifest("mini-red", m =>
        {
            var file = m["files"]!.AsArray().First(f => f!["path"]!.GetValue<string>() == Model)!;
            file["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            file["bytes"] = bytes.Length;
            m["schemaVersion"] = "1.1";
            m["options"] = JsonNode.Parse("""{ "shape": { "displayName": "Shape", "values": ["ball", "cube", "drop", "bowl"], "default": "drop", "bindings": ["uniform:figure"] } }""");
            m["presets"] = JsonNode.Parse("""
                {
                  "red": { "displayName": "Mini Red", "options": { "shape": "cube" }, "themes": { "body": { "light": "#F1273A", "dark": "#FF4454" } } },
                  "plain": { "displayName": "Plain" }
                }
                """);
            edit?.Invoke(m.AsObject());
        }, (Model, bytes));
    }

    private static (AvatarPackage? Package, AvatarValidationReport Report) Read(Stream stream)
    {
        var report = new AvatarValidationReport();
        return (AvatarArchive.TryRead(stream, report), report);
    }

    [Fact]
    public void OptionsAndPresetsLoadAndTheShaderStartsAtTheDefault()
    {
        var (package, report) = Read(WithOptions());

        Assert.True(package is not null, string.Join("; ", report.Failures.Select(f => f.Detail)));
        Assert.DoesNotContain(report.Checks, c => c.Status == AvatarCheckStatus.Warning && c.Name is "options" or "presets" or "schemaVersion");
        var shape = package.Manifest.Options["shape"];
        Assert.Equal("Shape", shape.DisplayName);
        Assert.Equal(2, shape.IndexOf(shape.Default));
        Assert.Equal(-1, shape.IndexOf("cone"));
        Assert.Equal("cube", package.Manifest.Presets["red"].Options["shape"]);
        Assert.Equal(new AvatarColors("#F1273A", "#FF4454"), package.Manifest.Presets["red"].Themes["body"]);
        Assert.Empty(package.Manifest.Presets["plain"].Options);
        Assert.Empty(package.Manifest.Presets["plain"].Themes);

        using var renderer = SkiaAvatarModel.Compile(package, package.Manifest.Representations[0]).CreateRenderer();
        renderer.Evaluate(new AvatarRenderFrame { ElapsedSeconds = 1 });
        using var bitmap = new SKBitmap(20, 20);
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, new SKRect(0, 0, 20, 20), dark: false);
        Assert.InRange(bitmap.GetPixel(10, 10).Red, 165, 175);
    }

    [Fact]
    public void AManifestWithoutOptionsHasNone()
    {
        var package = AvatarArchiveTests.Load("mini-red");

        Assert.Empty(package.Manifest.Options);
        Assert.Empty(package.Manifest.Presets);
    }

    private static readonly Dictionary<string, Action<JsonObject>> Breaks = new()
    {
        ["default outside the values"] = m => m["options"]!["shape"]!["default"] = "cone",
        ["preset sets an unknown option"] = m => m["presets"]!["red"]!["options"]!["eyes"] = "round",
        ["preset sets a value the option lacks"] = m => m["presets"]!["red"]!["options"]!["shape"] = "cone",
        ["preset colours an unknown slot"] = m => m["presets"]!["red"]!["themes"]!["hat"] = JsonNode.Parse("""{ "light": "#000000", "dark": "#000000" }"""),
        ["binding to an undeclared uniform"] = m => m["options"]!["shape"]!["bindings"] = new JsonArray("uniform:figure", "uniform:form"),
        ["binding to a uniform the runtime fills"] = m => m["options"]!["shape"]!["bindings"] = new JsonArray("uniform:figure", "uniform:blink"),
        ["binding of an unknown kind"] = m => m["options"]!["shape"]!["bindings"] = new JsonArray("uniform:figure", "paint:body"),
        ["runtime too old"] = m => m["minRuntimeVersion"] = "9.0.0",
        ["runtime version not a version"] = m => m["minRuntimeVersion"] = "1.1",
    };

    public static TheoryData<string, string> Broken => new()
    {
        { "default outside the values", "defaults to 'cone'" },
        { "preset sets an unknown option", "sets option 'eyes'" },
        { "preset sets a value the option lacks", "sets shape to 'cone'" },
        { "preset colours an unknown slot", "colours theme slot 'hat'" },
        { "binding to an undeclared uniform", "binds 'uniform:form', which the shader does not declare" },
        { "binding to a uniform the runtime fills", "binds 'uniform:blink', which the runtime or a theme slot already fills" },
        { "binding of an unknown kind", "binds 'paint:body'" },
        { "runtime too old", $"needs avatar runtime 9.0.0; this one is {AvatarRuntime.Version}" },
        { "runtime version not a version", "'1.1' is not a version" },
    };

    [Theory]
    [MemberData(nameof(Broken))]
    public void ABrokenOptionFailsWithItsReason(string broken, string reason)
    {
        var (package, report) = Read(WithOptions(Breaks[broken]));

        Assert.Null(package);
        Assert.Contains(report.Failures, f => f.Detail?.Contains(reason, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ReservedBindingsAndAnOldSchemaVersionWarn()
    {
        var (package, report) = Read(WithOptions(m =>
        {
            m["schemaVersion"] = "1.0";
            m["options"]!["shape"]!["bindings"] = new JsonArray("uniform:figure", "node:hat");
        }));

        Assert.NotNull(package);
        Assert.Contains(report.Checks, c => c.Status == AvatarCheckStatus.Warning && c.Detail!.Contains("'node:hat': node: and material: bindings are reserved"));
        Assert.Contains(report.Checks, c => c.Status == AvatarCheckStatus.Warning && c.Detail!.Contains("format 1.1, but schemaVersion says 1.0"));
    }

    [Theory]
    [InlineData("1.1.0", "1.1.0", 0)]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("2.0.0", "10.0.0", -1)]
    [InlineData("1.1.0-beta.2", "1.1.0", -1)]
    [InlineData("1.1.0+build.5", "1.1.0", 0)]
    [InlineData("1.1", "1.1.0", null)]
    public void VersionsOrderByMajorMinorPatch(string a, string b, int? order)
    {
        Assert.Equal(order, AvatarValidator.CompareVersions(a, b) is { } o ? Math.Sign(o) : null);
    }

    [Fact]
    public void ARuntimeAsNewAsThePackageLoadsIt()
    {
        var (package, report) = Read(WithOptions(m => m["minRuntimeVersion"] = AvatarRuntime.Version));

        Assert.True(package is not null, string.Join("; ", report.Failures.Select(f => f.Detail)));
    }

    private static readonly Dictionary<string, string> NoOptions = new(StringComparer.Ordinal);

    private static AvatarManifest Manifest() => Read(WithOptions()).Package!.Manifest;

    [Fact]
    public void ChoicesMergeTheDefaultThePresetAndTheApp()
    {
        var m = Manifest();

        Assert.Equal("drop", AvatarChoices.Options(m, null, NoOptions)["shape"]);
        Assert.Equal("cube", AvatarChoices.Options(m, "red", NoOptions)["shape"]);
        Assert.Equal("ball", AvatarChoices.Options(m, "red", new Dictionary<string, string> { ["shape"] = "ball" })["shape"]);
        Assert.Equal("drop", AvatarChoices.Options(m, "plain", NoOptions)["shape"]);
        Assert.Equal("drop", AvatarChoices.Options(m, "blue", NoOptions)["shape"]);
    }

    [Fact]
    public void APresetColoursEachMode()
    {
        var m = Manifest();

        Assert.Equal("#F1273A", AvatarChoices.PresetColors(m, "red", dark: false)["body"]);
        Assert.Equal("#FF4454", AvatarChoices.PresetColors(m, "red", dark: true)["body"]);
        Assert.Empty(AvatarChoices.PresetColors(m, null, dark: false));
        Assert.Empty(AvatarChoices.PresetColors(m, "plain", dark: false));
        Assert.Empty(AvatarChoices.PresetColors(m, "blue", dark: false));
    }

    [Fact]
    public void ChoicesNameWhatIsWrong()
    {
        var m = Manifest();

        Assert.Null(AvatarChoices.CheckOption(m, "shape", "cube"));
        Assert.Null(AvatarChoices.CheckOption(m, "shape", null));
        Assert.Contains("has no option 'size'; it has shape", AvatarChoices.CheckOption(m, "size", "big"));
        Assert.Contains("has no value 'cone'; it has ball, cube, drop, bowl", AvatarChoices.CheckOption(m, "shape", "cone"));
        Assert.Null(AvatarChoices.CheckPreset(m, "red"));
        Assert.Contains("has no preset 'blue'; it has red, plain", AvatarChoices.CheckPreset(m, "blue"));
    }

    [Fact]
    public void TheShaderFollowsTheOptionsBetweenFrames()
    {
        var package = Read(WithOptions()).Package!;
        using var renderer = SkiaAvatarModel.Compile(package, package.Manifest.Representations[0]).CreateRenderer();

        byte Red()
        {
            renderer.Evaluate(new AvatarRenderFrame { ElapsedSeconds = 1 });
            using var bitmap = new SKBitmap(20, 20);
            using var canvas = new SKCanvas(bitmap);
            renderer.Draw(canvas, new SKRect(0, 0, 20, 20), dark: false);
            return bitmap.GetPixel(10, 10).Red;
        }

        Assert.InRange(Red(), 165, 175);
        renderer.Options = new Dictionary<string, string> { ["shape"] = "ball" };
        Assert.InRange(Red(), 0, 5);
        renderer.Options = new Dictionary<string, string> { ["shape"] = "bowl" };
        Assert.InRange(Red(), 250, 255);
        renderer.Options = null;
        Assert.InRange(Red(), 165, 175);
    }

    [Fact]
    public void SlimmingKeepsOptionsAndPresets()
    {
        var (package, _) = Read(WithOptions());
        var original = JsonNode.Parse(package!.GetFile(AvatarArchive.ManifestPath).Span)!;

        var slim = AvatarSlimming.Manifest(package);

        Assert.True(JsonNode.DeepEquals(original["options"], slim["options"]));
        Assert.True(JsonNode.DeepEquals(original["presets"], slim["presets"]));
    }
}
