using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>What the generator says about a file that is wrong, and where.</summary>
public class DiagnosticsTests
{
    const string Model = """
        namespace App;
        public sealed record Card(string Name, string? Nickname, int Count, bool IsLive, System.DateTimeOffset At,
            System.Collections.Generic.IReadOnlyList<double> Values, System.Collections.Generic.IReadOnlyList<string> Goals);
        public static class AppIcons { public const string Drop = "drop"; }
        """;

    static string Layout(string body, string root = "LiveActivity") => $"""
        <{root} xmlns="https://schemas.cosmomedia.se/spine/layout" Model="App.Card">
        {body}
        </{root}>
        """;

    sealed class File(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    static (ImmutableArray<Diagnostic> Generator, ImmutableArray<Diagnostic> Compiler, string Source) Run(string layout, params (string Path, string Text)[] others)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(Common.W).Assembly.Location));
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create("App", [CSharpSyntaxTree.ParseText(Model, parse)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new LayoutGenerator().AsSourceGenerator()],
            [new File("/app/Card.layout.xml", layout), .. others.Select(o => new File(o.Path, o.Text))], parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var source = string.Concat(driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        return (diagnostics, output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray(), source);
    }

    static Diagnostic Single(string id, string layout, params (string, string)[] others)
    {
        var (generator, _, _) = Run(layout, others);
        return Assert.Single(generator, d => d.Id == id);
    }

    static int Line(Diagnostic diagnostic) => diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;

    [Fact]
    public void A_good_file_has_nothing_to_say_and_compiles()
    {
        var (generator, compiler, source) = Run(Layout("""
            <VStack Spacing="4"><Label Text="{Name}" /><Elapsed Since="{At}" Format="Ago" /></VStack>
            """));
        Assert.Empty(generator);
        Assert.Empty(compiler);
        Assert.Contains("public static partial class CardLayout", source);
    }

    [Fact]
    public void An_unknown_element_is_SPL001_at_its_line()
    {
        var diagnostic = Single("SPL001", Layout("""
            <VStack>
                <Lable Text="{Name}" />
            </VStack>
            """));
        Assert.Equal(3, Line(diagnostic));
        Assert.Equal("/app/Card.layout.xml", diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public void An_unknown_attribute_is_SPL002_with_a_suggestion() =>
        Assert.Contains("did you mean FontSize?", Single("SPL002", Layout("""<Label Text="{Name}" Fontsize="12" />""")).GetMessage());

    [Fact]
    public void An_unknown_path_is_SPL003_with_a_suggestion() =>
        Assert.Contains("did you mean Name?", Single("SPL003", Layout("""<Label Text="{Nmae}" />""")).GetMessage());

    [Fact]
    public void If_on_a_number_is_SPL004() =>
        Single("SPL004", Layout("""<Label Text="{Name}" If="{Count}" />"""));

    [Fact]
    public void A_bad_color_is_SPL005() =>
        Single("SPL005", Layout("""<Label Text="{Name}" TextColor="Purple" />"""));

    [Fact]
    public void Text_that_may_be_absent_is_SPL006_unless_guarded()
    {
        Single("SPL006", Layout("""<Label Text="{Nickname}" />"""));
        Assert.Empty(Run(Layout("""<Label Text="{Nickname}" If="{Nickname}" />""")).Generator);
        Assert.Empty(Run(Layout("""<Label Text="{Nickname, Fallback={Name}}" />""")).Generator);
    }

    [Fact]
    public void A_second_part_in_a_region_is_SPL007() =>
        Single("SPL007", Layout("""<VStack><Label Text="{Name}" Part="Value" /><Label Text="{Name}" Part="Value" /></VStack>"""));

    [Fact]
    public void An_unused_template_or_shared_node_is_SPL008()
    {
        var (generator, _, _) = Run(Layout("""
            <LiveActivity.Shared><Label Shared="Title" Text="{Name}" /></LiveActivity.Shared>
            <Template Name="Unused"><Spacer /></Template>
            <Label Text="{Name}" />
            """));
        Assert.Equal(2, generator.Count(d => d.Id == "SPL008"));
    }

    [Fact]
    public void Strings_are_checked_in_every_language()
    {
        var strings = new[] { ("/app/Resources/Strings/strings.xml", """<strings><s key="Hello">Hello {0}</s></strings>"""),
            ("/app/Resources/Strings/strings.sv.xml", """<strings culture="sv"></strings>""") };
        Assert.Contains("strings.sv.xml", Single("SPL009", Layout("""<Label Text="{Loc Hello, Name}" />"""), strings).GetMessage());
        Assert.Contains("takes 1 argument", Single("SPL010", Layout("""<Label Text="{Loc Hello}" />"""), strings).GetMessage());
    }

    [Fact]
    public void A_use_of_a_shared_node_that_may_be_absent_is_SPL011_unless_guarded()
    {
        const string shared = """<LiveActivity.Shared><Label Shared="Live" If="{IsLive}" Text="{Name}" /></LiveActivity.Shared>""";
        Single("SPL011", Layout(shared + """<VStack><Use Shared="Live" /></VStack>"""));
        Assert.Empty(Run(Layout(shared + """<VStack If="{IsLive}"><Use Shared="Live" /></VStack>""")).Generator);
    }

    [Fact]
    public void An_icon_with_no_SVG_is_SPL012()
    {
        Single("SPL012", Layout("""<Icon Source="dorp" />"""));
        Assert.Empty(Run(Layout("""<Icon Source="drop" />""")).Generator);
    }

    [Fact]
    public void A_graph_without_a_description_is_SPL013() =>
        Single("SPL013", Layout("""<Sparkline Values="{Values}" />"""));

    [Fact]
    public void A_resource_defined_twice_is_SPL014() =>
        Single("SPL014", Layout("""<Label Text="{Name}" />"""),
            ("/app/A.resources.xml", """<Resources><Color Name="Ink">#000000</Color><Color Name="Ink">#111111</Color></Resources>"""));

    [Fact]
    public void Broken_XML_is_SPL015() =>
        Single("SPL015", Layout("""<Label Text="{Name}">"""));

    [Fact]
    public void A_button_in_the_compact_island_is_an_error() =>
        Single("SPL001", Layout("""
            <Label Text="{Name}" />
            <LiveActivity.Island><Island.Minimal><Button Action="go"><Label Text="Go" /></Button></Island.Minimal></LiveActivity.Island>
            """));

    [Fact]
    public void A_compiler_error_in_a_bound_expression_points_at_the_file()
    {
        var (_, compiler, _) = Run(Layout("""
            <VStack>
                <Label Text="{Static Missing.Member}" />
            </VStack>
            """));
        var error = Assert.Single(compiler);
        Assert.Equal("/app/Card.layout.xml", error.Location.GetMappedLineSpan().Path);
        Assert.Equal(3, error.Location.GetMappedLineSpan().StartLinePosition.Line + 1);
    }

    [Fact]
    public void ForEach_names_the_item_and_widgets_take_sizes()
    {
        var (generator, compiler, _) = Run(Layout("""
            <VStack><ForEach Items="{Goals}" Max="3"><Label Text="{.}" /><ForEach.Empty><Label Text="None" /></ForEach.Empty></ForEach></VStack>
            <Widget.LockLine><Label Text="{Name}" /></Widget.LockLine>
            """, "Widget"));
        Assert.Empty(generator);
        Assert.Empty(compiler);
    }

    [Fact]
    public void The_XSD_in_the_package_is_the_vocabulary()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Plugin.Maui.Spine.Common", "build", "spine-layout.xsd");
        // SPINE_WRITE_XSD=1 dotnet test … writes it after a change to the vocabulary.
        if (Environment.GetEnvironmentVariable("SPINE_WRITE_XSD") == "1")
            System.IO.File.WriteAllText(path, Vocabulary.Xsd());
        Assert.Equal(Vocabulary.Xsd(), System.IO.File.ReadAllText(path).Replace("\r\n", "\n"));
    }
}
