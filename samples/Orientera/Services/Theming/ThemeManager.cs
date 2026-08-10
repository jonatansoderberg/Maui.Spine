using Orientera.Resources.Styles;

namespace Orientera.Services.Theming;

/// <summary>
/// Projects <see cref="LightTheme"/> / <see cref="DarkTheme"/> into a single token dictionary
/// merged into the application resources, and re-projects it when the system theme changes.
/// Both dictionaries declare the same key set, so every consumer resolves tokens through
/// <c>{DynamicResource}</c> and re-resolves on the swap.
/// </summary>
public static class ThemeManager
{
    private static readonly ResourceDictionary Tokens = [];

    // Application.RequestedThemeChanged is a weak event: a lambda closure kept alive only by
    // the event manager is collected and the handler silently stops running. Root it here.
    private static EventHandler<AppThemeChangedEventArgs>? _handler;

    public static void Attach(Application app)
    {
        app.Resources.MergedDictionaries.Add(Tokens);
        Apply(app);

        _handler = (_, _) => Apply(app);
        app.RequestedThemeChanged += _handler;
    }

    private static void Apply(Application app)
    {
        ResourceDictionary source = app.RequestedTheme == AppTheme.Dark
            ? new DarkTheme()
            : new LightTheme();

        foreach (var (key, value) in source)
            Tokens[key] = value;
    }
}
