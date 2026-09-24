using System.Globalization;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

/// <summary>
/// Wires <see cref="SpineStrings.Current"/> to the app: its providers, the stored culture, and
/// the repaint signal shared with the theme.
/// </summary>
internal static class StringsSetup
{
    private const string PreferenceKey = "Spine.Culture";

    /// <summary>Registers the configured and embedded providers. Called from <c>UseSpine</c>.</summary>
    public static void AddProviders(SpineOptions options)
    {
        var strings = SpineStrings.Current;

        foreach (var provider in options.Strings.Providers)
            strings.AddProvider(provider);

        foreach (var assembly in options.Assemblies)
            strings.AddProvider(new EmbeddedXmlStringProvider(assembly));

        strings.AddDefaults(new EmbeddedXmlStringProvider(typeof(SpineOptions).Assembly));
    }

    /// <summary>
    /// Applies the stored culture and follows the store from then on. Called from the
    /// application's constructor, before the first page is built.
    /// </summary>
    public static void Initialize(SpineOptions options)
    {
        var strings = SpineStrings.Current;

        if (options.Strings.Persist && Preferences.Default.Get(PreferenceKey, "") is { Length: > 0 } stored)
            strings.Culture = CultureInfo.GetCultureInfo(stored);

        strings.Changed += (_, _) =>
        {
            if (options.Strings.Persist)
                Preferences.Default.Set(PreferenceKey, strings.Culture.Name);

            // Text lives in the same code-built views as colours do, so a culture switch repaints
            // whatever the theme would.
            if (MainThread.IsMainThread)
                Repaint();
            else
                MainThread.BeginInvokeOnMainThread(Repaint);
        };
    }

    private static void Repaint()
    {
        ThemeTracker.BeginChange();
        ThemeTracker.Notify();
    }
}
