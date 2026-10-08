namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Declares an <see cref="IControlProvider"/> as the source of the control with the given kind — a
/// Control Center control on iOS 18 and a Quick Settings tile on Android. The same kind must be
/// declared to the build with a <c>&lt;SpineControl Include="kind" Type="Toggle" /&gt;</c> item, which is
/// what puts the native control in the app.
/// </summary>
/// <param name="kind">Stable identifier of the control, e.g. <c>goal-alerts</c>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ControlAttribute(string kind) : Attribute
{
    /// <summary>Stable identifier of the control.</summary>
    public string Kind { get; } = kind;
}
