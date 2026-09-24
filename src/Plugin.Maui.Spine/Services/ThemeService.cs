using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Services;

internal sealed class ThemeService(SpineOptions options) : IThemeService
{
    private const string PreferenceKey = "Spine.Theme";
    private const string AccentPreferenceKey = "Spine.Accent";

    private readonly ResourceDictionary _tokens = [];
    private Application? _app;
    private SpineAccent? _accent;

    // The accent the app's own resources declare, read before the token dictionary shadows them,
    // so setting the accent back to null restores it.
    private SpineAccent? _ownAccent;
    private bool _merged;

    // Application.RequestedThemeChanged is a weak event: a handler held only by the event manager
    // is collected and silently stops running. The field roots it for the service's lifetime.
    private EventHandler<AppThemeChangedEventArgs>? _handler;

    /// <summary>The instance behind <see cref="SpineTheme"/>, set when the application initializes it.</summary>
    internal static ThemeService? Instance { get; private set; }

    internal SpineThemeOptions Options => options.Theme;

    public AppTheme Current
    {
        get => _app?.UserAppTheme ?? AppTheme.Unspecified;
        set
        {
            if (options.Theme.Persist)
                Preferences.Default.Set(PreferenceKey, (int)value);

            if (_app is not null)
                _app.UserAppTheme = value;
        }
    }

    public AppTheme Effective => _app?.RequestedTheme == AppTheme.Dark ? AppTheme.Dark : AppTheme.Light;

    public SpineAccent? Accent
    {
        get => _accent;
        set
        {
            if (options.Theme.Persist)
            {
                if (value is null)
                    Preferences.Default.Remove(AccentPreferenceKey);
                else
                    Preferences.Default.Set(AccentPreferenceKey, value.Serialize());
            }

            if (Equals(_accent, value))
                return;

            _accent = value;

            if (_merged)
                Announce();
        }
    }

    public int Version => ThemeTracker.Version;

    public event EventHandler? Changed;

    public void Track(VisualElement view, Action onChanged) => ThemeTracker.Track(view, onChanged);

    /// <summary>
    /// Applies the stored choice and starts following the theme. Called from the application's
    /// constructor, before the platform application handler connects, so Android's night mode is
    /// set before the activity inflates its window.
    /// </summary>
    internal void Initialize(Application app)
    {
        _app = app;
        Instance = this;

        if (options.Theme.Persist
            && Preferences.Default.Get(PreferenceKey, (int)AppTheme.Unspecified) is var stored
            && stored != (int)AppTheme.Unspecified)
        {
            app.UserAppTheme = (AppTheme)stored;
        }

        if (options.Theme.Persist)
            _accent = SpineAccent.Deserialize(Preferences.Default.Get<string?>(AccentPreferenceKey, null));

        CopyTokens();

        _handler = (_, _) => Announce();
        app.RequestedThemeChanged += _handler;
    }

    /// <summary>
    /// Merges the token dictionary and resets the repaint registry for a new window. Called from
    /// <c>CreateWindow</c>, after the app's <c>InitializeComponent</c> has installed its resources.
    /// </summary>
    internal void Attach()
    {
        if (_app is not null && !_merged)
        {
            _ownAccent = ReadOwnAccent(_app.Resources);
            _app.Resources.MergedDictionaries.Add(_tokens);
            _merged = true;
        }

        ApplyAccent();
        ThemeTracker.Reset();
    }

    private void Announce()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(Announce);
            return;
        }

        ThemeTracker.BeginChange();
        CopyTokens();
        ApplyAccent();
        Changed?.Invoke(this, EventArgs.Empty);
        ThemeTracker.Notify();
    }

    private void CopyTokens()
    {
        var source = Effective == AppTheme.Dark ? options.Theme.DarkTokens : options.Theme.LightTokens;
        if (source is null)
            return;

        foreach (var (key, value) in source())
            _tokens[key] = value;
    }

    private void ApplyAccent()
    {
        if ((_accent ?? _ownAccent) is not { } accent)
            return;

        var theme = options.Theme;
        var effective = accent.For(Effective);

        Write(theme.AccentLightKey, accent.Light);
        Write(theme.AccentDarkKey, accent.Dark);
        Write(theme.AccentKey, effective);
        Write(theme.OnAccentKey, SpineAccent.TextOn(effective));
    }

    private void Write(string? key, Color color)
    {
        // Skipping an unchanged value spares every DynamicResource on the key a re-apply.
        if (key is not null && !(_tokens.TryGetValue(key, out var current) && Equals(current, color)))
            _tokens[key] = color;
    }

    private SpineAccent? ReadOwnAccent(ResourceDictionary resources)
    {
        if (ReadColor(resources, options.Theme.AccentLightKey) is not { } light)
            return null;

        return new SpineAccent(light, ReadColor(resources, options.Theme.AccentDarkKey) ?? light);
    }

    internal static Color? ReadColor(ResourceDictionary? resources, string? key) =>
        key is not null && resources is not null && resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : null;
}
