namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Declares an <see cref="IWidgetProvider"/> as the source of the widget with the given kind. The
/// same kind must be declared to the build with a <c>&lt;SpineWidget Include="kind" /&gt;</c> item,
/// which is what generates the native extension; Spine validates the pairing at startup.
/// </summary>
/// <param name="kind">Stable identifier of the widget, e.g. <c>next-start</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WidgetAttribute(string kind) : Attribute
{
    /// <summary>Stable identifier of the widget; also the last segment of its open URL.</summary>
    public string Kind { get; } = kind;
}
