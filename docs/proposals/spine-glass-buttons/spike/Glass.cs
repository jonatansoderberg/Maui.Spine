namespace GlassSpike;

public enum GlassStyle { None, Regular, Prominent, Clear, ProminentClear }

public static partial class Glass
{
	public const string MapperKey = "SpineGlass";

	public static readonly BindableProperty StyleProperty = BindableProperty.CreateAttached(
		"Style", typeof(GlassStyle), typeof(Glass), GlassStyle.None,
		propertyChanged: static (b, _, _) => (b as VisualElement)?.Handler?.UpdateValue(MapperKey));

	public static GlassStyle GetStyle(BindableObject b) => (GlassStyle)b.GetValue(StyleProperty);
	public static void SetStyle(BindableObject b, GlassStyle v) => b.SetValue(StyleProperty, v);

	public static void Register() => RegisterPlatform();

	static partial void RegisterPlatform();
}
