using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Plugin.Maui.Spine.Layouts;

/// <summary>
/// Writes a class for each <c>*.layout.xml</c> among the project's additional files, with the project's
/// <c>*.resources.xml</c> and strings documents (<c>strings.xml</c>, <c>strings.sv.xml</c>) beside it.
/// Plugin.Maui.Spine.Common's build file adds all of them as additional files.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class LayoutGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Where(f => SourceFile.IsLayout(f.Path) || SourceFile.IsResources(f.Path) || SourceFile.StringsCulture(f.Path) is not null)
            .Select((f, cancellationToken) => f.GetText(cancellationToken) is { } text ? new SourceFile(f.Path, text) : null)
            .Where(f => f is not null)
            .Select((f, _) => f!);

        var layouts = files.Where(f => SourceFile.IsLayout(f.Path));
        var others = files.Where(f => !SourceFile.IsLayout(f.Path)).Collect();
        var rootNamespace = context.AnalyzerConfigOptionsProvider.Select((options, _) =>
            options.GlobalOptions.TryGetValue("build_property.RootNamespace", out var value) && value.Length > 0 ? value : null);

        context.RegisterSourceOutput(layouts.Combine(others).Combine(context.CompilationProvider).Combine(rootNamespace),
            (output, input) => Generate(output, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    static void Generate(SourceProductionContext output, SourceFile layout, ImmutableArray<SourceFile> others, Compilation compilation, string? rootNamespace)
    {
        var diagnostics = new List<Diagnostic>();
        var resources = new List<XDocument>();
        foreach (var file in others.Where(f => SourceFile.IsResources(f.Path)).OrderBy(f => f.Path, System.StringComparer.Ordinal))
            if (file.Load(diagnostics) is { } document)
                resources.Add(document);
        var strings = StringsTable.From(others.Where(f => !SourceFile.IsResources(f.Path)));

        var compiler = new LayoutCompiler(layout, compilation, resources, strings, rootNamespace);
        compiler.Compile();

        // A resources file's own problems are reported once, with the first layout.
        foreach (var diagnostic in compiler.Diagnostics.Concat(diagnostics).Distinct())
            output.ReportDiagnostic(diagnostic);
        if (compiler.HintName is { } hintName && compiler.Source is { } source)
            output.AddSource(hintName, source);
    }
}
