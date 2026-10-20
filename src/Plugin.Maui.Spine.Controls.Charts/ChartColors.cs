namespace Plugin.Maui.Spine.Controls;

/// <summary>Colours the charts share.</summary>
public static class ChartColors
{
    /// <summary>
    /// The track under a value: the value's colour mixed towards the background, so it reads as the same series on a
    /// light and a dark card. Mixed towards black in a dark theme, towards white in a light one.
    /// </summary>
    public static Color Track(Color color) => Application.Current?.RequestedTheme == AppTheme.Light
        ? new(0.8f + color.Red * 0.2f, 0.8f + color.Green * 0.2f, 0.8f + color.Blue * 0.2f)
        : new(color.Red * 0.2f, color.Green * 0.2f, color.Blue * 0.2f);
}

/// <summary>The base of the charts: a GraphicsView that draws itself and redraws when the theme changes.</summary>
public abstract class ChartView : GraphicsView, IDrawable
{
    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(ChartView), Colors.DodgerBlue, propertyChanged: Redraw);

    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(ChartView), null, propertyChanged: Redraw);

    /// <summary>The value's colour.</summary>
    public Color Color { get => (Color)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    /// <summary>The track's colour, or null for <see cref="ChartColors.Track"/> of <see cref="Color"/>.</summary>
    public Color? TrackColor { get => (Color?)GetValue(TrackColorProperty); set => SetValue(TrackColorProperty, value); }

    protected ChartView()
    {
        Drawable = this;
        // The track is drawn in code, so it does not follow AppThemeBinding on its own.
        Loaded += (_, _) => { if (Application.Current is { } app) app.RequestedThemeChanged += OnThemeChanged; };
        Unloaded += (_, _) => { if (Application.Current is { } app) app.RequestedThemeChanged -= OnThemeChanged; };
    }

    void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Invalidate();

    protected Color Track => TrackColor ?? ChartColors.Track(Color);

    protected static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((ChartView)bindable).Invalidate();

    public abstract void Draw(ICanvas canvas, RectF dirtyRect);
}
