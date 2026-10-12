using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>What the user did with a tour step.</summary>
internal enum SpotlightAnswer { Next, Skip, Cancelled }

/// <summary>
/// The window-wide view a tour shows through <see cref="SpineOverlay"/>: a dim with a lit hole round the
/// target, and a card with the tip and the Skip and Next buttons. A tap anywhere but Skip goes on.
/// </summary>
internal sealed partial class SpotlightView : Grid
{
    const uint MoveMilliseconds = 300;
    const double CardMaxWidth = 340;

    readonly DimDrawable _drawable = new();
    readonly GraphicsView _dim;
    readonly Border _card;
    readonly Label _title;
    readonly Label _tip;
    readonly Label _count;
    readonly Button _skip;
    readonly Button _next;
    TaskCompletionSource<SpotlightAnswer>? _answer;
    (double X, double Y, double Width, double Height)? _hole;
    double _radius;

    public SpotlightView()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var accent = SpineTheme.GetAccent(dark ? AppTheme.Dark : AppTheme.Light) ?? Color.FromArgb(dark ? "#7D9BFF" : "#2F5BEA");
        var text = dark ? Colors.White : Color.FromArgb("#171A23");

        _dim = new GraphicsView { Drawable = _drawable, BackgroundColor = Colors.Transparent };
        AutomationProperties.SetIsInAccessibleTree(_dim, false);
        _dim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Answer(SpotlightAnswer.Next)) });

        _title = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = text, IsVisible = false };
        _tip = new Label { FontSize = 15, TextColor = text, LineBreakMode = LineBreakMode.WordWrap };
        _count = new Label { FontSize = 13, TextColor = text.WithAlpha(0.6f), VerticalOptions = LayoutOptions.Center };
        _skip = new Button
        {
            Text = HighlightStrings.Get("Spine.Highlight.Skip"),
            BackgroundColor = Colors.Transparent,
            TextColor = accent,
            BorderWidth = 0,
            Padding = new Thickness(12, 6),
            Command = new Command(() => Answer(SpotlightAnswer.Skip)),
        };
        _next = new Button
        {
            BackgroundColor = accent,
            TextColor = SpineAccent.TextOn(accent),
            CornerRadius = 18,
            Padding = new Thickness(16, 6),
            Command = new Command(() => Answer(SpotlightAnswer.Next)),
        };

        var buttons = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)],
            ColumnSpacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        buttons.Add(_count, 0);
        buttons.Add(_skip, 1);
        buttons.Add(_next, 2);

        _card = new Border
        {
            BackgroundColor = dark ? Color.FromArgb("#2C2C2E") : Colors.White,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(16, 14),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Opacity = 0,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.25f, Radius = 16, Offset = new Point(0, 4) },
            Content = new VerticalStackLayout { Spacing = 4, Children = { _title, _tip, buttons } },
        };
        _card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Answer(SpotlightAnswer.Next)) });

        Children.Add(_dim);
        Children.Add(_card);
        Opacity = 0;

        // The dim covers the whole window and the frames are window coordinates: no room for the status bar.
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.None;
        HandlerChanged += (_, _) => ConnectPlatform();
    }

    /// <summary>Ends the current step with <paramref name="answer"/>: a button, a tap, Back or Escape.</summary>
    public void Answer(SpotlightAnswer answer) => _answer?.TrySetResult(answer);

    /// <summary>
    /// Lights <paramref name="frame"/>, rounded by <paramref name="targetRadius"/> so the hole has the target's
    /// shape, and shows <paramref name="step"/>'s tip as step <paramref name="number"/> of <paramref name="count"/>,
    /// then waits for the user. The first step fades the whole view in; later steps move the hole and swap the card.
    /// </summary>
    public async Task<SpotlightAnswer> ShowStepAsync(Rect frame, double targetRadius, TourStep step, int number, int count)
    {
        var window = await WhenSizedAsync();

        var hole = SpotlightLayout.Hole((frame.X, frame.Y, frame.Width, frame.Height), step.Padding, window.Width, window.Height);
        var radius = SpotlightLayout.HoleRadius(hole.Width, hole.Height, targetRadius, step.Padding);
        var first = _hole is null;
        var still = ReducedMotion.IsOn;

        if (!first && !still)
            await _card.FadeToAsync(0, 120);

        _title.Text = step.Title;
        _title.IsVisible = !string.IsNullOrEmpty(step.Title);
        _tip.Text = step.Tip;
        _count.Text = count > 1 ? HighlightStrings.Step(number, count) : null;
        _count.IsVisible = count > 1;
        _next.Text = HighlightStrings.Get(number < count ? "Spine.Highlight.Next" : "Spine.Highlight.Done");
        PlaceCard(hole, window);

        if (first || still)
            SetHole(hole, radius);
        else
            await MoveHoleAsync(_hole!.Value, _radius, hole, radius);
        _hole = hole;
        _radius = radius;

        if (first)
        {
            _card.Opacity = 1;
            await this.FadeToAsync(1, still ? 0u : 200u);
        }
        else
        {
            await _card.FadeToAsync(1, still ? 0u : 150u);
        }

        _tip.SetSemanticFocus();
        SemanticScreenReader.Default.Announce(count > 1 ? $"{HighlightStrings.Step(number, count)}. {step.Tip}" : step.Tip);
        FocusPlatform();

        _answer = new TaskCompletionSource<SpotlightAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        return await _answer.Task;
    }

    public Task FadeOutAsync() => this.FadeToAsync(0, ReducedMotion.IsOn ? 0u : 180u);

    void PlaceCard((double X, double Y, double Width, double Height) hole, Size window)
    {
        var insets = SafeInsets();
        var width = Math.Min(CardMaxWidth, window.Width - 2 * SpotlightLayout.Margin);
        _card.WidthRequest = width;

        // Measured without the previous step's place: the measure includes the margin.
        _card.Margin = Thickness.Zero;
        var height = _card.Measure(width, double.PositiveInfinity).Height;

        var place = SpotlightLayout.Tip(hole, width, height, window.Width, window.Height, insets.Top, insets.Bottom);
        _card.Margin = new Thickness(place.X, place.Y, 0, 0);
    }

    void SetHole((double X, double Y, double Width, double Height) hole, double radius)
    {
        _drawable.Hole = hole;
        _drawable.Radius = radius;
        _dim.Invalidate();
    }

    Task MoveHoleAsync((double X, double Y, double Width, double Height) from, double fromRadius,
        (double X, double Y, double Width, double Height) to, double toRadius)
    {
        var done = new TaskCompletionSource();
        new Animation(progress => SetHole(SpotlightLayout.Between(from, to, progress),
                fromRadius + (toRadius - fromRadius) * SpotlightLayout.Ease(progress)))
            .Commit(this, "spine.spotlight.move", length: MoveMilliseconds, finished: (_, _) => done.TrySetResult());
        return done.Task;
    }

    /// <summary>
    /// The overlay's size: the window's. Read from the platform view, since a view with no MAUI parent is laid
    /// out by its platform view but never gets a <see cref="VisualElement.Width"/> of its own.
    /// </summary>
    Size WindowSize()
    {
#if IOS || MACCATALYST
        if (Handler?.PlatformView is UIKit.UIView view)
            return new Size(view.Bounds.Width, view.Bounds.Height);
#elif ANDROID
        if (Handler?.PlatformView is Android.Views.View view && view.Resources?.DisplayMetrics?.Density is { } density)
            return new Size(view.Width / density, view.Height / density);
#elif WINDOWS
        if (Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement { XamlRoot: { } root })
            return new Size(root.Size.Width, root.Size.Height);
#endif
        return Size.Zero;
    }

    async Task<Size> WhenSizedAsync()
    {
        for (var i = 0; i < 120; i++)
        {
            if (WindowSize() is { Width: > 0, Height: > 0 } size)
                return size;

            await Task.Delay(16);
        }

        return WindowSize();
    }

    /// <summary>The status bar and the home indicator or navigation bar, which the card stays clear of.</summary>
    Thickness SafeInsets()
    {
#if IOS || MACCATALYST
        if ((Handler?.PlatformView as UIKit.UIView)?.Window is { } window)
            return new Thickness(window.SafeAreaInsets.Left, window.SafeAreaInsets.Top, window.SafeAreaInsets.Right, window.SafeAreaInsets.Bottom);
#elif ANDROID
        if (IPlatformApplication.Current?.Services.GetService<ISystemInsetsProvider>() is { } provider)
            return provider.SystemBarInsets;
#endif
        return Thickness.Zero;
    }

    partial void ConnectPlatform();

    partial void FocusPlatform();

    /// <summary>The dim: the window darkened, with a rounded hole that lets the target through.</summary>
    sealed class DimDrawable : IDrawable
    {
        public (double X, double Y, double Width, double Height)? Hole { get; set; }

        public double Radius { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var path = new PathF();
            path.AppendRectangle(dirtyRect);
            if (Hole is { } hole)
            {
                var radius = (float)Math.Min(Radius, Math.Min(hole.Width, hole.Height) / 2);
                path.AppendRoundedRectangle((float)hole.X, (float)hole.Y, (float)hole.Width, (float)hole.Height, radius);
            }

            canvas.FillColor = Colors.Black.WithAlpha(0.6f);
            canvas.FillPath(path, WindingMode.EvenOdd);
        }
    }
}
