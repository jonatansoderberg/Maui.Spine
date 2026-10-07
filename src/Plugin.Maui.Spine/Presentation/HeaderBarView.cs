using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Presentation;

#if WINDOWS
using Microsoft.UI.Windowing;
using Microsoft.Maui.Platform;
#endif

namespace Plugin.Maui.Spine.Presentation;

internal class HeaderBarView : Microsoft.Maui.Controls.ContentView
{
    public static readonly BindableProperty IsBackButtonVisibleProperty = BindableProperty.Create(
        nameof(IsBackButtonVisible), typeof(bool), typeof(HeaderBarView), true, propertyChanged: OnIsBackButtonVisibleChanged);

    public static readonly BindableProperty IsHeaderBarVisibleProperty = BindableProperty.Create(
        nameof(IsHeaderBarVisible), typeof(bool), typeof(HeaderBarView), true, propertyChanged: OnIsHeaderBarVisibleChanged);

    public static readonly BindableProperty IsTitleBarVisibleProperty = BindableProperty.Create(
        nameof(IsTitleBarVisible), typeof(bool), typeof(HeaderBarView), false, propertyChanged: OnIsTitleBarVisibleChanged);

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(IAsyncRelayCommand), typeof(HeaderBarView), default(IAsyncRelayCommand), propertyChanged: OnCloseCommandChanged);

    public static readonly BindableProperty BackCommandProperty = BindableProperty.Create(
        nameof(BackCommand), typeof(IAsyncRelayCommand), typeof(HeaderBarView), default(IAsyncRelayCommand), propertyChanged: OnBackCommandChanged);

    public static readonly BindableProperty DefaultPageActionProperty = BindableProperty.Create(
        nameof(DefaultPageAction), typeof(PageAction), typeof(HeaderBarView), default, propertyChanged: DefaultPageActionChanged);

    /// <summary>A fixed colour for the actions' text and icons, or <see langword="null"/> to follow the theme.</summary>
    public static readonly BindableProperty ForegroundProperty = BindableProperty.Create(
        nameof(Foreground), typeof(Color), typeof(HeaderBarView), null,
        propertyChanged: static (b, _, v) =>
        {
            var bar = (HeaderBarView)b;
            bar._primaryPageActionView.Foreground = (Color?)v;
            bar._secondaryPageActionView.Foreground = (Color?)v;
        });

    public Color? Foreground
    {
        get => (Color?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The glass of the actions' buttons on iOS 26.</summary>
    public static readonly BindableProperty GlassProperty = BindableProperty.Create(
        nameof(Glass), typeof(HeaderBarGlass), typeof(HeaderBarView), HeaderBarGlass.Regular,
        propertyChanged: static (b, _, v) =>
        {
            var bar = (HeaderBarView)b;
            bar._primaryPageActionView.Glass = (HeaderBarGlass)v;
            bar._secondaryPageActionView.Glass = (HeaderBarGlass)v;
        });

    public HeaderBarGlass Glass
    {
        get => (HeaderBarGlass)GetValue(GlassProperty);
        set => SetValue(GlassProperty, value);
    }

    /// <summary>Whether the bar lies over the page's content at rest (an Overlay header).</summary>
    public static readonly BindableProperty OverContentProperty = BindableProperty.Create(
        nameof(OverContent), typeof(bool), typeof(HeaderBarView), false,
        propertyChanged: static (b, _, v) =>
        {
            var bar = (HeaderBarView)b;
            bar._primaryPageActionView.OverContent = (bool)v;
            bar._secondaryPageActionView.OverContent = (bool)v;
        });

    public bool OverContent
    {
        get => (bool)GetValue(OverContentProperty);
        set => SetValue(OverContentProperty, value);
    }

    /// <summary>How far the bar's own background has faded in, 0 to 1.</summary>
    public static readonly BindableProperty BackgroundProgressProperty = BindableProperty.Create(
        nameof(BackgroundProgress), typeof(double), typeof(HeaderBarView), 0.0,
        propertyChanged: static (b, _, v) =>
        {
            var bar = (HeaderBarView)b;
            bar._primaryPageActionView.BackgroundProgress = (double)v;
            bar._secondaryPageActionView.BackgroundProgress = (double)v;
        });

    public double BackgroundProgress
    {
        get => (double)GetValue(BackgroundProgressProperty);
        set => SetValue(BackgroundProgressProperty, value);
    }

    public static readonly BindableProperty PrimaryPageActionProperty = BindableProperty.Create(
        nameof(PrimaryPageAction), typeof(PageAction), typeof(HeaderBarView), default, propertyChanged: PrimaryPageActionChanged);

    public static readonly BindableProperty PresentationProperty = BindableProperty.Create(
        nameof(Presentation),
        typeof(NavigationPresentation),
        typeof(HeaderBarView),
        defaultValue: NavigationPresentation.RegionPresentation,
        propertyChanged: OnPresentationChanged);

    readonly PageActionView _primaryPageActionView;
    readonly PageActionView _secondaryPageActionView;

    /// <summary>
    /// Raised when <see cref="PrimaryActionSlot"/> or <see cref="SecondaryActionSlot"/> changes.
    /// </summary>
    public event Action? ActionSlotsChanged;

    /// <summary>
    /// How far the leading action reaches in from the left edge of the bar, or 0 when it is
    /// hidden. Measured after layout so a text action reports the width it actually took.
    /// </summary>
    public double PrimaryActionSlot { get; private set; }

    /// <summary>How far the trailing action reaches in from the right edge of the bar, or 0.</summary>
    public double SecondaryActionSlot { get; private set; }

    public bool IsBackButtonVisible
    {
        get => (bool)GetValue(IsBackButtonVisibleProperty);
        set => SetValue(IsBackButtonVisibleProperty, value);
    }

    public bool IsHeaderBarVisible
    {
        get => (bool)GetValue(IsHeaderBarVisibleProperty);
        set => SetValue(IsHeaderBarVisibleProperty, value);
    }

    public bool IsTitleBarVisible
    {
        get => (bool)GetValue(IsTitleBarVisibleProperty);
        set => SetValue(IsTitleBarVisibleProperty, value);
    }

    public IAsyncRelayCommand CloseCommand
    {
        get => (IAsyncRelayCommand)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public IAsyncRelayCommand BackCommand
    {
        get => (IAsyncRelayCommand)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public PageAction DefaultPageAction
    {
        get => (PageAction)GetValue(DefaultPageActionProperty);
        set => SetValue(DefaultPageActionProperty, value);
    }

    public PageAction PrimaryPageAction
    {
        get => (PageAction)GetValue(PrimaryPageActionProperty);
        set => SetValue(PrimaryPageActionProperty, value);
    }

    public NavigationPresentation Presentation
    {
        get => (NavigationPresentation)GetValue(PresentationProperty);
        set => SetValue(PresentationProperty, value);
    }

    static void OnCloseCommandChanged(BindableObject bindable, object oldValue, object newValue)
    {
    }

    static void OnBackCommandChanged(BindableObject bindable, object oldValue, object newValue)
    {
    }

    static void DefaultPageActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBarView)bindable;
        view._secondaryPageActionView.Action = (PageAction?)newValue;
        view.UpdateSecondaryActionVisibility();
    }

    static void PrimaryPageActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBarView)bindable;
        view._primaryPageActionView.Action = (PageAction?)newValue;
        view.UpdatePrimaryActionVisibility();
    }

    static void OnIsHeaderBarVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBarView)bindable;
        view.SetIsHeaderBarVisible((bool)newValue);
    }

    static void OnIsBackButtonVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBarView)bindable;
        view.SetIsBackButtonVisible((bool)newValue);
    }

    static void OnIsTitleBarVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBarView)bindable;
        view.UpdateSecondaryActionVisibility();
    }

    static void OnPresentationChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBarView)bindable;
        view.UpdatePresentationSizes();
    }

    void SetIsHeaderBarVisible(bool isVisible)
    {
        if (Content is null)
            return;

        // Hiding fades and collapses the content, but this view keeps its full-width strip and
        // would still swallow touches meant for whatever the page draws underneath (a hero header).
        InputTransparent = !isVisible;

        // The whole bar only fades: shrinking every item at once reads as the bar sinking away.
        _ = AnimateVisibility(Content, isVisible, scales: false);
    }

    void SetIsBackButtonVisible(bool isVisible)
    {
        if (_primaryPageActionView is not null)
            _primaryPageActionView.IsEnabled = isVisible;

        UpdatePrimaryActionVisibility();
    }

    void UpdatePrimaryActionVisibility()
    {
        UpdateActionWidth(_primaryPageActionView);
        bool shouldShow = IsBackButtonVisible && _primaryPageActionView.Action is { IsVisible: true };
        _ = AnimateVisibility(_primaryPageActionView, shouldShow);
    }

    void UpdateSecondaryActionVisibility()
    {
        UpdateActionWidth(_secondaryPageActionView);
        bool shouldShow = !(IsWindowsDesktop() && IsTitleBarVisible) && _secondaryPageActionView.Action is { IsVisible: true };

        if (shouldShow && Presentation is NavigationPresentation.Region && IsWindowsDesktop())
        {
            var captionButtonsWidth = GetWindowsCaptionButtonsWidth();
            if (captionButtonsWidth > 0)
            {
                _secondaryPageActionView.Margin = new Thickness(0, 0, captionButtonsWidth, 0);
            }
            else
            {
                // RightInset not available yet (window hasn't rendered); correct the margin
                // on the very next layout pass, which fires before the user sees the button.
                void OnFirstLayout(object? s, EventArgs _)
                {
                    SizeChanged -= OnFirstLayout;
                    var w = GetWindowsCaptionButtonsWidth();
                    if (w > 0)
                        _secondaryPageActionView.Margin = new Thickness(0, 0, w, 0);
                }
                SizeChanged += OnFirstLayout;
            }
        }

        _ = AnimateVisibility(_secondaryPageActionView, shouldShow);
    }

    /// <summary>
    /// Publishes how much room each action takes, so the page title can keep exactly that much
    /// free — no more, and no less for a text action that is wider than an icon.
    /// </summary>
    void PublishActionSlots()
    {
        var primary = SlotFor(_primaryPageActionView, leading: true);
        var secondary = SlotFor(_secondaryPageActionView, leading: false);

        if (primary.Equals(PrimaryActionSlot) && secondary.Equals(SecondaryActionSlot))
            return;

        PrimaryActionSlot = primary;
        SecondaryActionSlot = secondary;

        ActionSlotsChanged?.Invoke();
    }

    double SlotFor(PageActionView view, bool leading)
    {
        if (!view.IsVisible)
            return 0;

        // The arranged frame is the honest answer: it already carries the side margin, the
        // button's own padding and — for a text action — the width the text actually needed.
        var bounds = view.Bounds;
        if (bounds.Width > 0 && Width > 0)
            return Math.Max(0, leading ? bounds.Right : Width - bounds.Left);

        // Before the first layout there is no frame yet. An icon action has a known width, so
        // seed with that rather than let the title start full-width and jump on the next pass.
        return SideMargin + IconButtonWidth;
    }

    void UpdateActionWidth(PageActionView pageActionView) => pageActionView.IconWidth = IconButtonWidth;

    // Glass buttons are 44-point circles whose edge lines up with the page's content, as a
    // UINavigationBar's do; without glass the older, platform-specific slots stay.
    internal double SideMargin => PageActionView.UseGlassHeaderActions
        ? HeaderBarConstants.PageMargin
        : Presentation is NavigationPresentation.Sheet ? HeaderBarConstants.SheetSideMargin : HeaderBarConstants.RegionSideMargin;

    internal double IconButtonWidth => PageActionView.UseGlassHeaderActions
        ? HeaderBarConstants.Height
        : Presentation is NavigationPresentation.Sheet
            ? HeaderBarConstants.SheetButtonWidth
            // Slightly larger for region pages, to accommodate caption buttons on Windows desktop.
            : HeaderBarConstants.RegionButtonWidth;

    void UpdatePresentationSizes()
    {
        // Keep height fixed for consistency; allow width to be measured by content so text-only actions size dynamically.

        if (Content is not Grid buttonGrid)
            return;

        if (Presentation is NavigationPresentation.Sheet)
        {
            buttonGrid.ColumnDefinitions[0].Width = SideMargin;
            buttonGrid.ColumnDefinitions[^1].Width = SideMargin;
        }
        else
        {
            buttonGrid.ColumnDefinitions[0].Width = SideMargin;

            if(IsWindowsDesktop())
            {
                var captionButtonsWidth = GetWindowsCaptionButtonsWidth();
                buttonGrid.ColumnDefinitions[^1].Width = captionButtonsWidth;
            }
            else
            {
                buttonGrid.ColumnDefinitions[^1].Width = SideMargin;
            }
        }

        _primaryPageActionView.HeightRequest = HeaderBarConstants.Height;

        _primaryPageActionView.ButtonPadding = Presentation is NavigationPresentation.Sheet
            ? new Thickness(HeaderBarConstants.SheetButtonPadding)
            : new Thickness(HeaderBarConstants.RegionButtonPadding);

        // No top margin here: NavigationRegion already offsets the whole header bar by
        // SheetTopPadding. Adding it a second time on each action pushed the buttons a full
        // row below the title, which lives in the page content and never got the offset.
        // No shift: the side margin and the button's padding already place a 24-point glyph about 16 points
        // from the edge on both sides, as Material's top app bar does. Both slots used to be shifted right by
        // the padding on Android, which put the back button that much further in and the trailing action
        // that much closer to the edge (the padding is zero on Apple and Windows).
        _primaryPageActionView.Margin = new Thickness(0);

        _secondaryPageActionView.HeightRequest = HeaderBarConstants.Height;

        _secondaryPageActionView.ButtonPadding = Presentation is NavigationPresentation.Sheet
            ? new Thickness(HeaderBarConstants.SheetButtonPadding)
            : new Thickness(HeaderBarConstants.RegionButtonPadding);

        _secondaryPageActionView.Margin = new Thickness(0);


        UpdateSecondaryActionVisibility();

//#if ANDROID
//        _primaryPageActionView.Margin = Presentation is NavigationPresentation.Sheet ? new Thickness(14, 0, 0, 0) : new Thickness(8, 0, 0, 0);
//        _secondaryPageActionView.Margin = Presentation is NavigationPresentation.Sheet ? new Thickness(14, 0, 0, 0) : new Thickness(0, 0, 8, 0);

//        //TESTING
//        _primaryPageActionView.Padding = new Thickness(8);

//        _primaryPageActionView.HeightRequest = 48;


//#endif
    }

    static bool IsWindowsDesktop()
    {
#if WINDOWS
        return DeviceInfo.Current.Idiom == DeviceIdiom.Desktop;
#else
        return false;
#endif
    }

    static double GetWindowsCaptionButtonsWidth()
    {
#if WINDOWS
        try
        {
            if (Application.Current?.Windows.FirstOrDefault() is Window mauiWindow
                && mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window winuiWindow)
            {
                var hWnd = winuiWindow.GetWindowHandle();
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                if (appWindow?.TitleBar is { } titleBar)
                    {
                        var rightInset = titleBar.RightInset;
                        if (rightInset > 0)
                        {
                            // RightInset is in physical screen pixels; divide by the display scale to
                            // get device-independent units (DIPs) that MAUI layout expects.
                            var scale = winuiWindow.Content?.XamlRoot?.RasterizationScale ?? 1.0;
                            var totalDips = rightInset / scale;

                            // RightInset always reserves space for all 3 caption buttons even when
                            // min/max are disabled via OverlappedPresenter (they render as grayed-out
                            // but are still measured). Scale the margin down to only the buttons that
                            // are actually enabled so the secondary action isn't pushed too far left.
                            if (appWindow.Presenter is OverlappedPresenter overlapped)
                            {
                                int visibleCount = 1; // close is always present
                                if (overlapped.IsMinimizable || overlapped.IsMaximizable) visibleCount += 2;
                                if (visibleCount < 3)
                                    return Math.Round(totalDips * visibleCount / 3.0);
                            }

                            return totalDips;
                        }
                    }
            }
        }
        catch { }
        return 0; // 0 signals "not ready yet" – caller should defer until SizeChanged
#else
        return 0;
#endif
    }

    public HeaderBarView()
    {
        _primaryPageActionView = new PageActionView
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = HeaderBarConstants.Height,
            IconWidth = HeaderBarConstants.SheetButtonWidth,
            ButtonPadding = HeaderBarConstants.SheetButtonPadding,
            Opacity = 0,
            IsVisible = false
        };

        _secondaryPageActionView = new PageActionView
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = HeaderBarConstants.Height,
            IconWidth = HeaderBarConstants.SheetButtonWidth,
            ButtonPadding = HeaderBarConstants.SheetButtonPadding,
            Opacity = 0,
            IsVisible = false
        };

        // An action that hides or shows itself in place keeps its instance, so the bindable
        // properties above never fire; the view tells us directly.
        _primaryPageActionView.VisibilityChanged += UpdatePrimaryActionVisibility;
        _secondaryPageActionView.VisibilityChanged += UpdateSecondaryActionVisibility;

        var buttonGrid = new Grid()
        {
            //BackgroundColor = Colors.Pink.WithAlpha(0.5f),
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = new GridLength(0) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(0) }
            ],
            VerticalOptions = LayoutOptions.Center,
            ColumnSpacing = 0
        };

        //Debug rainbow
        //buttonGrid.Add(new Border { BackgroundColor = Colors.LimeGreen.WithAlpha(0.5f) }, 0);
        //buttonGrid.Add(new Border { BackgroundColor = Colors.Purple.WithAlpha(0.5f) }, 2);
        //buttonGrid.Add(new Border { BackgroundColor = Colors.LimeGreen.WithAlpha(0.5f) }, 4);

        buttonGrid.Add(_primaryPageActionView, 1);
        buttonGrid.Add(_secondaryPageActionView, 3);

        Content = buttonGrid;

        _primaryPageActionView.SizeChanged += (_, _) => PublishActionSlots();
        _secondaryPageActionView.SizeChanged += (_, _) => PublishActionSlots();
        SizeChanged += (_, _) => PublishActionSlots();

        UpdatePresentationSizes();
    }

    readonly Dictionary<View, int> _visibilityVersions = [];

    // A navigation bar item fades and grows in, and fades and shrinks out. Liquid Glass cannot fade
    // through its alpha (it turns flat grey), so glass materializes and dissolves instead.
    async Task AnimateVisibility(View? target, bool show, bool scales = true)
    {
        if (target is null)
            return;

        var version = _visibilityVersions[target] = _visibilityVersions.GetValueOrDefault(target) + 1;
        var duration = PageActionView.TransitionDuration;
        var scale = scales ? PageActionView.TransitionScale : 1;
        // A hide still fading out would otherwise run on under a show that sets opacity and scale
        // directly (the glass path), and leave the action visible but transparent (#421).
        target.CancelAnimations();

        if (show)
        {
            if (!target.IsVisible || target.Opacity == 0)
                target.Scale = scale;

            target.IsVisible = true;
            PublishActionSlots();

#if IOS || MACCATALYST
            if (GlassAppearance.Applies(target))
            {
                target.Opacity = 1;
                await Task.WhenAll(
                    GlassAppearance.AnimateAsync(target, show: true, duration),
                    target.ScaleToAsync(1, duration, PageActionView.TransitionEasing));
                return;
            }

            // Glass UIKit has not built yet (the page is still arriving) draws a flat grey stand-in
            // for its first frames. Keep the action hidden for that frame, then let it
            // materialize like any other.
            if (PageActionView.UseGlassHeaderActions)
            {
                // One frame: long enough for UIKit to build it, short enough that both actions
                // of an arriving page start together.
                await Task.Delay(16);

                if (_visibilityVersions[target] != version)
                    return;

                target.Opacity = 1;
                if (GlassAppearance.Applies(target))
                {
                    await Task.WhenAll(
                        GlassAppearance.AnimateAsync(target, show: true, duration),
                        target.ScaleToAsync(1, duration, PageActionView.TransitionEasing));
                }
                else
                {
                    target.Scale = 1;
                }
                return;
            }
#endif
            await Task.WhenAll(
                target.FadeToAsync(1, duration, PageActionView.TransitionEasing),
                target.ScaleToAsync(1, duration, PageActionView.TransitionEasing));
        }
        else
        {
            // Leaving is quicker than arriving, as with a navigation bar's items: gone before the
            // page that took them has slid away.
            var outDuration = PageActionView.RemovalDuration;

#if IOS || MACCATALYST
            if (GlassAppearance.Applies(target))
            {
                await Task.WhenAll(
                    GlassAppearance.AnimateAsync(target, show: false, outDuration),
                    target.ScaleToAsync(scale, outDuration, PageActionView.TransitionEasing));
            }
            else
#endif
            {
                await Task.WhenAll(
                    target.FadeToAsync(0, outDuration, PageActionView.TransitionEasing),
                    target.ScaleToAsync(scale, outDuration, PageActionView.TransitionEasing));
            }

            // Shown again while it went away: leave it.
            if (_visibilityVersions[target] != version)
                return;

            target.Opacity = 0;
            target.IsVisible = false;
            target.Scale = 1;
            PublishActionSlots();
        }
    }
}
