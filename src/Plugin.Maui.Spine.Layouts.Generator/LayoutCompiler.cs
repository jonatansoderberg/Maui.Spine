using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace Plugin.Maui.Spine.Layouts;

/// <summary>
/// Turns one layout file into C#: a static <c>Build(model)</c> of <c>W.*</c> calls, its templates as local functions, the
/// actions' names and the icons it can show. Each node's code is preceded by a <c>#line</c> to its element, so a
/// compiler error in a bound expression points at the file.
/// </summary>
internal sealed class LayoutCompiler
{
    const string Common = "global::Plugin.Maui.Spine.Common.";

    readonly SourceFile _file;
    readonly Compilation _compilation;
    readonly IReadOnlyList<XDocument> _resources;
    readonly StringsTable _strings;
    readonly string? _rootNamespace;
    readonly Dictionary<string, ResourceValue> _values = new(StringComparer.Ordinal);
    readonly Dictionary<string, Template> _templates = new(StringComparer.Ordinal);
    readonly Dictionary<string, SharedNode> _shared = new(StringComparer.Ordinal);
    readonly SortedDictionary<string, string> _actions = new(StringComparer.Ordinal);
    readonly List<string> _icons = [];
    readonly HashSet<string>? _knownIcons;
    INamedTypeSymbol? _model;
    bool _usesStrings;
    TemplatePass? _tp;
    bool _quiet;
    readonly SortedSet<string> _sources = new(StringComparer.Ordinal);
    int _loops;

    public List<Diagnostic> Diagnostics { get; } = [];
    public string? HintName { get; private set; }
    public string? Source { get; private set; }

    public LayoutCompiler(SourceFile file, Compilation compilation, IReadOnlyList<XDocument> resources, StringsTable strings, string? rootNamespace)
    {
        _file = file;
        _compilation = compilation;
        _resources = resources;
        _strings = strings;
        _rootNamespace = rootNamespace;
        _knownIcons = KnownIcons(compilation);
    }

    sealed class ResourceValue(XElement element, string value)
    {
        public XElement Element { get; } = element;
        public string Value { get; } = value;
    }

    sealed class Template(XElement element, string name, List<Parameter> parameters, string signature)
    {
        public XElement Element { get; } = element;
        public string Name { get; } = name;
        public List<Parameter> Parameters { get; } = parameters;
        public string Signature { get; } = signature;
        public bool Used { get; set; }
        public bool HasContent => Element.Descendants().Any(e => e.Name.LocalName == "Content");
    }

    sealed class Parameter(string type, string name, ITypeSymbol? symbol)
    {
        public string Type { get; } = type;
        public string Name { get; } = name;
        public ITypeSymbol? Symbol { get; } = symbol;
    }

    sealed class SharedNode(XElement element, string key, string? guard)
    {
        public XElement Element { get; } = element;
        public string Key { get; } = key;
        public string? Guard { get; } = guard;
        public bool Used { get; set; }
    }

    sealed class Var(string code, ITypeSymbol? type, bool maybeNull)
    {
        public string Code { get; } = code;
        public ITypeSymbol? Type { get; } = type;
        public bool MaybeNull { get; } = maybeNull;
    }

    /// <summary>What is in scope where a node is written: the model, a template's parameters, a loop's item, what is known present.</summary>
    sealed class Scope
    {
        public Dictionary<string, Var> Vars { get; private set; } = new(StringComparer.Ordinal);
        public Var? Item { get; private set; }
        public HashSet<string> Present { get; private set; } = new(StringComparer.Ordinal);
        public HashSet<string> Guards { get; private set; } = new(StringComparer.Ordinal);
        public string Region { get; set; } = "";
        public bool InShared { get; set; }
        public bool InTemplate { get; set; }
        public bool Described { get; set; }
        public HashSet<string>? Parts { get; set; }

        public Scope Child(Var? item = null) => new()
        {
            Vars = Vars,
            Item = item ?? Item,
            Present = new HashSet<string>(Present, StringComparer.Ordinal),
            Guards = new HashSet<string>(Guards, StringComparer.Ordinal),
            Region = Region,
            InShared = InShared,
            InTemplate = InTemplate,
            Described = Described,
            Parts = Parts,
        };

        public Scope WithVars(Dictionary<string, Var> vars)
        {
            var scope = Child();
            scope.Vars = vars;
            return scope;
        }
    }

    /// <summary>A bound or literal value as C#: <see cref="Code"/> as it is, and where it may be absent the test and the sure form.</summary>
    sealed class Ex(string code, bool maybeNull = false, string? present = null, string? sure = null, ITypeSymbol? type = null)
    {
        public string Code { get; } = code;
        public bool MaybeNull { get; } = maybeNull;
        public string Present { get; } = present ?? "true";
        public string Sure { get; } = sure ?? code;
        public ITypeSymbol? Type { get; } = type;
    }

    sealed class Test(string code, string key, string? narrows, bool narrowsWhenTrue)
    {
        public string Code { get; } = code;
        public string Key { get; } = key;
        public string? Narrows { get; } = narrows;
        public bool NarrowsWhenTrue { get; } = narrowsWhenTrue;
    }

    sealed class Emitted(string code, bool maybeNull)
    {
        public string Code { get; } = code;
        public bool MaybeNull { get; } = maybeNull;
    }

    /// <summary>
    /// The second pass of a file with <c>Payload="Template"</c>: the same tree with each bound value a placeholder, and
    /// beside it the statements that collect those values, nested as the conditions and loops that guard them.
    /// </summary>
    sealed class TemplatePass
    {
        int _next;
        public StringBuilder Data { get; set; } = new();
        public string Site { get; set; } = "";
        public string Values { get; set; } = "d";
        public int Depth { get; set; }
        public StringBuilder Functions { get; } = new();
        public string? Failure { get; private set; }
        public XObject? FailedAt { get; private set; }

        /// <summary>The values already collected, by their code, in this block and those around it: used again rather than sent twice.</summary>
        public List<Dictionary<string, string>> Seen { get; set; } = [new(StringComparer.Ordinal)];

        public string Key()
        {
            var name = new StringBuilder();
            for (var n = _next++; ; n = n / 26 - 1)
            {
                name.Insert(0, (char)('a' + n % 26));
                if (n < 26) break;
            }
            return Site.Length == 0 ? Lit(name.ToString()) : $"{Site} + {Lit(name.ToString())}";
        }

        /// <summary>Starts the keys of a template's body again from a: its site's key keeps them apart.</summary>
        public (int, List<Dictionary<string, string>>) Restart()
        {
            var saved = (_next, Seen);
            (_next, Seen) = (0, [new(StringComparer.Ordinal)]);
            return saved;
        }

        public void Resume((int Next, List<Dictionary<string, string>> Seen) saved) => (_next, Seen) = saved;

        public void Fail(string reason, XObject? at)
        {
            Failure ??= reason;
            FailedAt ??= at;
        }
    }

    static readonly System.Text.RegularExpressions.Regex BoundCode = new(@"(?<![\w.""])(m|item\d+|strings)\b(?!"")");

    static bool Bound(string? code) => code is not null && BoundCode.IsMatch(code);

    /// <summary>
    /// A value where the layout uses it: as it is on the first pass; on the template pass, when it depends on the model,
    /// a placeholder, its value collected under a new key.
    /// </summary>
    string Out(string code, Kind kind, XObject? at)
    {
        if (_tp is null || !Bound(code))
            return code;
        var method = kind switch
        {
            Kind.Text or Kind.Icon or Kind.Name => "S",
            Kind.Number or Kind.FontSize => "N",
            Kind.Color => "C",
            Kind.Date => "D",
            Kind.Uri => "U",
            _ => null,
        };
        if (method is null)
        {
            _tp.Fail($"'{(at as XAttribute)?.Value ?? code}' is bound to a {kind} value, which a template cannot hold", at);
            return code;
        }
        var seen = _tp.Values + "|" + code;
        if (_tp.Seen.Select(level => level.TryGetValue(seen, out var known) ? known : null).LastOrDefault(k => k is not null) is { } same)
            return $"t.{method}({same})";
        var key = _tp.Key();
        _tp.Data.Append($"        {_tp.Values}.Set({key}, {code});\n");
        _tp.Seen[_tp.Seen.Count - 1][seen] = key;
        return $"t.{method}({key})";
    }

    /// <summary>Runs <paramref name="emit"/> with the template pass's values collected apart, and returns them.</summary>
    T Capture<T>(Func<T> emit, out string values)
    {
        if (_tp is null)
        {
            values = "";
            return emit();
        }
        var outer = _tp.Data;
        _tp.Data = new StringBuilder();
        _tp.Seen.Add(new Dictionary<string, string>(StringComparer.Ordinal));
        try { return emit(); }
        finally
        {
            values = _tp.Data.ToString();
            _tp.Data = outer;
            _tp.Seen.RemoveAt(_tp.Seen.Count - 1);
        }
    }

    public void Compile()
    {
        var document = _file.Load(Diagnostics);
        if (document?.Root is not { } root)
            return;

        foreach (var resources in _resources)
            ReadResources(resources);

        var kind = root.Name.LocalName;
        if (kind is not ("LiveActivity" or "Widget"))
        {
            Report(Layouts.Diagnostics.UnknownElement, root, $"A layout file's root is LiveActivity or Widget, not {kind}");
            return;
        }
        CheckAttributes(root, kind);

        if (root.Attribute("Model") is { } model)
        {
            _model = FindType(model.Value);
            if (_model is null)
                Report(Layouts.Diagnostics.UnknownPath, model, $"The model type '{model.Value}' was not found; give its full name");
        }

        var (ns, className) = ClassName(root);
        foreach (var template in root.Elements().Where(e => e.Name.LocalName == "Template"))
            ReadTemplate(template);

        var body = kind == "LiveActivity" ? LiveActivity(root) : Widget(root);
        if (body is null)
            return;

        foreach (var template in _templates.Values.Where(t => !t.Used && t.Element.Document == document))
            Report(Layouts.Diagnostics.DrawnNowhere, template.Element.Attribute("Name") ?? (XObject)template.Element, $"The template '{template.Name}' is used nowhere");
        foreach (var shared in _shared.Values.Where(s => !s.Used))
            Report(Layouts.Diagnostics.DrawnNowhere, shared.Element.Attribute("Shared") ?? (XObject)shared.Element, $"The shared node '{shared.Element.Attribute("Shared")?.Value}' is used nowhere");

        if (kind == "LiveActivity" && root.Attribute("Payload")?.Value == "Template")
            body += TemplateBody(root, className);

        var code = new StringBuilder();
        code.Append("// <auto-generated/>\n");
        code.Append($"// Written from {System.IO.Path.GetFileName(_file.Path)} by Plugin.Maui.Spine.Common's layout generator.\n");
        code.Append("#nullable enable\n");
        code.Append("#pragma warning disable CS1591\n");
        // The W.* styling is extension methods, which a qualified call cannot reach.
        code.Append("using Plugin.Maui.Spine.Common;\n\n");
        if (ns.Length > 0)
            code.Append($"namespace {ns};\n\n");
        code.Append($"/// <summary>The layout in {System.IO.Path.GetFileName(_file.Path)}.</summary>\n");
        code.Append($"[global::System.CodeDom.Compiler.GeneratedCode(\"Plugin.Maui.Spine.Layouts\", \"1.0\")]\n");
        code.Append($"public static partial class {className}\n{{\n");
        code.Append(body);
        if (_actions.Count > 0)
        {
            code.Append("\n    /// <summary>The names of the file's button actions, as the handler gets them.</summary>\n");
            code.Append("    public static class Actions\n    {\n");
            foreach (var action in _actions)
                code.Append($"        public const string {action.Key} = {Lit(action.Value)};\n");
            code.Append("    }\n");
        }
        var icons = _icons.Distinct(StringComparer.Ordinal).ToList();
        code.Append("\n    /// <summary>Every icon the layout can show, to be stored at start (<c>SpineWidgetsOptions.Icons</c>).</summary>\n");
        code.Append($"    public static global::System.Collections.Generic.IReadOnlyList<string> Icons {{ get; }} = new string[] {{ {string.Join(", ", icons.Select(Lit))} }};\n");
        code.Append("}\n");

        HintName = (ns.Length > 0 ? ns + "." : "") + className + ".layout.g.cs";
        Source = code.ToString();
    }

    // ---- The two kinds of layout ----

    string? LiveActivity(XElement root)
    {
        var model = ModelParameter();
        var regions = new List<string>();
        string? android = null;
        var sharedCode = new StringBuilder();

        // Shared nodes first, so a Use anywhere can find them.
        foreach (var section in root.Elements().Where(e => e.Name.LocalName == "LiveActivity.Shared"))
            foreach (var element in section.Elements())
            {
                if (element.Attribute("Shared") is not { Value.Length: > 0 } name)
                {
                    Report(Layouts.Diagnostics.UnknownAttribute, element, "A node in LiveActivity.Shared needs a Shared name");
                    continue;
                }
                var guard = element.Attribute("If") is { } ifAttr ? GuardKey(ifAttr, negate: false)
                    : element.Attribute("IfNot") is { } ifNot ? GuardKey(ifNot, negate: true) : null;
                if (_shared.ContainsKey(name.Value))
                    Report(Layouts.Diagnostics.DuplicateResource, name, $"There is already a shared node named '{name.Value}'");
                _shared[name.Value] = new SharedNode(element, Camel(name.Value), guard);
            }

        var top = new Scope();
        foreach (var shared in _shared.Values)
        {
            var scope = top.Child();
            scope.InShared = true;
            scope.Region = "Shared";
            if (Node(shared.Element, scope) is { } node)
                sharedCode.Append($"        {Common}LayoutValues.Share(shared, {Lit(shared.Key)},{node.Code});\n");
        }

        var lockScreen = root.Elements().Where(e => IsNodeElement(e)).ToList();
        if (lockScreen.Count > 1)
            Report(Layouts.Diagnostics.UnknownElement, lockScreen[1], "A LiveActivity has one tree for the Lock Screen; put the rest in a stack");
        if (lockScreen.FirstOrDefault() is { } lockTree)
            Region(regions, "LockScreen", lockTree, top);

        foreach (var element in root.Elements())
        {
            var name = element.Name.LocalName;
            if (IsNodeElement(element) || name is "Template" or "LiveActivity.Shared")
                continue;
            switch (name)
            {
                case "LiveActivity.Island":
                    foreach (var part in element.Elements())
                    {
                        var region = part.Name.LocalName.StartsWith("Island.", StringComparison.Ordinal)
                            && Vocabulary.Island.TryGetValue(part.Name.LocalName.Substring(7), out var property) ? property : null;
                        if (region is null)
                        {
                            Report(Layouts.Diagnostics.UnknownElement, part, $"'{part.Name.LocalName}' is not a region of the island: Island.{string.Join(", Island.", Vocabulary.Island.Keys)}");
                            continue;
                        }
                        if (One(part) is { } tree)
                            Region(regions, region, tree, top);
                    }
                    break;
                case "LiveActivity.Small":
                    if (One(element) is { } small)
                        Region(regions, "Small", small, top);
                    break;
                case "LiveActivity.Notification":
                    CheckAttributes(element, name);
                    android = Notification(element, top);
                    break;
                default:
                    Report(Layouts.Diagnostics.UnknownElement, element, $"'{name}' does not belong in a LiveActivity");
                    break;
            }
        }

        var properties = new List<string>();
        if (_shared.Count > 0)
            properties.Add("Shared = shared.Count == 0 ? null : shared");
        if (root.Attribute("Background") is { } background && Color(background, top) is { } color)
            properties.Add($"Background = {Out(color.Code, Kind.Color, background)}");
        if (root.Attribute("SystemBackground") is { } system && Bool(system, top) is { } systemBackground)
        {
            if (_tp is not null && Bound(systemBackground)) _tp.Fail("SystemBackground is bound", system);
            properties.Add($"SystemBackground = {systemBackground}");
        }
        if (root.Attribute("ActionColor") is { } actionColor && Color(actionColor, top) is { } action)
            properties.Add($"ActionColor = {Out(action.Code, Kind.Color, actionColor)}");
        if (root.Attribute("Link") is { } link && Value(link, Kind.Uri, null, top) is { } uri)
            properties.Add($"Link = {Out(uri.Code, Kind.Uri, link)}");
        properties.AddRange(regions);
        if (android is not null)
            properties.Add($"Android = {android}");

        var code = new StringBuilder();
        if (_tp is not null)
        {
            // The template: the same tree with placeholders, built once, with no model in reach.
            var functions = Templates(top);
            code.Append("        var shared = new global::System.Collections.Generic.Dictionary<string, " + Common + "WidgetNode>();\n");
            code.Append(sharedCode);
            code.Append("#line default\n");
            code.Append(functions);
            code.Append($"        return new {Common}LiveActivityLayout\n        {{\n");
            foreach (var property in properties)
                code.Append($"            {property},\n");
            code.Append("#line default\n        };\n");
            return code.ToString();
        }
        code.Append($"    /// <summary>The layout for <paramref name=\"m\"/>.</summary>\n");
        code.Append($"    public static {Common}LiveActivityLayout Build({Parameters(model)})\n    {{\n");
        if (_shared.Count > 0)
        {
            code.Append($"        var shared = new global::System.Collections.Generic.Dictionary<string, {Common}WidgetNode>();\n");
            code.Append(sharedCode);
            code.Append("#line default\n");
        }
        code.Append(Templates(top));
        code.Append($"        return new {Common}LiveActivityLayout\n        {{\n");
        foreach (var property in properties)
            code.Append($"            {property},\n");
        code.Append("#line default\n        };\n    }\n");
        code.Append(Binding(model));
        return code.ToString();
    }

    string? Widget(XElement root)
    {
        var model = ModelParameter();
        var top = new Scope();
        var trees = new List<(string Family, string Code)>();
        string? fallback = null;

        var defaults = root.Elements().Where(IsNodeElement).ToList();
        if (defaults.Count > 1)
            Report(Layouts.Diagnostics.UnknownElement, defaults[1], "A Widget has one tree for every size; put the rest in a stack, or give a size its own with Widget.Small and the rest");
        if (defaults.FirstOrDefault() is { } tree)
        {
            var scope = RegionScope(top, "HomeScreen");
            if (Node(tree, scope) is { } node)
                fallback = node.MaybeNull ? $"({node.Code}) ?? {Common}W.VStack()" : node.Code;
        }

        foreach (var element in root.Elements())
        {
            var name = element.Name.LocalName;
            if (IsNodeElement(element) || name == "Template")
                continue;
            if (name.StartsWith("Widget.", StringComparison.Ordinal) && Vocabulary.Families.TryGetValue(name.Substring(7), out var family))
            {
                if (One(element) is { } sizeTree && Node(sizeTree, RegionScope(top, name.Substring(7))) is { } node)
                    trees.Add((family, node.MaybeNull ? $"({node.Code}) ?? {Common}W.VStack()" : node.Code));
            }
            else
                Report(Layouts.Diagnostics.UnknownElement, element, $"'{name}' does not belong in a Widget: a tree, Template, or Widget.{string.Join(", Widget.", Vocabulary.Families.Keys)}");
        }

        if (fallback is null && trees.Count == 0)
        {
            Report(Layouts.Diagnostics.DrawnNowhere, root, "The widget has no tree");
            return null;
        }

        var code = new StringBuilder();
        string returns, value;
        if (trees.Count == 0)
            (returns, value) = ($"{Common}WidgetNode", fallback!);
        else
        {
            var dictionary = $"new global::System.Collections.Generic.Dictionary<{Common}WidgetFamily, {Common}WidgetNode>\n        {{\n"
                + string.Concat(trees.Select(t => $"            [{Common}WidgetFamily.{t.Family}] = {t.Code},\n"))
                + "#line default\n        }";
            (returns, value) = fallback is null
                ? ($"global::System.Collections.Generic.IReadOnlyDictionary<{Common}WidgetFamily, {Common}WidgetNode>", dictionary)
                : ($"{Common}WidgetNode", $"{Common}W.Adaptive({fallback}, {dictionary})");
        }

        code.Append(_model is null ? "    /// <summary>The widget's tree.</summary>\n" : "    /// <summary>The widget's tree for <paramref name=\"m\"/>.</summary>\n");
        code.Append($"    public static {returns} Build({Parameters(model)})\n    {{\n");
        code.Append(Templates(top));
        code.Append($"        return {value};\n#line default\n    }}\n");

        string? surface = null;
        if (root.Attribute("Background") is { } background)
            surface = Surface(background, top);
        if (surface is not null)
        {
            code.Append("\n    /// <summary>The widget's surface.</summary>\n");
            code.Append($"    public static {Common}WidgetSurface Surface({Parameters(model)}) => {surface};\n#line default\n");
        }

        if (model is not null)
        {
            var strings = _usesStrings ? ", " + Common + "ISpineStrings strings" : "";
            var pass = _usesStrings ? ", strings" : "";
            code.Append("\n    /// <summary>A timeline of one entry per model, each shown from its time.</summary>\n");
            code.Append($"    public static {Common}WidgetTimeline Timeline(global::System.Collections.Generic.IEnumerable<(global::System.DateTimeOffset At, {model} Entry)> entries{strings})\n    {{\n");
            code.Append($"        var timeline = new {Common}WidgetTimeline();\n");
            code.Append($"        foreach (var (at, entry) in entries)\n            timeline.Add(at, Build(entry{pass}), {(surface is null ? "null" : $"Surface(entry{pass})")});\n");
            code.Append("        return timeline;\n    }\n");
        }
        return code.ToString();
    }

    /// <summary>For a model that announces its changes: <c>Bind</c> and <c>StartAsync</c>, which keep an activity in step with it.</summary>
    string Binding(string? model)
    {
        if (model is null || !Announces(_model!))
            return "";
        var strings = _usesStrings ? $", {Common}ISpineStrings strings" : "";
        var pass = _usesStrings ? ", strings" : "";
        var watch = _usesStrings ? "strings" : "null";
        var sources = _sources.Count == 0 ? "null" : $"static m => new object?[] {{ {string.Join(", ", _sources.Select(s => "m." + s))} }}";
        var staleAt = $"global::System.Func<{model}, global::System.DateTimeOffset?>? staleAt = null";
        var options = $"{Common}LiveActivityBindingOptions? options = null";
        var code = new StringBuilder();
        code.Append("\n    /// <summary>Keeps <paramref name=\"activity\"/> in step with <paramref name=\"m\"/>, one update per burst of changes.</summary>\n");
        code.Append($"    public static {Common}LiveActivityBinding Bind({Common}LiveActivity activity, {model} m{strings}, {staleAt}, {options}) =>\n");
        code.Append($"        {Common}LiveActivityBindings.Bind(activity, m, model => Build(model{pass}), staleAt, {sources}, {watch}, options);\n");
        code.Append("\n    /// <summary>Starts an activity of <paramref name=\"kind\"/> drawn from <paramref name=\"m\"/> and keeps it in step; null when none was started.</summary>\n");
        code.Append($"    public static global::System.Threading.Tasks.Task<{Common}LiveActivityBinding?> StartAsync({Common}ILiveActivityService activities, string kind, {model} m{strings},\n");
        code.Append($"        {staleAt}, {options}, string? channel = null) =>\n");
        code.Append($"        {Common}LiveActivityBindings.StartBoundAsync(activities, kind, m, model => Build(model{pass}), staleAt, {sources}, {watch}, options, channel);\n");
        return code.ToString();
    }

    /// <summary>
    /// For <c>Payload="Template"</c>: the layout again, with every bound value a placeholder (<c>Template</c>), and the
    /// values alone (<c>Data(model)</c>), collected under the same conditions and loops as the tree draws them.
    /// </summary>
    string TemplateBody(XElement root, string className)
    {
        var model = ModelParameter();
        if (model is null)
        {
            Report(Layouts.Diagnostics.NotATemplate, root.Attribute("Payload"), "A template's values come from a Model, and the file has none");
            return "";
        }
        var reported = Diagnostics.Count;
        _tp = new TemplatePass();
        _quiet = true;
        foreach (var shared in _shared.Values) shared.Used = false;
        foreach (var template in _templates.Values) template.Used = false;
        string body;
        try { body = LiveActivity(root) ?? ""; }
        finally { _quiet = false; }
        var pass = _tp;
        _tp = null;
        if (pass.Failure is null && BoundCode.Match(body) is { Success: true } leak)
            pass.Fail($"'{body.Substring(Math.Max(0, leak.Index - 60), Math.Min(120, body.Length - Math.Max(0, leak.Index - 60))).Replace("\n", " ")}' uses the model where the template cannot reach it", root);
        if (pass.Failure is { } failure)
        {
            Report(Layouts.Diagnostics.NotATemplate, pass.FailedAt ?? root, $"The layout is sent as a tree: {failure}");
            return "";
        }

        var name = Kebab(className.EndsWith("Layout", StringComparison.Ordinal) ? className.Substring(0, className.Length - 6) : className);
        var code = new StringBuilder();
        code.Append("\n    static " + Common + "LiveActivityTemplate? s_template;\n\n");
        code.Append("    /// <summary>The layout as a template the device keeps, so a push carries only <see cref=\"Data\"/>: install it with <c>SpineWidgetsOptions.Templates</c>.</summary>\n");
        code.Append($"    public static {Common}LiveActivityTemplate Template => s_template ??= {Common}LiveActivityTemplate.Create({Lit(name)}, Icons, static t =>\n    {{\n");
        code.Append(body);
        code.Append("    });\n\n");
        code.Append("    /// <summary>The values <see cref=\"Template\"/> needs for <paramref name=\"m\"/>: with it, what <see cref=\"Build\"/> writes.</summary>\n");
        code.Append($"    public static {Common}LiveActivityTemplateData Data({Parameters(model)})\n    {{\n");
        code.Append($"        var d = new {Common}LiveActivityTemplateValues();\n");
        code.Append(pass.Functions);
        code.Append(pass.Data);
        code.Append("#line default\n        return d.For(Template);\n    }\n");
        return code.ToString();
    }

    static string Kebab(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && builder.Length > 0) builder.Append('-');
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    string? ModelParameter() => _model?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    string Parameters(string? model)
    {
        var parameters = new List<string>();
        if (model is not null) parameters.Add($"{model} m");
        if (_usesStrings) parameters.Add($"{Common}ISpineStrings strings");
        return string.Join(", ", parameters);
    }

    Scope RegionScope(Scope top, string region)
    {
        var scope = top.Child();
        scope.Region = region;
        scope.Parts = new HashSet<string>(StringComparer.Ordinal);
        return scope;
    }

    void Region(List<string> regions, string property, XElement tree, Scope top)
    {
        if (Node(tree, RegionScope(top, property)) is { } node)
            regions.Add($"{property} = {node.Code}");
    }

    string? Notification(XElement element, Scope scope)
    {
        var arguments = new List<string>();
        foreach (var (attribute, parameter) in new[] { ("Title", "Title"), ("Text", "Body"), ("Chip", "Chip"), ("Icon", "Icon") })
            if (element.Attribute(attribute) is { } a && Text(a, scope, required: false) is { } text)
                arguments.Add($"{parameter}: {Out(text.Code, Kind.Text, a)}");
        if (element.Attribute("Presentation") is { } presentation && Enum(presentation, "LiveUpdatePresentation") is { } p)
            arguments.Add($"Presentation: {p}");
        return $"{Line(element)}new {Common}LiveUpdateText({string.Join(", ", arguments)})";
    }

    string? Surface(XAttribute attribute, Scope scope)
    {
        var value = Parse(attribute);
        if (value is Markup { Name: "LinearGradient" } gradient)
        {
            var colors = gradient.Positional.Select(c => Color(attribute, c, scope)?.Code).ToList();
            if (colors.Count < 2 || colors.Any(c => c is null))
            {
                Report(Layouts.Diagnostics.BadLiteral, attribute, "A gradient takes two colors or more: {LinearGradient #1C222D, #0B0E13}");
                return null;
            }
            var direction = gradient["Direction"] is Literal d ? EnumValue(attribute, "WidgetGradientDirection", d.Text) : null;
            return $"{Line(attribute)}new {Common}WidgetSurface(new {Common}WidgetGradient(new {Common}WidgetColor[] {{ {string.Join(", ", colors)} }}{(direction is null ? "" : ", " + direction)}))";
        }
        return Color(attribute, value, scope) is { } color ? $"{Line(attribute)}new {Common}WidgetSurface({color.Sure})" : null;
    }

    // ---- Templates and resources ----

    void ReadResources(XDocument document)
    {
        if (document.Root is not { } root)
            return;
        var file = document.Annotation<SourceFile>();
        foreach (var element in root.Elements())
        {
            if (element.Attribute("Name") is not { Value.Length: > 0 } name)
            {
                Report(Layouts.Diagnostics.UnknownAttribute, element, "A resource needs a Name", file);
                continue;
            }
            if (element.Name.LocalName == "Template")
            {
                if (_templates.ContainsKey(name.Value))
                    Report(Layouts.Diagnostics.DuplicateResource, name, $"There is already a template named '{name.Value}'", file);
                ReadTemplate(element);
                continue;
            }
            if (_values.ContainsKey(name.Value))
                Report(Layouts.Diagnostics.DuplicateResource, name, $"There is already a resource named '{name.Value}'", file);
            _values[name.Value] = new ResourceValue(element, element.Attribute("Value")?.Value ?? element.Value.Trim());
        }
    }

    void ReadTemplate(XElement element)
    {
        CheckAttributes(element, "Template");
        if (element.Attribute("Name") is not { Value.Length: > 0 } name)
        {
            Report(Layouts.Diagnostics.UnknownAttribute, element, "A template needs a Name");
            return;
        }
        if (Vocabulary.Elements.ContainsKey(name.Value))
        {
            Report(Layouts.Diagnostics.UnknownElement, name, $"A template cannot be named '{name.Value}', which is one of Spine's elements");
            return;
        }
        if (_templates.TryGetValue(name.Value, out var existing) && existing.Element.Document == element.Document)
            Report(Layouts.Diagnostics.DuplicateResource, name, $"There is already a template named '{name.Value}'");

        var signature = element.Attribute("Parameters")?.Value ?? "";
        var parameters = new List<Parameter>();
        foreach (var part in SplitTopLevel(signature, ','))
        {
            var declaration = part.Split('=')[0].Trim();
            var space = declaration.LastIndexOf(' ');
            if (space <= 0)
            {
                Report(Layouts.Diagnostics.BadLiteral, element.Attribute("Parameters"), $"'{part.Trim()}' is not a parameter: write it as C#, double Size = 40");
                continue;
            }
            var type = declaration.Substring(0, space).Trim();
            parameters.Add(new Parameter(type, declaration.Substring(space + 1).Trim(), ParameterType(type)));
        }
        _templates[name.Value] = new Template(element, name.Value, parameters, signature);
    }

    string Templates(Scope top)
    {
        var code = new StringBuilder();
        // Templates used only by other templates are found while those are written, so repeat until none is new.
        var written = new HashSet<string>(StringComparer.Ordinal);
        var functions = new List<string>();
        while (_templates.Values.FirstOrDefault(t => t.Used && !written.Contains(t.Name)) is { } template)
        {
            written.Add(template.Name);
            var vars = new Dictionary<string, Var>(StringComparer.Ordinal);
            foreach (var parameter in template.Parameters)
                vars[parameter.Name] = new Var(parameter.Name, parameter.Symbol, parameter.Type.EndsWith("?", StringComparison.Ordinal));
            var scope = top.WithVars(vars);
            scope.InTemplate = true;
            scope.Region = "Template";
            var signature = template.Signature + (template.HasContent ? (template.Signature.Trim().Length > 0 ? ", " : "") + $"{Common}WidgetNode?[]? content = null" : "");
            if (_tp is null)
            {
                var body = One(template.Element, ignore: "Parameters") is { } root ? Node(root, scope) : null;
                functions.Add($"        {Common}WidgetNode? {template.Name}({signature}) =>{body?.Code ?? " null"};\n#line default\n");
                continue;
            }
            // On the template pass each use of the template is a site: its keys are the site's key and their own.
            var (site, values, depth) = (_tp.Site, _tp.Values, _tp.Depth);
            (_tp.Site, _tp.Values, _tp.Depth) = ("site", "d", 0);
            var keys = _tp.Restart();
            var shape = Capture(() => One(template.Element, ignore: "Parameters") is { } root ? Node(root, scope) : null, out var collected);
            _tp.Resume(keys);
            (_tp.Site, _tp.Values, _tp.Depth) = (site, values, depth);
            var parameters = template.Signature.Trim().Length > 0 ? ", " + template.Signature : "";
            functions.Add($"        {Common}WidgetNode? {template.Name}(string site{(template.HasContent ? parameters + $", {Common}WidgetNode?[]? content = null" : parameters)}) =>{shape?.Code ?? " null"};\n#line default\n");
            _tp.Functions.Append($"        void {template.Name}Data({Common}LiveActivityTemplateValues d, string site{parameters})\n        {{\n{collected}#line default\n        }}\n");
        }
        foreach (var function in functions)
            code.Append(function);
        return code.ToString();
    }

    ITypeSymbol? ParameterType(string type)
    {
        var nullable = type.EndsWith("?", StringComparison.Ordinal);
        var name = nullable ? type.Substring(0, type.Length - 1) : type;
        ITypeSymbol? symbol = name switch
        {
            "double" => _compilation.GetSpecialType(SpecialType.System_Double),
            "float" => _compilation.GetSpecialType(SpecialType.System_Single),
            "int" => _compilation.GetSpecialType(SpecialType.System_Int32),
            "long" => _compilation.GetSpecialType(SpecialType.System_Int64),
            "bool" => _compilation.GetSpecialType(SpecialType.System_Boolean),
            "string" => _compilation.GetSpecialType(SpecialType.System_String),
            "WidgetColor" => _compilation.GetTypeByMetadataName("Plugin.Maui.Spine.Common.WidgetColor"),
            "WidgetNode" => _compilation.GetTypeByMetadataName("Plugin.Maui.Spine.Common.WidgetNode"),
            "TextRole" => _compilation.GetTypeByMetadataName("Plugin.Maui.Spine.Common.TextRole"),
            "DateTimeOffset" => _compilation.GetTypeByMetadataName("System.DateTimeOffset"),
            "TimeSpan" => _compilation.GetTypeByMetadataName("System.TimeSpan"),
            _ => FindType(name),
        };
        if (symbol is null || !nullable)
            return symbol;
        return symbol.IsValueType
            ? _compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(symbol)
            : symbol.WithNullableAnnotation(NullableAnnotation.Annotated);
    }

    INamedTypeSymbol? FindType(string name)
    {
        if (_compilation.GetTypeByMetadataName(name) is { } exact)
            return exact;
        foreach (var ns in new[] { _model?.ContainingNamespace?.ToDisplayString(), _rootNamespace, "Plugin.Maui.Spine.Common" })
            if (ns is { Length: > 0 } && _compilation.GetTypeByMetadataName(ns + "." + name) is { } found)
                return found;
        return null;
    }

    // ---- Nodes ----

    static bool IsNodeElement(XElement element) =>
        !element.Name.LocalName.Contains('.') && element.Name.LocalName is not ("Template" or "On");

    XElement? One(XElement container, string? ignore = null)
    {
        if (ignore is null)
            CheckAttributes(container, container.Name.LocalName);
        var children = container.Elements().ToList();
        Text(container);
        if (children.Count == 0)
        {
            Report(Layouts.Diagnostics.DrawnNowhere, container, $"'{container.Name.LocalName}' is empty");
            return null;
        }
        if (children.Count > 1)
            Report(Layouts.Diagnostics.UnknownElement, children[1], $"'{container.Name.LocalName}' holds one element; put several in a stack");
        return children[0];
    }

    void Text(XElement element)
    {
        foreach (var text in element.Nodes().OfType<XText>().Where(t => t is not XCData && t.Value.Trim().Length > 0))
            Report(Layouts.Diagnostics.UnknownElement, text, "Text goes in a Label: <Label Text=\"…\" />");
    }

    Emitted? Node(XElement element, Scope outer)
    {
        var name = element.Name.LocalName;
        if (element.Name.NamespaceName is { Length: > 0 } xmlns && xmlns != Vocabulary.Namespace)
        {
            Report(Layouts.Diagnostics.UnknownElement, element, $"'{name}' is in the namespace '{xmlns}'; Spine's is {Vocabulary.Namespace}");
            return null;
        }
        if (element.Attribute("Shared") is not null && name != "Use" && !outer.InShared)
        {
            Report(Layouts.Diagnostics.UnknownAttribute, element.Attribute("Shared") ?? (XObject)element, "Shared names a node only in LiveActivity.Shared");
            return null;
        }

        var conditions = Conditions(element, outer);
        var scope = outer.Child();
        foreach (var condition in conditions)
            Narrow(scope, condition, true);
        if (element.Attribute("Description") is not null || IsTrue(element.Attribute("Decorative")))
            scope.Described = true;

        Emitted? Emit()
        {
            if (_templates.TryGetValue(name, out var template))
                return TemplateCall(element, template, scope);
            if (!Vocabulary.Nodes.Contains(name))
            {
                Report(Layouts.Diagnostics.UnknownElement, element,
                    name is "ForEach" or "Content" ? $"{name} goes among a stack's children" : $"There is no element '{name}' and no template of that name");
                return null;
            }
            CheckAttributes(element, name);
            var emitted = name switch
            {
                "VStack" or "HStack" or "ZStack" => Stack(element, name, scope),
                "Choose" => Choose(element, scope),
                "Use" => Use(element, scope),
                "Button" => Button(element, scope),
                _ => Leaf(element, name, scope),
            };
            if (emitted is not null && name is not ("Choose" or "Use") && !(_tp is not null && emitted.Code.StartsWith("t.Node(", StringComparison.Ordinal)))
                emitted = new Emitted(Overrides(emitted.Code, element, name, scope), emitted.MaybeNull);
            return emitted;
        }
        var guarded = "";
        var node = conditions.Count > 0 ? Capture(Emit, out guarded) : Emit();
        if (node is null)
            return null;

        if (element.Attribute("Part") is { } part && outer.Parts is { } parts && !outer.InTemplate && !parts.Add(part.Value))
            Report(Layouts.Diagnostics.DuplicatePart, part, $"A second {part.Value} in {outer.Region}: Android takes the first for its notification");

        if (conditions.Count == 0)
            return new Emitted($"{Line(element)}{node.Code}", node.MaybeNull);
        var test = string.Join(" && ", conditions.Select(c => c.Code));
        if (_tp is not null)
        {
            var key = _tp.Key();
            _tp.Data.Append($"        if ({_tp.Values}.If({key}, {test}))\n        {{\n{guarded}        }}\n");
            return new Emitted($"{Line(element)}t.If({key}, {node.Code})", true);
        }
        return new Emitted($"{Line(element)}({test} ? ({Common}WidgetNode?)({node.Code}) : null)", true);
    }

    List<Test> Conditions(XElement element, Scope scope)
    {
        var conditions = new List<Test>();
        if (element.Attribute("If") is { } ifAttr && Condition(ifAttr, false, scope) is { } yes)
            conditions.Add(yes);
        if (element.Attribute("IfNot") is { } ifNot && Condition(ifNot, true, scope) is { } no)
            conditions.Add(no);
        return conditions;
    }

    static void Narrow(Scope scope, Test condition, bool held)
    {
        scope.Guards.Add(held ? condition.Key : Negate(condition.Key));
        if (condition.Narrows is { } path && condition.NarrowsWhenTrue == held)
            scope.Present.Add(path);
    }

    static string Negate(string key) => key.StartsWith("!", StringComparison.Ordinal) ? key.Substring(1) : "!" + key;

    Emitted? Stack(XElement element, string name, Scope scope)
    {
        var children = Children(element, scope);
        return children is null ? null : new Emitted($"{Common}W.{name}({children})", false);
    }

    /// <summary>A stack's children as arguments, or as one collection expression where a ForEach or Content spreads.</summary>
    string? Children(XElement element, Scope scope)
    {
        Text(element);
        var items = new List<string>();
        var spread = false;
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            if (name == "On")
                continue;
            if (name == "ForEach")
            {
                if (ForEach(child, scope) is { } each)
                {
                    items.Add(_tp is null ? ".. " + each : each);
                    spread |= _tp is null;
                }
                continue;
            }
            if (name == "Content")
            {
                if (!scope.InTemplate)
                    Report(Layouts.Diagnostics.UnknownElement, child, "Content belongs in a template");
                items.Add($"{Line(child)}.. (content ?? global::System.Array.Empty<{Common}WidgetNode?>())");
                spread = true;
                continue;
            }
            if (name.Contains('.'))
            {
                Report(Layouts.Diagnostics.UnknownElement, child, $"'{name}' does not belong in a {element.Name.LocalName}");
                continue;
            }
            if (Node(child, scope) is { } node)
                items.Add(node.Code);
        }
        return spread ? $"[{string.Join(",", items)}]" : string.Join(",", items);
    }

    string? ForEach(XElement element, Scope scope)
    {
        CheckAttributes(element, "ForEach");
        if (element.Attribute("Items") is not { } itemsAttr)
        {
            Report(Layouts.Diagnostics.UnknownAttribute, element, "A ForEach needs Items=\"{…}\"");
            return null;
        }
        if (Parse(itemsAttr) is not Markup { Path: { } path } || Resolve(path, scope, itemsAttr) is not { } items)
        {
            if (Parse(itemsAttr) is not Markup)
                Report(Layouts.Diagnostics.WrongType, itemsAttr, "Items is bound to a list: Items=\"{Goals}\"");
            return null;
        }
        var itemType = ElementType(items.Type);
        if (items.Type is not null && itemType is null)
        {
            Report(Layouts.Diagnostics.WrongType, itemsAttr, $"'{path}' is not a list");
            return null;
        }
        var max = element.Attribute("Max") is { } maxAttr ? Value(maxAttr, Kind.Int, null, scope)?.Code : null;

        var variable = "item" + ++_loops;
        var inner = scope.Child(new Var(variable, itemType, itemType is not null && MaybeNull(itemType)));
        if (_tp is not null)
            return TemplateEach(element, items.Code, max, variable, scope, inner);
        var nodes = new List<string>();
        string? empty = null;
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "ForEach.Empty")
            {
                var children = Children(child, scope);
                empty = children is null ? null : Sequence(children);
                continue;
            }
            if (child.Name.LocalName == "ForEach")
            {
                if (ForEach(child, inner) is { } nested) nodes.Add(".. " + nested);
            }
            else if (Node(child, inner) is { } node)
                nodes.Add(node.Code);
        }
        return $"{Line(element)}{Common}LayoutValues.Each({items.Code}, {max ?? "null"}, {variable} => {Sequence(string.Join(",", nodes))}, {empty ?? "null"})";
    }

    /// <summary>
    /// A ForEach on the template pass: the item's nodes once, as <c>$each</c>, and the values of each item collected in a
    /// list under the key, each item its own scope.
    /// </summary>
    string TemplateEach(XElement element, string items, string? max, string variable, Scope scope, Scope inner)
    {
        var pass = _tp!;
        var key = pass.Key();
        var outer = pass.Values;
        var nodes = new List<string>();
        string? empty = null;
        var emptyValues = "";
        pass.Depth++;
        pass.Values = "d" + pass.Depth;
        var itemValues = Capture(() =>
        {
            foreach (var child in element.Elements().Where(c => c.Name.LocalName != "ForEach.Empty"))
            {
                var code = child.Name.LocalName == "ForEach" ? ForEach(child, inner) : Node(child, inner)?.Code;
                if (code is not null) nodes.Add(code);
            }
            return 0;
        }, out var collected);
        pass.Values = outer;
        pass.Depth--;
        _ = itemValues;
        if (element.Elements().FirstOrDefault(c => c.Name.LocalName == "ForEach.Empty") is { } emptyElement)
            empty = Capture(() => Children(emptyElement, scope), out emptyValues);
        var lambda = "d" + (pass.Depth + 1);
        pass.Data.Append($"        if (!{outer}.Each({key}, {items}, {max ?? "null"}, ({lambda}, {variable}) =>\n        {{\n{collected}        }}))\n        {{\n{emptyValues}        }}\n");
        return $"{Line(element)}t.Each({key}, [{string.Join(",", nodes)}]{(empty is null ? "" : $", [{(empty.StartsWith("[", StringComparison.Ordinal) ? empty.Substring(1, empty.Length - 2) : empty)}]")})";
    }

    /// <summary>Nodes as a sequence: a collection expression, which a ForEach or Content spreads into.</summary>
    static string Sequence(string items) =>
        $"(global::System.Collections.Generic.IEnumerable<{Common}WidgetNode?>)[{(items.StartsWith("[", StringComparison.Ordinal) ? items.Substring(1, items.Length - 2) : items)}]";

    static string Collection(string items) => items.StartsWith("[", StringComparison.Ordinal) ? items : $"[{items}]";

    Emitted? Choose(XElement element, Scope scope)
    {
        var branches = new List<(string? Test, string Code)>();
        var branchValues = new List<string>();
        var rest = scope.Child();
        foreach (var branch in element.Elements())
        {
            var name = branch.Name.LocalName;
            if (name is not ("When" or "Otherwise"))
            {
                Report(Layouts.Diagnostics.UnknownElement, branch, $"A Choose holds When and Otherwise, not '{name}'");
                continue;
            }
            CheckAttributes(branch, name);
            var conditions = name == "When" ? Conditions(branch, rest) : [];
            if (name == "When" && conditions.Count == 0)
            {
                Report(Layouts.Diagnostics.UnknownAttribute, branch, "A When needs If or IfNot");
                continue;
            }
            var inner = rest.Child();
            foreach (var condition in conditions)
                Narrow(inner, condition, true);
            var tree = One(branch, ignore: "");
            var node = Capture(() => tree is null ? null : Node(tree, inner), out var values);
            branches.Add((conditions.Count == 0 ? null : string.Join(" && ", conditions.Select(c => c.Code)), node?.Code ?? "null"));
            branchValues.Add(values);
            if (conditions.Count == 1)
                Narrow(rest, conditions[0], false);
            if (name == "Otherwise")
                break;
        }
        if (branches.Count == 0)
            return null;
        if (_tp is not null)
        {
            // if (d.If(a, first)) { … } else if (d.If(b, second)) { … } else { … }, and t.If(a, …, t.If(b, …, …)).
            var data = new StringBuilder();
            var shape = new StringBuilder();
            var closing = 0;
            for (var i = 0; i < branches.Count; i++)
            {
                var (test, node) = branches[i];
                if (test is null)
                {
                    data.Append($"        {(i > 0 ? "else " : "")}{{\n{branchValues[i]}        }}\n");
                    shape.Append(node);
                    break;
                }
                var key = _tp.Key();
                data.Append($"        {(i > 0 ? "else " : "")}if ({_tp.Values}.If({key}, {test}))\n        {{\n{branchValues[i]}        }}\n");
                shape.Append($"t.If({key}, {node}, ");
                closing++;
                if (i == branches.Count - 1)
                    shape.Append("null");
            }
            shape.Append(')', closing);
            _tp.Data.Append(data);
            return new Emitted(shape.ToString(), true);
        }
        var code = new StringBuilder("(");
        foreach (var (test, node) in branches)
            code.Append(test is null ? $"({Common}WidgetNode?)({node})" : $"{test} ? ({Common}WidgetNode?)({node}) : ");
        if (branches[branches.Count - 1].Test is not null)
            code.Append("null");
        code.Append(')');
        return new Emitted(code.ToString(), true);
    }

    Emitted? Use(XElement element, Scope scope)
    {
        if (element.Attribute("Shared") is not { } name)
        {
            Report(Layouts.Diagnostics.UnknownAttribute, element, "A Use names its shared node: <Use Shared=\"Graph\" />");
            return null;
        }
        if (scope.InShared)
        {
            Report(Layouts.Diagnostics.UnknownElement, element, "A shared node cannot use another");
            return null;
        }
        if (!_shared.TryGetValue(name.Value, out var shared))
        {
            Report(Layouts.Diagnostics.UnknownPath, name, $"There is no shared node named '{name.Value}'");
            return null;
        }
        shared.Used = true;
        if (shared.Guard is { } guard && !scope.Guards.Contains(guard))
            Report(Layouts.Diagnostics.UnguardedUse, element,
                $"'{name.Value}' is shared only {(guard.StartsWith("!", StringComparison.Ordinal) ? "unless " + guard.Substring(1) : "if " + guard)}; here it draws nothing otherwise. Give the Use, or a node around it, the same condition");
        var height = element.Attribute("Height") is { } h && Value(h, Kind.Number, null, scope)?.Code is { } heightCode ? Out(heightCode, Kind.Number, h) : null;
        return new Emitted($"{Common}W.Use({Lit(shared.Key)}{(height is null ? "" : ", " + height)})", false);
    }

    Emitted? Button(XElement element, Scope scope)
    {
        if (scope.Region is "CompactLeading" or "CompactTrailing" or "Minimal")
        {
            Report(Layouts.Diagnostics.UnknownElement, element, $"A button cannot be in {scope.Region}: iOS opens the app on a tap there");
            return null;
        }
        if (element.Attribute("Action") is not { Value.Length: > 0 } action)
        {
            Report(Layouts.Diagnostics.UnknownAttribute, element, "A Button needs an Action");
            return null;
        }
        var id = action.Value == "End" ? null : action.Value;
        if (id is not null)
            _actions[Pascal(id)] = id;
        var child = One(element, ignore: "") is { } tree ? Node(tree, scope) : null;
        if (child is null)
            return null;
        var actionCode = id is null ? $"{Common}LiveActivityActions.End" : Lit(id);
        return new Emitted($"{Common}W.Button({actionCode}, {(child.MaybeNull ? $"({child.Code}) ?? {Common}W.Spacer()" : child.Code)})", false);
    }

    Emitted? TemplateCall(XElement element, Template template, Scope scope)
    {
        template.Used = true;
        var arguments = new List<string>();
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName is not ("If" or "IfNot")))
        {
            var parameter = template.Parameters.FirstOrDefault(p => p.Name == attribute.Name.LocalName);
            if (parameter is null)
            {
                Report(Layouts.Diagnostics.UnknownAttribute, attribute,
                    $"The template '{template.Name}' has no parameter '{attribute.Name.LocalName}'{(template.Parameters.Count == 0 ? "" : ": " + string.Join(", ", template.Parameters.Select(p => p.Name)))}");
                continue;
            }
            if (Value(attribute, KindOf(parameter.Symbol), null, scope) is { } value)
            {
                var argument = parameter.Type.EndsWith("?", StringComparison.Ordinal) ? value.Code : value.Sure;
                if (_tp is not null && Bound(argument))
                    _tp.Fail($"the template '{template.Name}' is given '{attribute.Value}', which comes from the model; pass it inside the template instead", attribute);
                arguments.Add($"{parameter.Name}: {argument}");
            }
        }
        if (element.Elements().Any())
        {
            if (!template.HasContent)
                Report(Layouts.Diagnostics.UnknownElement, element.Elements().First(), $"The template '{template.Name}' has no <Content /> for what is put in it");
            else if (Children(element, scope) is { } content)
                arguments.Add($"content: {Collection(content)}");
        }
        if (_tp is not null)
        {
            var site = _tp.Key();
            var values = arguments.Where(a => !a.StartsWith("content:", StringComparison.Ordinal)).ToList();
            _tp.Data.Append($"        {template.Name}Data({_tp.Values}, {site}{string.Concat(values.Select(a => ", " + a))});\n");
            return new Emitted($"{template.Name}({site}{string.Concat(arguments.Select(a => ", " + a))})", true);
        }
        return new Emitted($"{template.Name}({string.Join(", ", arguments)})", true);
    }

    static Kind KindOf(ITypeSymbol? type)
    {
        var plain = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;
        return plain switch
        {
            null => Kind.Any,
            { SpecialType: SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Decimal } => Kind.Number,
            { SpecialType: SpecialType.System_Int32 or SpecialType.System_Int64 } => Kind.Int,
            { SpecialType: SpecialType.System_Boolean } => Kind.Bool,
            { SpecialType: SpecialType.System_String } => Kind.Text,
            { Name: "WidgetColor" } => Kind.Color,
            { Name: "DateTimeOffset" } => Kind.Date,
            { Name: "TimeSpan" } => Kind.Duration,
            _ => Kind.Any,
        };
    }

    Emitted? Leaf(XElement element, string name, Scope scope)
    {
        string? Required(string attribute, Kind kind, bool raw = false)
        {
            if (element.Attribute(attribute) is not { } a)
            {
                Report(Layouts.Diagnostics.UnknownAttribute, element, $"{name} needs {attribute}");
                return null;
            }
            if (kind == Kind.Text)
                return Text(a, scope, required: true)?.Code is { } text ? (raw ? text : Out(text, kind, a)) : null;
            if (Value(a, kind, null, scope) is not { } value)
                return null;
            if (value.MaybeNull)
                Report(Layouts.Diagnostics.NullableWithoutFallback, a, $"'{a.Value}' may be absent, and {name} needs its {attribute}: guard the element with If, or bind a value that is always there");
            return raw ? value.Sure : Out(value.Sure, kind, a);
        }

        string? Optional(string attribute, Kind kind, bool raw = false) =>
            element.Attribute(attribute) is { } a && (kind == Kind.Color ? Color(a, scope)?.Code : Value(a, kind, null, scope)?.Code) is { } code
                ? (raw ? code : Out(code, kind, a))
                : null;

        if (name is "Image" or "Sparkline" or "Ring" or "Progress" && !scope.Described)
            Report(Layouts.Diagnostics.Silent, element, $"A screen reader says nothing for this {name}: give it a Description, or Decorative=\"True\"");

        switch (name)
        {
            case "Label":
                return Required("Text", Kind.Text) is { } text ? new Emitted($"{Common}W.Label({text})", false) : null;
            case "Elapsed":
            {
                if (Required("Since", Kind.Date) is not { } since) return null;
                var format = element.Attribute("Format") is { } f ? Enum(f, "ElapsedFormat") : null;
                var prefix = element.Attribute("Prefix") is { } p && Text(p, scope, required: false)?.Code is { } prefixCode ? Out(prefixCode, Kind.Text, p) : null;
                return new Emitted($"{Common}W.Elapsed({since}{(format is null ? "" : ", " + format)}{(prefix is null ? "" : ", prefix: " + prefix)})", false);
            }
            case "Countdown":
            {
                if (Required("Until", Kind.Date) is not { } until) return null;
                var prefix = element.Attribute("Prefix") is { } p && Text(p, scope, required: false)?.Code is { } prefixCode ? Out(prefixCode, Kind.Text, p) : null;
                return new Emitted($"{Common}W.Countdown({until}{(prefix is null ? "" : ", " + prefix)})", false);
            }
            case "Icon":
            {
                if (element.Attribute("Source") is not { } source)
                {
                    Report(Layouts.Diagnostics.UnknownAttribute, element, "Icon needs Source");
                    return null;
                }
                var icon = Icon(source, element.Attribute("Values"), scope);
                if (icon is null) return null;
                icon = Out(icon, Kind.Icon, source);
                var tint = Optional("Tint", Kind.Color);
                var size = Optional("Size", Kind.Number);
                var code = $"{Common}W.Icon({icon}{(tint is null ? "" : ", color: " + tint)}{(size is null ? "" : ", size: " + size)})";
                return new Emitted(code, false);
            }
            case "Image":
            {
                if (Required("Source", Kind.Text) is not { } asset) return null;
                var height = Optional("Height", Kind.Number);
                return new Emitted($"{Common}W.Image({asset}{(height is null ? "" : ", " + height)})", false);
            }
            case "Progress":
            {
                if (Required("Value", Kind.Number, raw: true) is not { } value) return null;
                var tint = Optional("Tint", Kind.Color);
                if (_tp is not null && Bound(value))
                {
                    // The factory clamps its value, which would lose the placeholder: the value is set after it.
                    var key = _tp.Key();
                    _tp.Data.Append($"        {_tp.Values}.Set({key}, {Common}W.Progress({value}).Value);\n");
                    return new Emitted($"({Common}W.Progress(0{(tint is null ? "" : ", " + tint)}) with {{ Value = t.N({key}) }})", false);
                }
                return new Emitted($"{Common}W.Progress({value}{(tint is null ? "" : ", " + tint)})", false);
            }
            case "Ring":
            {
                var value = element.Attribute("Value") is { } v ? Value(v, Kind.Number, null, scope)?.Code : null;
                if (value is null) return null;
                var tint = Optional("Tint", Kind.Color);
                var size = Optional("Size", Kind.Number);
                var options = $"{(tint is null ? "" : ", color: " + tint)}{(size is null ? "" : ", size: " + size)}";
                if (_tp is not null && Bound(value))
                {
                    var key = _tp.Key();
                    _tp.Data.Append($"        {_tp.Values}.Set({key}, {Common}W.Ring({value}).Value);\n");
                    return new Emitted($"({Common}W.Ring(0{options}) with {{ Value = t.N({key}) }})", false);
                }
                return new Emitted($"{Common}W.Ring({value}{options})", false);
            }
            case "Sparkline":
            {
                if (Required("Values", Kind.Values, raw: true) is not { } values) return null;
                var color = Optional("Tint", Kind.Color) ?? Optional("Color", Kind.Color);
                var height = Optional("Height", Kind.Number);
                var options = $"{(color is null ? "" : ", color: " + color)}{(height is null ? "" : ", height: " + height)}";
                var from = element.Attribute("From");
                var to = element.Attribute("To");
                string code;
                if (from is not null || to is not null)
                {
                    if (Required("From", Kind.Date, raw: true) is not { } f || Required("To", Kind.Date, raw: true) is not { } t) return null;
                    var step = Optional("Step", Kind.Duration, raw: true);
                    if (_tp is not null && (Bound(values) || Bound(f) || Bound(t) || Bound(step)))
                    {
                        // The values are put on their time axis by the factory: the data carries its result, the template the rest.
                        var (kv, kf, kt, ks) = (_tp.Key(), _tp.Key(), _tp.Key(), _tp.Key());
                        _tp.Data.Append($"        {{\n            var line = {Common}W.Sparkline({values}, {f}, {t}{(step is null ? "" : ", " + step)});\n" +
                            $"            {_tp.Values}.Set({kv}, line.Values);\n            {_tp.Values}.Set({kf}, line.From);\n            {_tp.Values}.Set({kt}, line.To);\n            {_tp.Values}.Set({ks}, line.Step);\n        }}\n");
                        return new Emitted($"({Common}W.Sparkline(global::System.Array.Empty<double?>(){options}) with {{ Values = t.L({kv}), From = t.D({kf}), To = t.D({kt}), Step = t.N({ks}) }})", false);
                    }
                    code = $"{Common}W.Sparkline({values}, {f}, {t}{(step is null ? "" : ", " + step)}{options})";
                }
                else
                {
                    if (_tp is not null && Bound(values))
                    {
                        var kv = _tp.Key();
                        _tp.Data.Append($"        {_tp.Values}.Set({kv}, {Common}W.Sparkline({values}).Values);\n");
                        return new Emitted($"({Common}W.Sparkline(global::System.Array.Empty<double?>(){options}) with {{ Values = t.L({kv}) }})", false);
                    }
                    code = $"{Common}W.Sparkline({values}{options})";
                }
                return new Emitted(code, false);
            }
            case "Spacer":
                return new Emitted($"{Common}W.Spacer()", false);
            case "Divider":
                return new Emitted($"{Common}W.Divider()", false);
        }
        return null;
    }

    string? Icon(XAttribute source, XAttribute? values, Scope scope)
    {
        var value = Parse(source);
        if (value is Literal literal)
        {
            CheckIcon(source, literal.Text);
            _icons.Add(literal.Text);
            return Lit(literal.Text);
        }
        if (values is not null)
            foreach (var name in values.Value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0))
            {
                CheckIcon(values, name);
                _icons.Add(name);
            }
        var text = Text(source, scope, required: true);
        if (text?.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            _icons.AddRange(enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).Select(f => f.Name));
        return text?.Code;
    }

    void CheckIcon(XAttribute attribute, string name)
    {
        if (_knownIcons is null || name.Contains('.') && !name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return;
        var bare = name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        if (!_knownIcons.Contains(bare))
            Report(Layouts.Diagnostics.UnknownIcon, attribute, $"There is no icon '{name}' among Spine's icons or the app's SVGs");
    }

    static HashSet<string>? KnownIcons(Compilation compilation)
    {
        var types = new List<INamedTypeSymbol>();
        if (compilation.GetTypeByMetadataName("Plugin.Maui.Spine.Svg.Icons.SpineIcons") is { } spine)
            types.Add(spine);
        types.AddRange(compilation.GetSymbolsWithName("AppIcons", SymbolFilter.Type).OfType<INamedTypeSymbol>());
        if (types.Count == 0)
            return null;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
                if (field.HasConstantValue && field.ConstantValue is string value)
                    names.Add(value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? value.Substring(0, value.Length - 4) : value);
        return names;
    }

    // ---- Attributes on a node ----

    static readonly HashSet<string> Structural = ["If", "IfNot", "Shared", "Text", "Since", "Format", "Prefix", "Until", "Source", "Values",
        "Tint", "Size", "Height", "Value", "From", "To", "Step", "Color", "Action"];

    string Overrides(string code, XElement element, string name, Scope scope)
    {
        var attributes = element.Attributes().Where(a => !a.IsNamespaceDeclaration && !Structural.Contains(a.Name.LocalName)).ToList();
        if (attributes.Count > 0)
            code = Setters(code.Contains('\n') ? code + Line(element) : code, element, name, attributes, scope);

        foreach (var on in element.Elements().Where(e => e.Name.LocalName == "On"))
        {
            CheckOn(on, name);
            var platform = on.Attribute("Platform") is { } p ? Enum(p, "WidgetPlatform") : null;
            var place = on.Attribute("Surface") is { } s ? Enum(s, "WidgetPlace") : null;
            var size = on.Attribute("Size") is { } z ? Family(z, z.Value) : null;
            var minVersion = on.Attribute("MinVersion") is { } v ? Value(v, Kind.Int, null, scope)?.Code : null;
            var set = on.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName is not ("Platform" or "Surface" or "Size" or "MinVersion")).ToList();
            if (set.Count == 0)
            {
                Report(Layouts.Diagnostics.DrawnNowhere, on, "This On changes nothing");
                continue;
            }
            var change = Setters("n", on, name, set, scope);
            code = $"{code}{Line(on)}.On({platform ?? "null"}, {place ?? "null"}, {size ?? "null"}, {minVersion ?? "null"}, n => {change})";
        }
        return code;
    }

    void CheckOn(XElement on, string parent)
    {
        foreach (var attribute in on.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            var local = attribute.Name.LocalName;
            if (Vocabulary.Find("On", local) is null && (Vocabulary.Find(parent, local) is null || local is "If" or "IfNot" or "Shared"))
                Report(Layouts.Diagnostics.UnknownAttribute, attribute, $"On changes {parent}'s own attributes, and '{local}' is not one");
        }
    }

    string Setters(string code, XElement element, string name, List<XAttribute> attributes, Scope scope)
    {
        var handled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            var local = attribute.Name.LocalName;
            if (handled.Contains(local))
                continue;
            switch (local)
            {
                case "MaxLines" or "ShrinkToFit" when element.Attribute("MaxLines")?.Value == "1" && element.Attribute("ShrinkToFit") is { } shrink && attributes.Contains(shrink):
                    handled.Add("MaxLines");
                    handled.Add("ShrinkToFit");
                    code = Set(code, shrink, scope, Kind.Number, v => $".SingleLine({v})");
                    continue;
                case "BandLow" or "BandHigh" or "AboveColor" or "BelowColor" or "Shaded":
                {
                    handled.UnionWith(["BandLow", "BandHigh", "AboveColor", "BelowColor", "Shaded"]);
                    var low = element.Attribute("BandLow");
                    var high = element.Attribute("BandHigh");
                    if (low is null || high is null)
                    {
                        Report(Layouts.Diagnostics.UnknownAttribute, attribute, "A band needs BandLow and BandHigh");
                        continue;
                    }
                    var lowValue = Value(low, Kind.Number, null, scope)?.Sure is { } l ? Out(l, Kind.Number, low) : null;
                    var highValue = Value(high, Kind.Number, null, scope)?.Sure is { } h ? Out(h, Kind.Number, high) : null;
                    var above = element.Attribute("AboveColor") is { } a && Color(a, scope)?.Code is { } ac ? Out(ac, Kind.Color, a) : null;
                    var below = element.Attribute("BelowColor") is { } b && Color(b, scope)?.Code is { } bc ? Out(bc, Kind.Color, b) : null;
                    var shaded = element.Attribute("Shaded") is { } s ? Bool(s, scope) : null;
                    if (_tp is not null && Bound(shaded)) _tp.Fail("Shaded is bound", element.Attribute("Shaded"));
                    code += $".Band({lowValue}, {highValue}{(above is null ? "" : ", " + above)}{(below is null ? "" : ", " + below)}{(shaded is null ? "" : ", shaded: " + shaded)})";
                    continue;
                }
                case "Minimum" or "Maximum":
                {
                    handled.UnionWith(["Minimum", "Maximum"]);
                    if (element.Attribute("Minimum") is not { } min || element.Attribute("Maximum") is not { } max)
                    {
                        Report(Layouts.Diagnostics.UnknownAttribute, attribute, "A fixed scale needs Minimum and Maximum");
                        continue;
                    }
                    var minValue = Value(min, Kind.Number, null, scope)?.Sure is { } lo ? Out(lo, Kind.Number, min) : null;
                    var maxValue = Value(max, Kind.Number, null, scope)?.Sure is { } hi ? Out(hi, Kind.Number, max) : null;
                    code += $".Scale({minValue}, {maxValue})";
                    continue;
                }
            }
            code = Setter(code, name, attribute, scope);
        }
        return code;
    }

    string Setter(string code, string element, XAttribute attribute, Scope scope)
    {
        var local = attribute.Name.LocalName;
        var attr = Vocabulary.Find(element, local);
        if (attr is null)
            return code;
        var value = Parse(attribute);

        if (value is Markup { Name: "OnPlatform" or "OnSize" } on)
        {
            var result = on["Default"] is { } fallback ? Setter(code, element, attribute, scope, fallback) : code;
            foreach (var entry in on.Named.Where(n => n.Key != "Default"))
            {
                string? platform = null, size = null;
                if (on.Name == "OnPlatform")
                    platform = EnumValue(attribute, "WidgetPlatform", entry.Key);
                else
                    size = Family(attribute, entry.Key);
                if (platform is null && size is null)
                    continue;
                result += $".On({platform ?? "null"}, null, {size ?? "null"}, null, n => {Setter("n", element, attribute, scope, entry.Value)})";
            }
            return result;
        }
        return Setter(code, element, attribute, scope, value);
    }

    string Setter(string code, string element, XAttribute attribute, Scope scope, Value value)
    {
        var local = attribute.Name.LocalName;
        var attr = Vocabulary.Find(element, local)!;

        // A one-sided OnBool changes the node only on that side.
        if (value is Markup { Name: "OnBool" } onBool && (onBool["True"] is null || onBool["False"] is null))
        {
            var held = onBool["True"] is not null;
            if (BoolCondition(attribute, onBool, scope) is not { } test)
                return code;
            var side = onBool[held ? "True" : "False"]!;
            if (_tp is not null)
            {
                // A color on one side only: the data holds it on that side and nothing on the other.
                var method = local == "Background" ? "Background" : local is "Tint" or "Color" ? "Tint" : local == "TextColor" ? "TextColor" : null;
                if (attr.Kind != Kind.Color || method is null || Color(attribute, side, scope) is not { } color)
                {
                    _tp.Fail($"{local} is set on one side of an OnBool only", attribute);
                    return code;
                }
                return $"{code}.{method}({Out($"({(held ? test : $"!({test})")} ? ({Common}WidgetColor?)({color.Sure}) : null)", Kind.Color, attribute)})";
            }
            return $"{Common}LayoutValues.When({code}, {(held ? test : $"!({test})")}, n => {Setter("n", element, attribute, scope, side)})";
        }

        switch (local)
        {
            case "Expand" or "Fill" or "Decorative" or "Pending" or "Accented" or "Area" or "Dot" or "FullColor":
            {
                var method = local == "Expand" ? "Fill" : local;
                if (value is Literal flag)
                    return Flag(attribute, flag.Text) switch { true => $"{code}.{method}()", _ => code };
                if (Bool(attribute, value, scope) is not { } on)
                    return code;
                if (_tp is not null) _tp.Fail($"{local} is bound", attribute);
                return $"{Common}LayoutValues.When({code}, {on}, n => n.{method}())";
            }
            case "Hidden":
                if (value is Literal hidden)
                    return Flag(attribute, hidden.Text) switch { true => $"{code}.Hidden()", false => $"{code}.Hidden(false)", _ => code };
                if (Bool(attribute, value, scope) is not { } isHidden)
                    return code;
                if (_tp is not null) _tp.Fail("Hidden is bound", attribute);
                return $"{code}.Hidden({isHidden})";
            case "FontSize":
                if (value is Literal size && Vocabulary.Enums["TextRole"].Contains(size.Text))
                    return $"{code}.{size.Text}()";
                return Set(code, attribute, scope, Kind.FontSize, v => $".FontSize({v})", value);
            case "FontAttributes":
                if (value is Literal attributes)
                {
                    if (EnumValue(attribute, "WidgetFontAttributes", attributes.Text) is null)
                        return code;
                    foreach (var flag in attributes.Text.Split(',').Select(f => f.Trim()))
                        if (flag is "Bold" or "Italic")
                            code += $".{flag}()";
                    return code;
                }
                return Set(code, attribute, scope, Kind.Any, v => $".FontAttributes({v})", value);
            case "Spacing":
                if (value is Literal { Text: "Evenly" })
                {
                    if (element != "HStack")
                        Report(Layouts.Diagnostics.BadLiteral, attribute, "Evenly spreads an HStack's children");
                    return element == "HStack" ? $"{code}.Evenly()" : code;
                }
                return Set(code, attribute, scope, Kind.Number, v => $".Spacing({v})", value);
            case "Alignment":
            {
                if (value is Literal alignment)
                {
                    var fits = alignment.Text switch
                    {
                        "Start" or "Center" or "End" => true,
                        "Baseline" => element == "HStack",
                        _ => element == "ZStack",
                    };
                    if (!fits && Vocabulary.Enums["WidgetAlignment"].Contains(alignment.Text))
                    {
                        Report(Layouts.Diagnostics.BadLiteral, attribute, $"{alignment.Text} does not line up the children of a {element}");
                        return code;
                    }
                }
                return Set(code, attribute, scope, Kind.Enum, v => $".Alignment({v})", value, "WidgetAlignment");
            }
            case "Margin" or "Padding":
                return Set(code, attribute, scope, Kind.Thickness, v => $".{local}({v})", value);
        }

        if (local is "Tint" or "Color" && element is "Icon" or "Ring" or "Progress" or "Sparkline")
            return Set(code, attribute, scope, Kind.Color, v => $".Tint({v})", value);
        var method2 = local switch
        {
            "HorizontalOptions" or "Part" or "Description" or "FontDesign" or "TextColor" or "HorizontalTextAlignment" or "MaxLines"
                or "ShrinkToFit" or "CornerRadius" or "Background" or "AltText" or "MaxAspect" => local,
            _ => null,
        };
        if (method2 is null)
        {
            Report(Layouts.Diagnostics.UnknownAttribute, attribute, $"'{local}' cannot be changed in an On; use a Choose");
            return code;
        }
        return Set(code, attribute, scope, attr.Kind, v => $".{method2}({v})", value, attr.Enum);
    }

    /// <summary>Applies one setter; a value that may be absent sets it only when present.</summary>
    string Set(string code, XAttribute attribute, Scope scope, Kind kind, Func<string, string> call, Value? value = null, string? @enum = null)
    {
        var ex = kind switch
        {
            Kind.Color => Color(attribute, value ?? Parse(attribute), scope),
            Kind.Text => Text(attribute, value ?? Parse(attribute), scope, required: false),
            _ => Value(attribute, value ?? Parse(attribute), kind, @enum, scope),
        };
        if (ex is null)
            return code;
        if (_tp is not null && Bound(ex.Code + ex.Present))
        {
            // The data leaves a value out when there is none, and the placeholder's property goes with it: the same as When.
            if (attribute.Name.LocalName is "MaxLines" or "ShrinkToFit" || kind == Kind.FontSize && Plain(ex.Type)?.Name == "TextRole")
            {
                _tp.Fail($"{attribute.Name.LocalName} is bound", attribute);
                return code;
            }
            return code + call(Out(ex.Code, kind, attribute));
        }
        return ex.MaybeNull
            ? $"{Common}LayoutValues.When({code}, {ex.Present}, n => n{call(ex.Sure)})"
            : code + call(ex.Code);
    }

    void CheckAttributes(XElement element, string name)
    {
        foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            if (attribute.Name.NamespaceName.Length > 0)
                continue;
            if (Vocabulary.Find(name, attribute.Name.LocalName) is null)
            {
                var known = Vocabulary.Elements.TryGetValue(name, out var attrs) ? attrs.Select(a => a.Name).Where(n => n is not ("If" or "IfNot")).ToList() : [];
                Report(Layouts.Diagnostics.UnknownAttribute, attribute, $"{name} has no attribute '{attribute.Name.LocalName}'"
                    + (Suggest(attribute.Name.LocalName, known) is { } suggestion ? $"; did you mean {suggestion}?" : ""));
            }
        }
    }

    static string? Suggest(string name, IEnumerable<string> known) =>
        known.Select(k => (Name: k, Distance: Distance(name.ToLowerInvariant(), k.ToLowerInvariant())))
            .Where(k => k.Distance <= Math.Max(1, name.Length / 3))
            .OrderBy(k => k.Distance)
            .Select(k => k.Name)
            .FirstOrDefault();

    /// <summary>Edits between two names, a swap of neighbours counting as one: Nmae is one from Name.</summary>
    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    // ---- Values ----

    Value Parse(XAttribute attribute)
    {
        var value = Layouts.Value.Parse(attribute.Value);
        foreach (var markup in Markups(value))
            if (markup.Error is { } error)
                Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{attribute.Value}': {error}");
        return value;
    }

    static IEnumerable<Markup> Markups(Value value) => value switch
    {
        Markup markup => new[] { markup }.Concat(markup.Positional.Concat(markup.Named.Select(n => n.Value)).SelectMany(Markups)),
        Mixed mixed => mixed.Parts.SelectMany(Markups),
        _ => [],
    };

    Ex? Value(XAttribute attribute, Kind kind, string? @enum, Scope scope) => kind switch
    {
        Kind.Color => Color(attribute, scope),
        Kind.Text => Text(attribute, scope, required: false),
        _ => Value(attribute, Parse(attribute), kind, @enum, scope),
    };

    Ex? Value(XAttribute attribute, Value value, Kind kind, string? @enum, Scope scope)
    {
        switch (value)
        {
            case Literal literal:
                return Literal(attribute, literal.Text, kind, @enum) is { } literalCode ? new Ex(literalCode) : null;
            case Markup { Name: "StaticResource" } resource:
                return Resource(attribute, resource) is { } text ? Value(attribute, Layouts.Value.Parse(text), kind, @enum, scope) : null;
            case Markup { Name: "Static" } member:
                return member.Positional.FirstOrDefault() is Literal { Text.Length: > 0 } m ? new Ex(m.Text) : Bad(attribute, "{Static Type.Member}");
            case Markup { Name: "OnBool" } onBool:
            {
                var test = BoolCondition(attribute, onBool, scope);
                var yes = onBool["True"] is { } t ? Value(attribute, t, kind, @enum, scope) : null;
                var no = onBool["False"] is { } f ? Value(attribute, f, kind, @enum, scope) : null;
                if (test is null || yes is null || no is null) return null;
                return new Ex($"({test} ? {yes.Sure} : {no.Sure})");
            }
            case Markup { Name: "Binding" } binding:
            {
                if (binding.Path is not { } path)
                    return Bad(attribute, "a binding names a property: {Name}");
                if (Resolve(path, scope, attribute) is not { } resolved)
                    return null;
                var code = resolved.Code;
                var sure = resolved.Sure;
                if (binding["Scale"] is Literal scale)
                {
                    if (!double.TryParse(scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor))
                        return Bad(attribute, $"Scale is a number, not '{scale.Text}'");
                    sure = kind == Kind.FontSize
                        ? $"global::System.Math.Round({sure} * {Number(factor)})"
                        : $"{sure} * {Number(factor)}";
                    code = sure;
                    if (resolved.MaybeNull)
                        code = $"({resolved.Present} ? {sure} : null)";
                }
                if (binding["Fallback"] is { } fallback && resolved.MaybeNull)
                {
                    var other = Value(attribute, fallback, kind, @enum, scope);
                    return other is null ? null : new Ex($"({resolved.Present} ? {sure} : {other.Sure})", type: resolved.Type);
                }
                if (kind is Kind.Number or Kind.FontSize && resolved.Type is { } type && KindOf(type) is not (Kind.Number or Kind.Int) && type.TypeKind != TypeKind.Error)
                    Report(Layouts.Diagnostics.WrongType, attribute, $"'{path}' is a {type.ToDisplayString()}, not a number");
                return new Ex(code, resolved.MaybeNull, resolved.Present, sure, resolved.Type);
            }
            case Markup { Name: "Loc" }:
            case Mixed:
                return Text(attribute, value, scope, required: true);
            default:
                return Bad(attribute, $"'{attribute.Value}' cannot be used here");
        }
    }

    Ex? Bad(XAttribute attribute, string message)
    {
        Report(Layouts.Diagnostics.BadLiteral, attribute, message);
        return null;
    }

    string? Literal(XAttribute attribute, string text, Kind kind, string? @enum)
    {
        switch (kind)
        {
            case Kind.Number or Kind.FontSize:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    return Number(number);
                Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{text}' is not a number{(kind == Kind.FontSize ? " or Body, Headline, Title, Caption, LargeTitle" : "")}");
                return null;
            case Kind.Int:
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                    return whole.ToString(CultureInfo.InvariantCulture);
                Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{text}' is not a whole number");
                return null;
            case Kind.Bool:
                return Flag(attribute, text) is { } flag ? (flag ? "true" : "false") : null;
            case Kind.Thickness:
            {
                var parts = text.Split(',').Select(p => p.Trim()).ToArray();
                var numbers = parts.Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : (double?)null).ToArray();
                if (parts.Length is 1 or 2 or 4 && numbers.All(n => n is >= 0))
                    return string.Join(", ", numbers.Select(n => Number(n!.Value)));
                Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{text}' is not a thickness: 16, 16,10 (horizontal, vertical) or 16,10,16,6 (left, top, right, bottom)");
                return null;
            }
            case Kind.Duration:
                return Duration(attribute, text);
            case Kind.Enum when @enum is not null:
                return EnumValue(attribute, @enum, text);
            case Kind.Uri:
                if (Uri.TryCreate(text, UriKind.Absolute, out _))
                    return $"new global::System.Uri({Lit(text)})";
                Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{text}' is not an absolute URL");
                return null;
            case Kind.Text or Kind.Icon or Kind.Name or Kind.Any:
                return Lit(text);
            default:
                Report(Layouts.Diagnostics.BadLiteral, attribute, $"{attribute.Name.LocalName} is bound to the model: {attribute.Name.LocalName}=\"{{…}}\"");
                return null;
        }
    }

    string? Duration(XAttribute attribute, string text)
    {
        var units = new (string Suffix, string Method)[] { ("ms", "FromMilliseconds"), ("s", "FromSeconds"), ("m", "FromMinutes"), ("h", "FromHours"), ("d", "FromDays") };
        foreach (var (suffix, method) in units)
            if (text.EndsWith(suffix, StringComparison.Ordinal)
                && double.TryParse(text.Substring(0, text.Length - suffix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
                return $"global::System.TimeSpan.{method}({Number(amount)})";
        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var span))
            return $"global::System.TimeSpan.FromTicks({span.Ticks}L)";
        Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{text}' is not a duration: 5m, 30s, 1h, 00:05:00");
        return null;
    }

    string? EnumValue(XAttribute attribute, string @enum, string text)
    {
        var names = Vocabulary.Enums[@enum];
        var values = Vocabulary.FlagEnums.Contains(@enum) ? text.Split(',').Select(v => v.Trim()).ToArray() : [text.Trim()];
        foreach (var value in values)
            if (!names.Contains(value))
            {
                Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{value}' is not one of {string.Join(", ", names)}");
                return null;
            }
        var type = @enum == "WidgetSize" ? "WidgetFamily" : @enum;
        return string.Join(" | ", values.Select(v => $"{Common}{type}.{(@enum == "WidgetSize" ? Vocabulary.Families[v] : v)}"));
    }

    string? Enum(XAttribute attribute, string @enum) => EnumValue(attribute, @enum, attribute.Value);

    string? Family(XAttribute attribute, string name)
    {
        if (Vocabulary.Families.TryGetValue(name, out var family))
            return $"{Common}WidgetFamily.{family}";
        Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{name}' is not a size: {string.Join(", ", Vocabulary.Families.Keys)}");
        return null;
    }

    bool? Flag(XAttribute attribute, string text)
    {
        if (string.Equals(text, "True", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(text, "False", StringComparison.OrdinalIgnoreCase)) return false;
        Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{text}' is not True or False");
        return null;
    }

    static bool IsTrue(XAttribute? attribute) => string.Equals(attribute?.Value, "True", StringComparison.OrdinalIgnoreCase);

    string? Bool(XAttribute attribute, Scope scope) => Bool(attribute, Parse(attribute), scope);

    string? Bool(XAttribute attribute, Value value, Scope scope)
    {
        if (value is Literal literal)
            return Flag(attribute, literal.Text) is { } flag ? (flag ? "true" : "false") : null;
        if (Value(attribute, value, Kind.Bool, null, scope) is not { } ex)
            return null;
        return ex.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } ? $"{ex.Code} == true" : ex.Code;
    }

    string? BoolCondition(XAttribute attribute, Markup onBool, Scope scope)
    {
        if (onBool.Positional.FirstOrDefault() is not Literal { Text.Length: > 0 } path)
        {
            Report(Layouts.Diagnostics.BadLiteral, attribute, "{OnBool Property, True=…, False=…}");
            return null;
        }
        return Condition(attribute, path.Text, false, scope)?.Code;
    }

    Ex? Color(XAttribute attribute, Scope scope) => Color(attribute, Parse(attribute), scope);

    Ex? Color(XAttribute attribute, Value value, Scope scope)
    {
        switch (value)
        {
            case Literal literal:
                return ColorLiteral(attribute, literal.Text) is { } colorCode ? new Ex(colorCode) : null;
            case Markup { Name: "AppThemeBinding" } theme:
                if (theme["Light"] is Literal light && theme["Dark"] is Literal dark && light.Text.StartsWith("#", StringComparison.Ordinal) && dark.Text.StartsWith("#", StringComparison.Ordinal))
                    return new Ex($"{Common}WidgetColor.Pair({Lit(light.Text)}, {Lit(dark.Text)})");
                return Bad(attribute, "{AppThemeBinding Light=#1C1C1E, Dark=#FFFFFF}, both hex colors");
            case Markup { Name: "StaticResource" } resource:
                return Resource(attribute, resource) is { } text ? Color(attribute, Layouts.Value.Parse(text), scope) : null;
            case Markup { Name: "Static" } member:
                return member.Positional.FirstOrDefault() is Literal { Text.Length: > 0 } m ? new Ex($"{Common}LayoutValues.Color({m.Text})") : Bad(attribute, "{Static Type.Member}");
            case Markup { Name: "OnBool" } onBool:
            {
                var test = BoolCondition(attribute, onBool, scope);
                var yes = onBool["True"] is { } t ? Color(attribute, t, scope) : null;
                var no = onBool["False"] is { } f ? Color(attribute, f, scope) : null;
                if (test is null || yes is null || no is null)
                    return null;
                return new Ex($"({test} ? {yes.Sure} : {no.Sure})");
            }
            case Markup { Name: "Binding" }:
            {
                if (Value(attribute, value, Kind.Any, null, scope) is not { } ex)
                    return null;
                var plain = Plain(ex.Type);
                var isColor = plain?.Name == "WidgetColor";
                if (plain is not null && !isColor && plain.SpecialType != SpecialType.System_String && plain.TypeKind != TypeKind.Error)
                {
                    Report(Layouts.Diagnostics.WrongType, attribute, $"'{attribute.Value}' is a {plain.ToDisplayString()}, not a WidgetColor or a hex string");
                    return null;
                }
                var sure = isColor ? ex.Sure : $"{Common}LayoutValues.Color({ex.Sure})";
                var code = !ex.MaybeNull ? sure : isColor ? ex.Code : $"({ex.Present} ? {sure} : ({Common}WidgetColor?)null)";
                return new Ex(code, ex.MaybeNull, ex.Present, sure, ex.Type);
            }
            default:
                return Bad(attribute, $"'{attribute.Value}' is not a color");
        }
    }

    string? ColorLiteral(XAttribute attribute, string text)
    {
        if (Vocabulary.Colors.Contains(text))
            return $"{Common}WidgetColor.{text}";
        var slash = text.IndexOf('/');
        if (slash > 0 && IsHex(text.Substring(0, slash)) && IsHex(text.Substring(slash + 1)))
            return $"{Common}WidgetColor.Pair({Lit(text.Substring(0, slash))}, {Lit(text.Substring(slash + 1))})";
        if (IsHex(text))
            return $"{Common}WidgetColor.FromHex({Lit(text)})";
        Report(Layouts.Diagnostics.BadLiteral, attribute, $"'{text}' is not a color: #RRGGBB, #AARRGGBB, #Light/#Dark or one of {string.Join(", ", Vocabulary.Colors)}");
        return null;
    }

    static bool IsHex(string text) =>
        text.Length is 4 or 7 or 9 && text[0] == '#' && text.Skip(1).All(c => Uri.IsHexDigit(c));

    string? Resource(XAttribute attribute, Markup markup)
    {
        if (markup.Positional.FirstOrDefault() is Literal { Text.Length: > 0 } name && _values.TryGetValue(name.Text, out var resource))
            return resource.Value;
        Report(Layouts.Diagnostics.UnknownPath, attribute, $"There is no resource named '{(markup.Positional.FirstOrDefault() as Literal)?.Text}' in the project's *.resources.xml");
        return null;
    }

    /// <summary>Text: a literal, a binding (formatted, with a fallback or prefix), {Loc}, or all of them mixed.</summary>
    Ex? Text(XAttribute attribute, Scope scope, bool required) => Text(attribute, Parse(attribute), scope, required);

    Ex? Text(XAttribute attribute, Value value, Scope scope, bool required)
    {
        switch (value)
        {
            case Literal literal:
                return new Ex(Lit(literal.Text));
            case Mixed mixed:
            {
                var builder = new StringBuilder("$\"");
                foreach (var part in mixed.Parts)
                {
                    if (part is Literal text)
                        builder.Append(Escape(text.Text).Replace("{", "{{").Replace("}", "}}"));
                    else if (Segment(attribute, part, scope) is { } segment)
                        builder.Append(segment);
                    else
                        return null;
                }
                builder.Append('"');
                return new Ex(builder.ToString());
            }
            case Markup { Name: "Loc" } loc:
                return Loc(attribute, loc, scope) is { } call ? new Ex(call) : null;
            case Markup { Name: "StaticResource" } resource:
                return Resource(attribute, resource) is { } resourceText ? Text(attribute, Layouts.Value.Parse(resourceText), scope, required) : null;
            case Markup { Name: "Static" } member:
                return member.Positional.FirstOrDefault() is Literal { Text.Length: > 0 } m ? new Ex($"$\"{{{m.Text}}}\"") : Bad(attribute, "{Static Type.Member}");
            case Markup { Name: "OnBool" } onBool:
            {
                var test = BoolCondition(attribute, onBool, scope);
                var yes = onBool["True"] is { } t ? Text(attribute, t, scope, required) : new Ex("\"\"");
                var no = onBool["False"] is { } f ? Text(attribute, f, scope, required) : new Ex("\"\"");
                return test is null || yes is null || no is null ? null : new Ex($"({test} ? {yes.Code} : {no.Code})");
            }
            case Markup { Name: "Binding" } binding:
            {
                if (binding.Path is not { } path)
                    return Bad(attribute, "a binding names a property: {Name}");
                if (Resolve(path, scope, attribute) is not { } resolved)
                    return null;
                var isString = Plain(resolved.Type)?.SpecialType == SpecialType.System_String || resolved.Type is null;
                var format = binding["Format"] is Literal f ? f.Text : null;
                var sure = isString && format is null ? resolved.Sure : $"$\"{{{resolved.Sure}{(format is null ? "" : ":" + format)}}}\"";
                if (binding["Prefix"] is { } prefixValue && Text(attribute, prefixValue, scope, false) is { } prefix)
                    sure = $"{prefix.Code} + {sure}";
                if (!resolved.MaybeNull)
                    return new Ex(sure, type: resolved.Type);
                if (binding["Fallback"] is { } fallbackValue)
                    return Text(attribute, fallbackValue, scope, required) is not { } fallback ? null
                        : isString && format is null && binding["Prefix"] is null ? new Ex($"({resolved.Code} ?? {fallback.Code})", type: resolved.Type)
                        : new Ex($"({resolved.Present} ? {sure} : {fallback.Code})", type: resolved.Type);
                if (binding["Prefix"] is not null)
                    return new Ex($"({resolved.Present} ? {sure} : \"\")", type: resolved.Type);
                if (required)
                {
                    Report(Layouts.Diagnostics.NullableWithoutFallback, attribute,
                        $"'{path}' may be absent: give it a Fallback, guard the element with If=\"{{{path}}}\", or bind a value that is always there");
                    return new Ex($"({resolved.Code} ?? \"\")", type: resolved.Type);
                }
                return new Ex(isString && format is null ? resolved.Code : $"({resolved.Present} ? {sure} : null)", true, resolved.Present, sure, resolved.Type);
            }
            default:
                return Bad(attribute, $"'{attribute.Value}' is not text");
        }
    }

    /// <summary>One binding inside mixed text, where an absent value writes nothing.</summary>
    string? Segment(XAttribute attribute, Value part, Scope scope)
    {
        var text = Text(attribute, part, scope, required: false);
        if (text is null)
            return null;
        return $"{{({text.Code})}}";
    }

    string? Loc(XAttribute attribute, Markup loc, Scope scope)
    {
        if (loc.Positional.FirstOrDefault() is not Literal { Text.Length: > 0 } key)
        {
            Report(Layouts.Diagnostics.BadLiteral, attribute, "{Loc Key} or {Loc Key, Argument, …}");
            return null;
        }
        _usesStrings = true;
        var arguments = new List<string>();
        foreach (var argument in loc.Positional.Skip(1))
        {
            if (argument is Literal { Text.Length: > 0 } path && Resolve(path.Text, scope, attribute) is { } resolved)
                arguments.Add(resolved.Code);
            else if (argument is Markup && Text(attribute, argument, scope, false) is { } nested)
                arguments.Add(nested.Code);
            else
                return null;
        }
        if (loc["Count"] is { } countValue)
        {
            if (countValue is not Literal { Text.Length: > 0 } countPath || Resolve(countPath.Text, scope, attribute) is not { } count)
                return null;
            CheckString(attribute, key.Text + ".other", 1, plural: true);
            return $"strings.Get({Lit(key.Text)}, (int){count.Sure})";
        }
        CheckString(attribute, key.Text, arguments.Count, plural: false);
        return arguments.Count == 0 ? $"strings[{Lit(key.Text)}]" : $"strings.Get({Lit(key.Text)}, {string.Join(", ", arguments)})";
    }

    void CheckString(XAttribute attribute, string key, int arguments, bool plural)
    {
        if (_strings.Cultures.Count == 0)
            return;
        var missing = _strings.Cultures.Where(c => !c.Value.ContainsKey(key)).Select(c => c.Key.Length == 0 ? "the neutral strings.xml" : $"strings.{c.Key}.xml").ToList();
        if (missing.Count > 0)
            Report(Layouts.Diagnostics.MissingString, attribute, $"'{key}' is missing from {string.Join(", ", missing)}");
        foreach (var culture in _strings.Cultures)
            if (culture.Value.TryGetValue(key, out var text) && StringsTable.Arguments(text) is var expected && expected != arguments && !(plural && expected <= 1))
                Report(Layouts.Diagnostics.StringArguments, attribute,
                    $"'{key}' in {(culture.Key.Length == 0 ? "strings.xml" : $"strings.{culture.Key}.xml")} takes {expected} argument{(expected == 1 ? "" : "s")}, and {arguments} {(arguments == 1 ? "is" : "are")} given");
    }

    // ---- Conditions and paths ----

    Test? Condition(XAttribute attribute, bool negate, Scope scope)
    {
        var value = Parse(attribute);
        if (value is not Markup { Path: { } path })
        {
            Report(Layouts.Diagnostics.WrongType, attribute, $"{attribute.Name.LocalName} is bound to the model: {attribute.Name.LocalName}=\"{{IsLive}}\"");
            return null;
        }
        return Condition(attribute, path, negate, scope);
    }

    Test? Condition(XAttribute attribute, string path, bool negate, Scope scope)
    {
        if (Resolve(path, scope, attribute) is not { } resolved)
            return null;
        var key = (negate ? "!" : "") + resolved.Canonical;
        var plain = Plain(resolved.Type);
        if (resolved.Type is null || plain?.TypeKind == TypeKind.Error)
            return new Test(negate ? $"!({resolved.Code})" : resolved.Code, key, null, false);
        if (plain!.SpecialType == SpecialType.System_Boolean)
            return resolved.MaybeNull
                ? new Test(negate ? $"{resolved.Code} != true" : $"{resolved.Code} == true", key, null, false)
                : new Test(negate ? $"!{resolved.Code}" : resolved.Code, key, null, false);
        if (resolved.MaybeNull)
            return new Test(negate ? $"{resolved.Code} is null" : $"{resolved.Code} is not null", key, resolved.Canonical, !negate);
        if (plain.SpecialType == SpecialType.System_String)
            return new Test(negate ? $"string.IsNullOrEmpty({resolved.Code})" : $"!string.IsNullOrEmpty({resolved.Code})", key, null, false);
        Report(Layouts.Diagnostics.WrongType, attribute, $"'{path}' is a {resolved.Type.ToDisplayString()}: {attribute.Name.LocalName} takes a bool, or a value that may be absent");
        return null;
    }

    string? GuardKey(XAttribute attribute, bool negate)
    {
        if (Layouts.Value.Parse(attribute.Value) is not Markup { Path: { } path })
            return null;
        var resolved = Resolve(path, new Scope(), attribute, report: false);
        return resolved is null ? null : (negate ? "!" : "") + resolved.Canonical;
    }

    sealed class Resolved(string code, string canonical, ITypeSymbol? type, bool maybeNull, bool nullableValue)
    {
        public string Code { get; } = code;
        public string Canonical { get; } = canonical;
        public ITypeSymbol? Type { get; } = type;
        public bool MaybeNull { get; } = maybeNull;
        public string Present => $"{Code} is not null";
        public string Sure => !MaybeNull ? Code : nullableValue ? $"({Code}).Value" : $"{Code}!";
    }

    /// <summary>
    /// A path in the model (<c>Person.Name</c>), a loop's item (<c>.</c>, its members) or a template's parameter;
    /// <c>Model.X</c> is the model inside a loop. A step through a value that may be absent uses <c>?.</c>, unless an
    /// <c>If</c> around it already made sure it is there.
    /// </summary>
    Resolved? Resolve(string path, Scope scope, XAttribute attribute, bool report = true)
    {
        var segments = path == "." ? [] : path.Split('.');
        string code;
        ITypeSymbol? type;
        bool maybeNull;
        var start = 1;

        if (path == ".")
        {
            if (scope.Item is { } loopItem)
                (code, type, maybeNull) = (loopItem.Code, loopItem.Type, loopItem.MaybeNull);
            else
                (code, type, maybeNull) = ("m", _model, false);
        }
        else if (segments[0] == "Model" && segments.Length > 1 && _model is not null)
            (code, type, maybeNull) = ("m", _model, false);
        else if (scope.Item is { } item && (item.Type is null || Member(Plain(item.Type)!, segments[0]) is not null))
            (code, type, maybeNull, start) = (item.Code, item.Type, item.MaybeNull, 0);
        else if (scope.Vars.TryGetValue(segments[0], out var variable))
            (code, type, maybeNull) = (variable.Code, variable.Type, variable.MaybeNull);
        else if (_model is not null)
            (code, type, maybeNull, start) = ("m", _model, false, 0);
        else
        {
            if (report)
                Report(Layouts.Diagnostics.UnknownPath, attribute, $"'{path}' is bound, but the layout has no Model");
            return null;
        }

        var canonical = code;
        var lifted = false;
        for (var i = start; i < segments.Length; i++)
        {
            var name = segments[i];
            ITypeSymbol? next = null;
            var lookup = Plain(type);
            if (lookup is not null && lookup.TypeKind != TypeKind.Error)
            {
                next = Member(lookup, name);
                if (next is null)
                {
                    if (report)
                        Report(Layouts.Diagnostics.UnknownPath, attribute,
                            $"'{lookup.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' has no property '{name}'{(Suggest(name, Members(lookup)) is { } s ? $"; did you mean {s}?" : "")}");
                    return null;
                }
            }
            // The model's own objects and lists that announce changes: a binding follows them too.
            if (report && canonical == "m" && next is not null && Announces(next))
                _sources.Add(name);
            var known = scope.Present.Contains(canonical);
            if (maybeNull && !known)
            {
                code += "?." + name;
                lifted = true;
            }
            else if (maybeNull)
                code += (IsNullableValue(type) ? ".Value." : "!.") + name;
            else
                code += "." + name;
            canonical += "." + name;
            maybeNull = (maybeNull && !known) || (next is not null && MaybeNull(next));
            type = next;
        }

        var nullableValue = IsNullableValue(type) || (lifted && type is { IsValueType: true });
        if (maybeNull && scope.Present.Contains(canonical))
            return new Resolved(nullableValue ? $"({code}).Value" : $"{code}!", canonical, Plain(type), false, false);
        return new Resolved(code, canonical, type, maybeNull, nullableValue);
    }

    static bool Announces(ITypeSymbol type)
    {
        var plain = Plain(type)!;
        return new[] { plain }.Concat(plain.AllInterfaces).Any(t => t.ToDisplayString() is "System.ComponentModel.INotifyPropertyChanged" or "System.Collections.Specialized.INotifyCollectionChanged");
    }

    static bool IsNullableValue(ITypeSymbol? type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    static ITypeSymbol? Plain(ITypeSymbol? type, bool unwrap = true) =>
        unwrap && type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;

    static bool MaybeNull(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated
        || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    static ITypeSymbol? Member(ITypeSymbol type, string name)
    {
        foreach (var t in Hierarchy(type))
            foreach (var member in t.GetMembers(name))
                switch (member)
                {
                    case IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsIndexer: false } property:
                        return property.Type;
                    case IFieldSymbol { DeclaredAccessibility: Accessibility.Public } field:
                        return field.Type;
                }
        return null;
    }

    static IEnumerable<string> Members(ITypeSymbol type) =>
        Hierarchy(type).SelectMany(t => t.GetMembers()).Where(m => m is IPropertySymbol or IFieldSymbol && m.DeclaredAccessibility == Accessibility.Public && m.CanBeReferencedByName).Select(m => m.Name);

    static IEnumerable<ITypeSymbol> Hierarchy(ITypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
            yield return t;
        foreach (var i in type.AllInterfaces)
            yield return i;
    }

    static ITypeSymbol? ElementType(ITypeSymbol? type)
    {
        type = Plain(type);
        if (type is null) return null;
        if (type is IArrayTypeSymbol array) return array.ElementType;
        if (type.SpecialType == SpecialType.System_String) return null;
        var enumerable = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T } self
            ? self
            : type.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        return enumerable?.TypeArguments[0];
    }

    // ---- Names and code ----

    (string Namespace, string Class) ClassName(XElement root)
    {
        if (root.Attribute("Class") is { Value.Length: > 0 } explicitName)
        {
            var dot = explicitName.Value.LastIndexOf('.');
            return dot < 0 ? ("", explicitName.Value) : (explicitName.Value.Substring(0, dot), explicitName.Value.Substring(dot + 1));
        }
        var file = System.IO.Path.GetFileName(_file.Path);
        var stem = file.Substring(0, file.Length - ".layout.xml".Length);
        var ns = _model?.ContainingNamespace is { IsGlobalNamespace: false } modelNamespace ? modelNamespace.ToDisplayString() : _rootNamespace ?? "";
        return (ns, Identifier(stem) + "Layout");
    }

    static string Identifier(string text)
    {
        var builder = new StringBuilder();
        var upper = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
                upper = true;
        }
        if (builder.Length == 0 || char.IsDigit(builder[0]))
            builder.Insert(0, '_');
        return builder.ToString();
    }

    static string Pascal(string id) => Identifier(id);

    static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '<' or '(' or '[') depth++;
            else if (c is '>' or ')' or ']') depth--;
            else if (c == separator && depth == 0)
            {
                if (text.Substring(start, i - start).Trim().Length > 0)
                    yield return text.Substring(start, i - start);
                start = i + 1;
            }
        }
        if (text.Substring(start).Trim().Length > 0)
            yield return text.Substring(start);
    }

    string Line(XObject node) =>
        node is IXmlLineInfo info && info.HasLineInfo()
            ? $"\n#line {info.LineNumber} \"{(node.Document?.Annotation<SourceFile>() ?? _file).Path.Replace("\\", "\\\\")}\"\n"
            : "";

    static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    static string Lit(string text) => "\"" + Escape(text) + "\"";

    static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    void Report(DiagnosticDescriptor descriptor, XObject? node, string message, SourceFile? file = null)
    {
        if (_quiet)
            return;
        var length = node switch
        {
            XAttribute attribute => attribute.Name.LocalName.Length,
            XElement element => element.Name.LocalName.Length,
            _ => 0,
        };
        var source = file ?? node?.Document?.Annotation<SourceFile>() ?? _file;
        Diagnostics.Add(Diagnostic.Create(descriptor, source.Location(node, length), message));
    }
}
