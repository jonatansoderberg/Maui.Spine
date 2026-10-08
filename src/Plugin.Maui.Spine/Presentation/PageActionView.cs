using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Presentation;

/// <summary>
/// One header bar action. It draws the action on one of two faces: when the action is replaced by
/// another (Back by Cancel, Filter by Bell) the new face fades and grows in while the old one fades
/// and shrinks out, at the same time, as a navigation bar's items do.
/// </summary>
/// <remarks>
/// With Liquid Glass and <see cref="SpineOptions.ApplePlatformOptions.MorphHeaderActions"/> there is one
/// glass button for icons and text alike, and a replacement changes it inside a UIKit spring
/// animation, so the glass itself morphs from a circle to a capsule.
/// </remarks>
internal sealed class PageActionView : ContentView
{
    public static readonly BindableProperty ActionProperty = BindableProperty.Create(
        nameof(Action),
        typeof(PageAction),
        typeof(PageActionView),
        default(PageAction),
        propertyChanged: OnActionChanged);

    /// <summary>A fixed colour for the text and the icon, or <see langword="null"/> to follow the theme.</summary>
    public static readonly BindableProperty ForegroundProperty = BindableProperty.Create(
        nameof(Foreground), typeof(Color), typeof(PageActionView), null,
        propertyChanged: static (b, _, _) => ((PageActionView)b).ForEachFace(f => f.ApplyForeground()));

    public static readonly BindableProperty GlassProperty = BindableProperty.Create(
        nameof(Glass), typeof(HeaderBarGlass), typeof(PageActionView), HeaderBarGlass.Regular,
        propertyChanged: static (b, _, _) => ((PageActionView)b).ForEachFace(f => f.ApplyGlass()));

    /// <summary>Whether the bar lies over the page's content at rest: a hero, a photo or a map under an Overlay header.</summary>
    public static readonly BindableProperty OverContentProperty = BindableProperty.Create(
        nameof(OverContent), typeof(bool), typeof(PageActionView), false,
        propertyChanged: static (b, _, _) => ((PageActionView)b).ForEachFace(f => f.ApplyGlass()));

    /// <summary>How far the bar's own background has faded in, 0 to 1; a container behind the button fades out as it does.</summary>
    public static readonly BindableProperty BackgroundProgressProperty = BindableProperty.Create(
        nameof(BackgroundProgress), typeof(double), typeof(PageActionView), 0.0,
        propertyChanged: static (b, _, _) => ((PageActionView)b).ForEachFace(f => f.ApplyContainerFade()));

    public bool OverContent
    {
        get => (bool)GetValue(OverContentProperty);
        set => SetValue(OverContentProperty, value);
    }

    public double BackgroundProgress
    {
        get => (double)GetValue(BackgroundProgressProperty);
        set => SetValue(BackgroundProgressProperty, value);
    }

    /// <summary>
    /// Padding inside an icon button, between its slot and its glyph. Not the view's own
    /// <see cref="Microsoft.Maui.Controls.Layout.Padding"/>: that pads the slot as well, and the two together
    /// put a header bar's buttons twice the padding in from the edge.
    /// </summary>
    public static readonly BindableProperty ButtonPaddingProperty = BindableProperty.Create(
        nameof(ButtonPadding), typeof(Thickness), typeof(PageActionView), Thickness.Zero);

    public Thickness ButtonPadding
    {
        get => (Thickness)GetValue(ButtonPaddingProperty);
        set => SetValue(ButtonPaddingProperty, value);
    }

    public HeaderBarGlass Glass
    {
        get => (HeaderBarGlass)GetValue(GlassProperty);
        set => SetValue(GlassProperty, value);
    }

    public Color? Foreground
    {
        get => (Color?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static readonly BindableProperty HideDisabledProperty = BindableProperty.Create(
        nameof(HideDisabled),
        typeof(bool),
        typeof(PageActionView),
        false,
        propertyChanged: static (b, _, _) => ((PageActionView)b).ForEachFace(f => f.ApplyHideDisabled()));

    /// <summary>
    /// Width of the slot an icon action takes. A text action sizes to its text; the view itself
    /// always sizes to its content, so a face that is fading out keeps its own size.
    /// </summary>
    public static readonly BindableProperty IconWidthProperty = BindableProperty.Create(
        nameof(IconWidth), typeof(double), typeof(PageActionView), HeaderBarConstants.Height,
        propertyChanged: static (b, _, _) => ((PageActionView)b)._front.ApplyIconWidth());

    public PageAction? Action
    {
        get => (PageAction?)GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    public bool HideDisabled
    {
        get => (bool)GetValue(HideDisabledProperty);
        set => SetValue(HideDisabledProperty, value);
    }

    public double IconWidth
    {
        get => (double)GetValue(IconWidthProperty);
        set => SetValue(IconWidthProperty, value);
    }

    readonly bool _glass;
    readonly bool _morph;
    Face _front;
    Face _back;

    public PageActionView()
    {
        _glass = UseGlassHeaderActions;
        _morph = _glass && OperatingSystem.IsIOSVersionAtLeast(26)
            && IPlatformApplication.Current?.Services.GetService<SpineOptions>()?.Apple.MorphHeaderActions == true;
        _front = new Face(this);
        _back = new Face(this) { Opacity = 0, IsVisible = false, InputTransparent = true };

        Content = new Grid { Children = { _back, _front } };

        // Keep the colours in sync when the user switches the theme or the accent at runtime.
        SpineTheme.Track(this, () => ForEachFace(f => f.ApplyForeground()));

        _front.Apply(null);
    }

    // The option is read here rather than passed down: HeaderBarView and PageActionView are built by
    // pages, not by DI, and the attached property is a no-op off Apple anyway.
    internal static bool UseGlassHeaderActions =>
        OperatingSystem.IsIOS()
        && IPlatformApplication.Current?.Services.GetService<SpineOptions>()?.Apple.GlassHeaderActions == true;

    /// <summary>Raised when the assigned action's <see cref="PageAction.IsVisible"/> changes in place.</summary>
    public event Action? VisibilityChanged;

    /// <summary>
    /// How long a header bar action takes to swap, show or hide: close to UIKit's navigation bar on
    /// Apple, Material 3's fade-through on Android. Short and without movement under Reduce Motion.
    /// </summary>
    internal static uint TransitionDuration => ReducedMotion.IsOn ? 150u
        : DeviceInfo.Platform == DevicePlatform.Android ? 150u
        : DeviceInfo.Platform == DevicePlatform.WinUI ? 200u
        : 300u;

    /// <summary>How long an action takes to go away: half as long as it takes to arrive.</summary>
    internal static uint RemovalDuration => TransitionDuration / 2;

    /// <summary>The scale an action grows from and shrinks to; 1 (none) under Reduce Motion.</summary>
    internal static double TransitionScale => ReducedMotion.IsOn ? 1 : 0.85;

    internal static readonly Easing TransitionEasing = Easing.CubicOut;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == HeightRequestProperty.PropertyName && _front is not null)
            _front.ApplyIconWidth();
    }

    void ForEachFace(Action<Face> apply)
    {
        apply(_front);
        apply(_back);
    }

    static void OnActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (PageActionView)bindable;

        if (oldValue is PageAction oldAction)
            oldAction.PropertyChanged -= view.OnActionPropertyChanged;
        if (newValue is PageAction newAction)
            newAction.PropertyChanged += view.OnActionPropertyChanged;

        var oldSvg = (oldValue as PageAction)?.Svg;
        var newSvg = (newValue as PageAction)?.Svg;
        var sameGlyph = !string.IsNullOrWhiteSpace(oldSvg) && oldSvg == newSvg;

        // The same icon under another action (a page's Save after the previous page's Save) is not
        // a change the user sees; anything else crosses over.
        if (sameGlyph || view.Opacity == 0 || !view.IsVisible)
            view._front.Apply((PageAction?)newValue);
        else if (view._morph)
            view.Morph((PageAction?)newValue);
        else
            _ = view.SwapAsync((PageAction?)newValue);
    }

    void OnActionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PageAction.Svg) when _morph:
                Morph(Action);
                break;
            case nameof(PageAction.Svg):
                _ = SwapAsync(Action);
                break;
            case nameof(PageAction.IsVisible):
                _front.Apply(Action);
                VisibilityChanged?.Invoke();
                break;
            default:
                _front.Apply(Action);
                break;
        }
    }

    /// <summary>
    /// Changes the one glass button to <paramref name="action"/> and lays the bar out again inside a
    /// spring animation: UIKit animates the new frame and the button's content, and the glass
    /// reshapes with it.
    /// </summary>
    void Morph(PageAction? action)
    {
#if IOS || MACCATALYST
        if (Window?.Handler?.PlatformView is UIKit.UIWindow window)
        {
            var reduced = ReducedMotion.IsOn;

            // MAUI measures and arranges in the window's layout pass, so that is the pass to run
            // inside the spring. Whatever else is pending (a page arriving, its title) is laid out
            // first, without animation, so only this action's change is animated.
            UIKit.UIView.PerformWithoutAnimation(window.LayoutIfNeeded);

            // Everything starts at once, so this action moves together with the other one and
            // with the page: the new content goes in invisible and fades in while the glass
            // grows or shrinks to it inside the spring.
            UIKit.UIView.PerformWithoutAnimation(() => _front.Apply(action));
            GlassAppearance.SetContentAlpha(_front, 0);
            ((IView)this).InvalidateMeasure();

            UIKit.UIView.AnimateNotify(
                reduced ? 0.2 : 0.45, 0, reduced ? 1f : 0.82f, 0,
                UIKit.UIViewAnimationOptions.BeginFromCurrentState | UIKit.UIViewAnimationOptions.AllowUserInteraction,
                window.LayoutIfNeeded,
                null);

            // The glass starts moving at once; the content follows a beat later, when the
            // capsule is wide enough not to clip it.
            _ = GlassAppearance.FadeContentAsync(_front, show: true, reduced ? 150u : 250u, delay: reduced ? 0 : 0.1);
            return;
        }
#endif
        _front.Apply(action);
    }

    /// <summary>Crosses from what the front face shows to <paramref name="action"/> on the other face.</summary>
    async Task SwapAsync(PageAction? action)
    {
        var outgoing = _front;
        var incoming = _back;

        this.AbortAnimation("Swap");

        _front = incoming;
        _back = outgoing;

        // The outgoing face keeps the size it has now, whatever the slot does next.
        outgoing.Freeze();
        outgoing.InputTransparent = true;

        incoming.Apply(action);
        incoming.InputTransparent = false;
        incoming.IsVisible = true;
        incoming.Opacity = 0;
        incoming.Scale = TransitionScale;

        // Drawn above the outgoing face, so a tap during the swap reaches the new action.
        incoming.ZIndex = 1;
        outgoing.ZIndex = 0;

        var duration = TransitionDuration;
        var shrink = TransitionScale;

#if IOS || MACCATALYST
        // Glass turns flat grey when its alpha fades: materialize and dissolve it instead.
        if (_glass && GlassAppearance.Applies(this))
        {
            incoming.Opacity = 1;
            outgoing.Opacity = 1;

            await Task.WhenAll(
                GlassAppearance.AnimateAsync(incoming, show: true, duration),
                incoming.ScaleToAsync(1, duration, TransitionEasing),
                GlassAppearance.AnimateAsync(outgoing, show: false, RemovalDuration),
                outgoing.ScaleToAsync(shrink, RemovalDuration, TransitionEasing));

            if (ReferenceEquals(_back, outgoing))
            {
                outgoing.IsVisible = false;
                outgoing.Scale = 1;
                outgoing.Apply(null);
            }
            return;
        }
#endif

        var tcs = new TaskCompletionSource();

        new Animation
        {
            { 0, 1, new Animation(v => incoming.Opacity = v, 0, 1) },
            { 0, 1, new Animation(v => incoming.Scale = v, shrink, 1) },
            // The old face is gone halfway, well before the new one has settled.
            { 0, 0.5, new Animation(v => outgoing.Opacity = v, outgoing.Opacity, 0) },
            { 0, 0.5, new Animation(v => outgoing.Scale = v, 1, shrink) },
        }.Commit(this, "Swap", 16, duration, TransitionEasing, (_, cancelled) =>
        {
            if (!cancelled && ReferenceEquals(_back, outgoing))
            {
                outgoing.IsVisible = false;
                outgoing.Scale = 1;
                outgoing.Apply(null);
            }

            incoming.Opacity = 1;
            incoming.Scale = 1;
            tcs.TrySetResult();
        });

        await tcs.Task;
    }

    /// <summary>One way of drawing the action: a text button, an icon button and a badge.</summary>
    sealed class Face : Grid
    {
        readonly PageActionView _owner;
        readonly Button _textButton;
        readonly ImageButton _imageButton;
        readonly Border _badge;
        readonly Label _badgeLabel;
        // The icon of a morphing action, drawn over its glass circle; taps go to the button under it.
        readonly Image _morphIcon = new()
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            IsVisible = false,
        };
        string? _currentSvg;
        // A confirm action leads the screen: tinted glass on iOS 26, a filled accent circle elsewhere. A selected
        // action (a toggle that is on) is filled the same way, with the foreground instead of the accent.
        bool _prominent;
        bool _selected;

        public Face(PageActionView owner)
        {
            _owner = owner;

            // Primary actions sit at the leading edge and secondary ones at the trailing edge; two
            // faces of different widths overlap at that edge.
            SetBinding(HorizontalOptionsProperty, new Binding(nameof(HorizontalOptions), source: owner));

            _textButton = new Button
            {
                BackgroundColor = Colors.Transparent,
                BorderWidth = 0,
                Margin = new Thickness(12, 0, 12, 0),
            };

            ButtonExtensions.SetCompact(_textButton, true);

            // Re-apply in HandlerChanged because the implicit Button style is applied when the
            // view enters the visual tree and can race with the initial assignment.
            // Direct SetValue (not a binding) definitively wins over any style setter.
            _textButton.HandlerChanged += (_, _) =>
            {
                if (_textButton.Handler is not null)
                    ApplyForeground();
            };

            _imageButton = new ImageButton
            {
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.Transparent,
                BorderWidth = 0,
                BorderColor = Colors.Transparent,
                CornerRadius = DeviceInfo.Platform == DevicePlatform.Android ? 24 : 0,
            };
            _imageButton.ApplyCommonVisualStates(owner.HideDisabled);

            if (Circles)
            {
                _imageButton.HeightRequest = FilledSize;
                _imageButton.CornerRadius = (int)(FilledSize / 2);
                _imageButton.HorizontalOptions = LayoutOptions.Center;
                _imageButton.Margin = new Thickness(CircleInset, 0);
            }
            else
            {
                _imageButton.SetBinding(VisualElement.HeightRequestProperty, new Binding(nameof(HeightRequest), source: owner));
                _imageButton.SetBinding(ImageButton.PaddingProperty, new Binding(nameof(ButtonPadding), source: owner));
            }
            ApplyIconWidth();

            if (owner._glass)
            {
                // Compact zeroed the padding; the capsule needs some room around the text, and it
                // sits centred in the 44-point row rather than filling it. The capsule is the
                // button's edge, so it lines up with the page margin like an icon's circle; the
                // plain text button's inset would push it 12 points further in.
                _textButton.Margin = new Thickness(0);
                _textButton.Padding = new Thickness(14, 8);
                _textButton.VerticalOptions = LayoutOptions.Center;

                // The glass makes the slot visible, so the icon becomes a circle centred in it
                // rather than a pill hugging the screen edge.
                _imageButton.HorizontalOptions = LayoutOptions.Center;
                ApplyGlass();
            }

            _badgeLabel = new Label
            {
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.NoWrap,
            };

            // The same red the native tab badges use, so a count reads the same everywhere.
            _badge = new Border
            {
                Content = _badgeLabel,
                BackgroundColor = Color.FromArgb("#FF3B30"),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(4, 0),
                MinimumWidthRequest = 16,
                HeightRequest = 16,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                // On the glass capsule the pill sits inside the slot's corner; on a plain text button
                // it sits past the text, which ends 12 points short of the edge.
                Margin = owner._glass ? new Thickness(0, 2, 6, 0) : new Thickness(0, 0, 0, 0),
                InputTransparent = true,
                IsVisible = false,
            };

            Children.Add(_textButton);
            Children.Add(_imageButton);
            Children.Add(_morphIcon);
            Children.Add(_badge);

            ApplyForeground();
        }

        /// <summary>
        /// The one glass button draws icons too: an icon action is an empty circle as tall as the
        /// row with the SVG drawn over it, a text action a capsule around its text.
        /// </summary>
        void ApplyMorphing(PageAction action, bool hasSvg)
        {
            _imageButton.IsVisible = false;
            _textButton.IsVisible = true;
            _textButton.IsEnabled = action.IsEnabled;
            _textButton.Opacity = action.IsEnabled ? 1 : 0.4;

            _badgeLabel.Text = action.Badge ?? string.Empty;
            _badge.IsVisible = !string.IsNullOrEmpty(action.Badge);
            SemanticProperties.SetDescription(_textButton, action.Description ?? (hasSvg ? null : action.Text));
            MenuButton.SetItems(_textButton, action.Menu);
            MenuButton.SetShowsSelection(_textButton, action.MenuShowsSelection);
            Haptics.SetOnTap(_textButton, action.Haptic);

            _currentSvg = hasSvg ? action.Svg : null;
            ApplyProminence(action.Role == PageActionRole.Confirm || action.IsSelected, action.IsSelected);

            // The icon is not the glass button's image: a glass configuration that has held an
            // image draws later titles in the label colour, not the accent.
            _morphIcon.IsVisible = hasSvg;

            if (hasSvg)
            {
                // Not an empty title: a glass button whose title goes empty draws its next one in
                // the label colour rather than the accent.
                _textButton.Text = "\u200B";
                _textButton.Padding = new Thickness(0);
                _textButton.WidthRequest = _owner.HeightRequest;
                _textButton.HeightRequest = _owner.HeightRequest;
                ApplyMorphingImage();
            }
            else
            {
                _textButton.Padding = new Thickness(14, 8);
                _textButton.WidthRequest = -1;
                _textButton.HeightRequest = -1;
                _textButton.Text = action.Text ?? string.Empty;
            }

            _textButton.Command = action.Command;
            _textButton.CommandParameter = action.CommandParameter;
        }

        // The glyph at the size and weight a UIBarButtonItem's symbol has, tinted like the icon buttons.
        void ApplyMorphingImage()
        {
            var names = IPlatformApplication.Current?.Services.GetService<ResourceNameCache>();
            if (_currentSvg is not { } svg || names?.Resolve(svg) is not { } resource)
                return;

            var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var tint = _prominent ? OnFill() : _owner.Foreground ?? (dark ? Colors.White : Colors.Black);
            var size = GlyphSize(svg);
            _morphIcon.WidthRequest = size;
            _morphIcon.HeightRequest = size;
            _morphIcon.Source = SvgBitmapLoader.LoadFromEmbedded(resource, size, size, tint, Thickness.Zero, lineWidthScale: (float)LineWidthScale(svg));
        }

        /// <summary>A glass icon is a circle as tall as the row; otherwise it fills the slot the bar gives it.</summary>
        public void ApplyIconWidth()
        {
            _imageButton.RemoveBinding(VisualElement.WidthRequestProperty);
            _imageButton.WidthRequest = _owner._glass ? _owner.HeightRequest : Filled || Circles ? FilledSize : _owner.IconWidth;
        }

        const double FilledSize = 40;

        // How far a filled circle's outer edge sits inside its slot.
        const double GlyphInset = 5;

        // The back button's chevron has a size and weight of its own, as UIKit's back indicator does.
        static double GlyphSize(string? svg) =>
            svg == HeaderBarConstants.BackGlyph ? HeaderBarConstants.BackGlyphSize : HeaderBarConstants.GlyphSize;

        static double LineWidthScale(string? svg) =>
            svg == HeaderBarConstants.BackGlyph ? HeaderBarConstants.BackGlyphLineWidthScale : HeaderBarConstants.GlyphLineWidthScale;

        // The inset that centres the glyph at its size in the glass circle, the 40-point circle or the bare slot.
        Thickness GlyphPadding(string? svg)
        {
            var side = _owner._glass ? _owner.HeightRequest
                : Filled || Circles ? FilledSize
                : Math.Min(_owner.HeightRequest, _owner.IconWidth);
            return new Thickness(Math.Max(0, (side - GlyphSize(svg)) / 2));
        }

        // Material 3's icon button: a 40-point circle (the state layer, or the fill) centred in a 48-point
        // touch target, with a 24-point icon. Every icon button on Android has that shape, filled or not, so
        // they all sit the same way: the circle 8 points from the edge and the icon at the page margin.
        static readonly bool Circles = DeviceInfo.Platform == DevicePlatform.Android;
        const double CircleInset = 4;

        // Over a photo or a camera a bare icon disappears against light parts of the picture. Where there is no
        // glass to clear it, a clear-glass bar puts each button on a dark translucent circle, as Google's camera
        // and scanner apps do and Material asks for icon buttons over imagery.
        static readonly Color ScrimFill = Colors.Black.WithAlpha(0.4f);

        bool Scrim => !_owner._glass && !_prominent && _owner.Glass == HeaderBarGlass.Clear;

        // Under an Overlay header with regular glass, the button is Material 3's filled tonal icon button
        // instead: a circle in the surface container's tone, which reads over the picture as iOS 26's
        // frosted glass does, where a bare icon would not.
        bool Tonal => !_owner._glass && !_prominent && _owner.Glass == HeaderBarGlass.Regular && _owner.OverContent;

        bool Filled => _prominent || Scrim || Tonal;

        // Slightly translucent, so the picture still shows through a little, as through glass.
        static readonly Color TonalLight = Color.FromRgba(0.95f, 0.95f, 0.96f, 0.88f);
        static readonly Color TonalDark = Color.FromRgba(0.17f, 0.17f, 0.18f, 0.88f);

        // The tone that contrasts with the icon: a light circle under a dark icon and the other way round, so a
        // white foreground chosen for a dark photo does not land on a light circle.
        Color TonalFill()
        {
            var icon = _owner.Foreground ?? (IsDark ? Colors.White : Colors.Black);
            return icon.GetLuminosity() > 0.5f ? TonalDark : TonalLight;
        }

        // A container stands in for the bar's background while the bar has none: as the background fades
        // in on scroll it fades out, and the button is a plain icon button on the bar.
        Color ContainerFill(Color fill)
        {
            var fade = Math.Round(1 - Math.Clamp(_owner.BackgroundProgress, 0, 1), 2);
            return fill.WithAlpha(fill.Alpha * (float)fade);
        }

        Color? _appliedFill;

        public void ApplyContainerFade()
        {
            if (Scrim || Tonal)
                ApplyForeground();
        }

        bool _filledShape;

        /// <summary>
        /// Without glass, a 40-point circle inside the slot for a filled button (Material 3's filled icon button),
        /// clear of the sheet's edge; otherwise the icon fills the slot.
        /// </summary>
        void ApplyShape()
        {
            var filled = Filled;
            if (filled == _filledShape)
                return;

            _filledShape = filled;
            // An Android circle keeps its size and its glyph; only its fill changes
            if (!Circles && filled)
            {
                // The circle's outer edge goes where a bare glyph's would be, on whichever side the slot is,
                // so a circle and an icon line up with each other and with the page's content
                var leading = _owner.HorizontalOptions.Alignment == LayoutAlignment.Start;
                var outer = (leading ? _owner.ButtonPadding.Left : _owner.ButtonPadding.Right) + GlyphInset;
                _imageButton.HorizontalOptions = leading ? LayoutOptions.Start : LayoutOptions.End;
                _imageButton.RemoveBinding(VisualElement.HeightRequestProperty);
                _imageButton.RemoveBinding(ImageButton.PaddingProperty);
                _imageButton.HeightRequest = FilledSize;
                _imageButton.Padding = new Thickness(0);
                _imageButton.Margin = leading ? new Thickness(outer, 0, 0, 0) : new Thickness(0, 0, outer, 0);
            }
            else if (!Circles)
            {
                _imageButton.HorizontalOptions = LayoutOptions.End;
                _imageButton.Margin = new Thickness(0);
                _imageButton.SetBinding(VisualElement.HeightRequestProperty, new Binding(nameof(HeightRequest), source: _owner));
                _imageButton.SetBinding(ImageButton.PaddingProperty, new Binding(nameof(ButtonPadding), source: _owner));
            }
            _imageButton.CornerRadius = filled || Circles ? (int)(FilledSize / 2) : 0;
            _textButton.CornerRadius = filled ? (int)Math.Round(_owner.HeightRequest / 2) : -1;
            if (!filled)
            {
                _imageButton.ApplyCommonVisualStates(_owner.HideDisabled);
                _appliedFill = null;
            }

            if (_imageButton.Behaviors.OfType<SvgImageSourceBehavior>().FirstOrDefault() is { } svg)
                svg.Padding = GlyphPadding(svg.Svg);
        }

        /// <summary>
        /// The prominent look of a confirm: the glass tinted with the accent (iOS 26's prominent bar button), or
        /// without glass a filled accent circle; the glyph or text in the colour that reads on the accent. A selected
        /// action takes the same look in the foreground colour.
        /// </summary>
        void ApplyProminence(bool prominent, bool selected)
        {
            if (prominent == _prominent && selected == _selected)
                return;

            _selected = selected;
            if (prominent == _prominent)
            {
                ApplyForeground();
                return;
            }

            _prominent = prominent;
            if (_owner._glass)
                ApplyGlass();
            else
                ApplyShape();
            ApplyIconWidth();
            ApplyForeground();
        }

        // The common states set the background, and a state's value outranks the fill set on the button,
        // so a filled button gets states of its own: the fill, lighter while pressed, dimmed when disabled
        void ApplyFilledStates(Color fill)
        {
            var group = new VisualStateGroup { Name = "CommonStates" };
            void Add(string name, Color background, double opacity)
            {
                var state = new VisualState { Name = name };
                state.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = background });
                state.Setters.Add(new Setter { Property = VisualElement.OpacityProperty, Value = opacity });
                group.States.Add(state);
            }
            Add("Normal", fill, 1);
            Add("PointerOver", fill, 1);
            // A container faded out with the bar's background leaves Material's state layer as the only press feedback
            var pressed = fill.Alpha < 0.1f
                ? (IsDark ? Colors.White : Colors.Black).WithAlpha(0.1f)
                : fill.WithAlpha(fill.Alpha * 0.75f);
            Add("Pressed", pressed, 1);
            Add("Disabled", fill, _owner.HideDisabled ? 0 : 0.4);
            VisualStateManager.SetVisualStateGroups(_imageButton, [group]);
        }

        static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark
            || (Application.Current?.RequestedTheme != AppTheme.Light
                && Application.Current?.PlatformAppTheme == AppTheme.Dark);

        static Color Accent() => SpineTheme.GetAccent(IsDark ? AppTheme.Dark : AppTheme.Light)
            ?? Color.FromArgb(IsDark ? "#0A84FF" : "#007AFF");

        Color Fill() => _selected
            ? _owner.Foreground ?? (IsDark ? Colors.White : Colors.Black)
            : Accent();

        Color OnFill() => SpineAccent.TextOn(Fill());

        /// <summary>Holds the face at its current size while it fades out.</summary>
        public void Freeze()
        {
            if (Width > 0)
                WidthRequest = Width;
        }

        public void ApplyHideDisabled()
        {
            if (!Filled || _owner._glass)
            {
                _imageButton.ApplyCommonVisualStates(_owner.HideDisabled);
                return;
            }

            // The filled states dim a disabled button by HideDisabled too
            _appliedFill = null;
            ApplyForeground();
        }

        public void ApplyGlass()
        {
            if (!_owner._glass)
            {
                ApplyShape();
                ApplyIconWidth();
                ApplyForeground();
                return;
            }

            var clear = _owner.Glass == HeaderBarGlass.Clear;
            var style = _prominent
                ? clear ? GlassStyle.ProminentClear : GlassStyle.Prominent
                : clear ? GlassStyle.Clear : GlassStyle.Regular;
            Extensions.Glass.SetStyle(_imageButton, style);
            Extensions.Glass.SetStyle(_textButton, style);
        }

        public void ApplyForeground()
        {
            // Read again on every theme or accent change, so the prominent fill follows the accent
            var fill = _prominent ? Fill() : Scrim ? ContainerFill(ScrimFill) : Tonal ? ContainerFill(TonalFill()) : Colors.Transparent;
            _imageButton.BackgroundColor = fill;
            _textButton.BackgroundColor = fill;

            // The states are rebuilt only when the fill changes: a fade calls this on every scroll frame
            if (Filled && !_owner._glass && !fill.Equals(_appliedFill))
            {
                _appliedFill = fill;
                ApplyFilledStates(fill);
            }

            if (_prominent)
                _textButton.TextColor = OnFill();
            else if (_owner.Foreground is { } foreground)
                _textButton.TextColor = foreground;
            else
                _textButton.TextColor = Accent();

            if (_imageButton.Behaviors.OfType<SvgImageSourceBehavior>().FirstOrDefault() is { } svg)
            {
                svg.TintColor = _prominent ? OnFill() : _owner.Foreground;
                svg.UpdateImage();
            }

            if (_owner._morph && _morphIcon.IsVisible)
                ApplyMorphingImage();
        }

        public void Apply(PageAction? action)
        {
            WidthRequest = -1;
            ApplyIconWidth();

            if (action is null || !action.IsVisible)
            {
                _textButton.IsVisible = false;
                _imageButton.IsVisible = false;
                _badge.IsVisible = false;
                return;
            }

            var hasSvg = !string.IsNullOrWhiteSpace(action.Svg);

            if (_owner._morph)
            {
                ApplyMorphing(action, hasSvg);
                return;
            }

            _imageButton.IsVisible = hasSvg;
            _textButton.IsVisible = !hasSvg;
            _imageButton.IsEnabled = action.IsEnabled;
            _textButton.IsEnabled = action.IsEnabled;
            // The app's Disabled visual state may not reach a button whose colour is set directly.
            _imageButton.Opacity = action.IsEnabled ? 1 : 0.4;
            _textButton.Opacity = action.IsEnabled ? 1 : 0.4;

            _badgeLabel.Text = action.Badge ?? string.Empty;
            SemanticProperties.SetDescription(_imageButton, action.Description);
            SemanticProperties.SetDescription(_textButton, action.Description);
            ApplyProminence(action.Role == PageActionRole.Confirm || action.IsSelected, action.IsSelected);

            MenuButton.SetItems(_imageButton, action.Menu);
            MenuButton.SetItems(_textButton, action.Menu);
            MenuButton.SetShowsSelection(_textButton, action.MenuShowsSelection);
            Haptics.SetOnTap(_imageButton, action.Haptic);
            Haptics.SetOnTap(_textButton, action.Haptic);
            _badge.IsVisible = !string.IsNullOrEmpty(action.Badge);

            if (hasSvg)
            {
                if (action.Svg != _currentSvg)
                {
                    _imageButton.Behaviors.Clear();
                    var behavior = new SvgImageSourceBehavior
                    {
                        Svg = action.Svg!,
                        LightTintColor = Colors.Black,
                        DarkTintColor = Colors.White,
                        TintColor = _prominent ? OnFill() : _owner.Foreground,
                        Padding = GlyphPadding(action.Svg),
                        LineWidthScale = LineWidthScale(action.Svg),
                    };
                    _imageButton.Behaviors.Add(behavior);
                    _currentSvg = action.Svg;
                }

                _imageButton.Command = action.Command;
                _imageButton.CommandParameter = action.CommandParameter;
            }
            else
            {
                if (_currentSvg is not null)
                {
                    _imageButton.Behaviors.Clear();
                    _currentSvg = null;
                }

                _textButton.Text = action.Text ?? string.Empty;
                _textButton.Command = action.Command;
                _textButton.CommandParameter = action.CommandParameter;
            }
        }
    }
}
