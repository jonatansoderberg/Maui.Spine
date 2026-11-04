using Microsoft.CodeAnalysis;

namespace Plugin.Maui.Spine.Layouts;

/// <summary>The layout files' diagnostics, each reported at its file, line and column.</summary>
internal static class Diagnostics
{
    const string Category = "Spine.Layouts";
    const string Help = "https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widget-layout-files.md#diagnostics";

    static DiagnosticDescriptor Error(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true, helpLinkUri: Help);

    static DiagnosticDescriptor Warning(string id, string title, string message) =>
        new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, helpLinkUri: Help);

    public static readonly DiagnosticDescriptor UnknownElement = Error("SPL001", "Unknown element", "{0}");
    public static readonly DiagnosticDescriptor UnknownAttribute = Error("SPL002", "Unknown attribute", "{0}");
    public static readonly DiagnosticDescriptor UnknownPath = Error("SPL003", "Unknown binding path", "{0}");
    public static readonly DiagnosticDescriptor WrongType = Error("SPL004", "Wrong type", "{0}");
    public static readonly DiagnosticDescriptor BadLiteral = Error("SPL005", "Bad value", "{0}");
    public static readonly DiagnosticDescriptor NullableWithoutFallback = Warning("SPL006", "A value that may be absent, without a fallback", "{0}");
    public static readonly DiagnosticDescriptor DuplicatePart = Warning("SPL007", "Duplicate part", "{0}");
    public static readonly DiagnosticDescriptor DrawnNowhere = Warning("SPL008", "Drawn nowhere", "{0}");
    public static readonly DiagnosticDescriptor MissingString = Error("SPL009", "Missing string", "{0}");
    public static readonly DiagnosticDescriptor StringArguments = Warning("SPL010", "String arguments", "{0}");
    public static readonly DiagnosticDescriptor UnguardedUse = Warning("SPL011", "A shared node that may be absent", "{0}");
    public static readonly DiagnosticDescriptor UnknownIcon = Error("SPL012", "Unknown icon", "{0}");
    public static readonly DiagnosticDescriptor Silent = Warning("SPL013", "Silent for screen readers", "{0}");
    public static readonly DiagnosticDescriptor DuplicateResource = Error("SPL014", "Duplicate resource", "{0}");
    public static readonly DiagnosticDescriptor BadXml = Error("SPL015", "Not well-formed XML", "{0}");
}
