using System.Diagnostics;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>
/// The widget extension's Swift interpreter fills a template in as C# does: compiled from
/// <c>native/ios/SpineLayoutTemplates.swift</c> on the Mac and run on the same templates and data. Skipped where there is no swiftc.
/// </summary>
public class SwiftTemplateTests
{
    static readonly Lazy<string?> Interpreter = new(Compile);

    static string? Compile()
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/xcrun"))
            return null;
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "Plugin.Maui.Spine.Widgets", "native", "ios", "SpineLayoutTemplates.swift"));
        var folder = Directory.CreateTempSubdirectory("spine-templates-").FullName;
        File.WriteAllText(Path.Combine(folder, "main.swift"), """
            import Foundation
            let template = try JSONDecoder().decode(JSONValue.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])))
            let data = try JSONDecoder().decode(JSONValue.self, from: Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[2])))
            let layout = LayoutTemplates.render(template, data: data)
            FileHandle.standardOutput.write(try JSONEncoder().encode(layout))
            """);
        var output = Path.Combine(folder, "fill");
        Run("/usr/bin/xcrun", ["swiftc", "-O", "-o", output, source, Path.Combine(folder, "main.swift")]);
        return output;
    }

    static string Run(string file, string[] arguments)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{file} failed: {error}");
        return output;
    }

    static void Same(LiveActivityTemplate template, LiveActivityTemplateData data)
    {
        if (Interpreter.Value is not { } fill)
            return;
        var folder = Directory.CreateTempSubdirectory("spine-template-").FullName;
        File.WriteAllText(Path.Combine(folder, "template.json"), template.Json);
        File.WriteAllText(Path.Combine(folder, "data.json"), data.ToJson());
        var swift = JsonNode.Parse(Run(fill, [Path.Combine(folder, "template.json"), Path.Combine(folder, "data.json")]));
        Directory.Delete(folder, recursive: true);
        var csharp = JsonNode.Parse(template.Render(data));
        Assert.True(JsonNode.DeepEquals(csharp, swift), $"C#    {csharp!.ToJsonString()}\nSwift {swift!.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(GlucoseLayoutTests.Cases), MemberType = typeof(GlucoseLayoutTests))]
    public void Swift_fills_the_reading_in_as_CSharp_does(string name)
    {
        var model = GlucoseLayoutTests.Case(name);
        Same(GlucoseLayout.Template, GlucoseLayout.Data(model));
        Same(GlucoseNoValueLayout.Template, GlucoseNoValueLayout.Data(model));
    }

    [Fact]
    public void Swift_fills_a_list_in_as_CSharp_does()
    {
        var model = new ScoreModel();
        Same(ScoreLayout.Template, ScoreLayout.Data(model));
        model.Goals.Add("1–0 Ek 4'");
        model.Goals.Add("1–1 Berg 9'");
        Same(ScoreLayout.Template, ScoreLayout.Data(model));
    }
}
