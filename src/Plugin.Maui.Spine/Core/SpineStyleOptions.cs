namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Base for the style options of Spine's code-drawn controls (the shimmer, calendar and data grid).
/// A control asks <see cref="Resolve"/> for its effective options every time it renders.
/// </summary>
/// <remarks>
/// The chain is:
/// <list type="number">
/// <item>the options set on the control instance,</item>
/// <item>an application resource keyed <c>"Default" + type name</c>, e.g. <c>DefaultShimmerStyleOptions</c>,</item>
/// <item>the code defaults.</item>
/// </list>
/// Colours are nullable and <see langword="null"/> means "not set": an unset colour on the
/// instance is taken from the resource, and one unset there from <see cref="ApplyThemeDefaults"/>
/// for the theme in effect. Overriding one padding on an instance therefore keeps the control
/// themed. The effective object is a copy cached against <see cref="SpineTheme.Version"/>, so the
/// objects the app declared are never changed and a cached lookup is a single comparison.
/// </remarks>
public abstract class SpineStyleOptions<TSelf>
    where TSelf : SpineStyleOptions<TSelf>, new()
{
    private static readonly string ResourceKey = "Default" + typeof(TSelf).Name;
    private static TSelf? _codeDefaults;

    private TSelf? _effective;
    private int _effectiveVersion = -1;
    private AppTheme _effectiveTheme;

    /// <summary>Fills every colour still <see langword="null"/> from <paramref name="source"/>.</summary>
    protected abstract void InheritColorsFrom(TSelf source);

    /// <summary>Fills every colour still <see langword="null"/> with the code default for <paramref name="theme"/>.</summary>
    protected abstract void ApplyThemeDefaults(AppTheme theme);

    /// <summary>The effective options for a control whose own options are <paramref name="instance"/>.</summary>
    public static TSelf Resolve(TSelf? instance)
    {
        var fromResources = Application.Current?.Resources.TryGetValue(ResourceKey, out var resource) == true
            ? resource as TSelf
            : null;

        var source = instance ?? fromResources ?? (_codeDefaults ??= new TSelf());
        var theme = Application.Current?.RequestedTheme == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;

        if (source._effective is { } cached && source._effectiveVersion == SpineTheme.Version && source._effectiveTheme == theme)
            return cached;

        var effective = (TSelf)source.MemberwiseClone();
        effective._effective = null;

        if (instance is not null && fromResources is not null && !ReferenceEquals(instance, fromResources))
            effective.InheritColorsFrom(fromResources);

        effective.ApplyThemeDefaults(theme);

        source._effective = effective;
        source._effectiveVersion = SpineTheme.Version;
        source._effectiveTheme = theme;
        return effective;
    }
}
