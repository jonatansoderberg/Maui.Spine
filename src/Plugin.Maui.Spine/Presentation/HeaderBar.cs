using CommunityToolkit.Mvvm.Input;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Presentation;

#if WINDOWS
using Microsoft.UI.Windowing;
using Microsoft.Maui.Platform;
#endif

namespace Plugin.Maui.Spine.Presentation;

internal class HeaderBar : Microsoft.Maui.Controls.ContentView
{
    public static readonly BindableProperty IsBackButtonVisibleProperty = BindableProperty.Create(
        nameof(IsBackButtonVisible), typeof(bool), typeof(HeaderBar), true, propertyChanged: OnIsBackButtonVisibleChanged);

    public static readonly BindableProperty IsHeaderBarVisibleProperty = BindableProperty.Create(
        nameof(IsHeaderBarVisible), typeof(bool), typeof(HeaderBar), true, propertyChanged: OnIsHeaderBarVisibleChanged);

    public static readonly BindableProperty IsTitleBarVisibleProperty = BindableProperty.Create(
        nameof(IsTitleBarVisible), typeof(bool), typeof(HeaderBar), false, propertyChanged: OnIsTitleBarVisibleChanged);

    public static readonly BindableProperty CloseCommandProperty = BindableProperty.Create(
        nameof(CloseCommand), typeof(IAsyncRelayCommand), typeof(HeaderBar), default(IAsyncRelayCommand), propertyChanged: OnCloseCommandChanged);

    public static readonly BindableProperty BackCommandProperty = BindableProperty.Create(
        nameof(BackCommand), typeof(IAsyncRelayCommand), typeof(HeaderBar), default(IAsyncRelayCommand), propertyChanged: OnBackCommandChanged);

    public static readonly BindableProperty DefaultPageActionProperty = BindableProperty.Create(
        nameof(DefaultPageAction), typeof(PageAction), typeof(HeaderBar), default, propertyChanged: DefaultPageActionChanged);

    public static readonly BindableProperty PrimaryPageActionProperty = BindableProperty.Create(
        nameof(PrimaryPageAction), typeof(PageAction), typeof(HeaderBar), default, propertyChanged: PrimaryPageActionChanged);

    public static readonly BindableProperty PresentationProperty = BindableProperty.Create(
        nameof(Presentation),
        typeof(NavigationPresentation),
        typeof(HeaderBar),
        defaultValue: NavigationPresentation.RegionPresentation,
        propertyChanged: OnPresentationChanged);

    readonly PageActionView _primaryPageActionView;
    readonly PageActionView _secondaryPageActionView;

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
        var view = (HeaderBar)bindable;
        view._secondaryPageActionView.Action = (PageAction?)newValue;
        view.UpdateSecondaryActionVisibility();
    }

    static void PrimaryPageActionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBar)bindable;
        view._primaryPageActionView.Action = (PageAction?)newValue;
        view.UpdatePrimaryActionVisibility();
    }

    static void OnIsHeaderBarVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBar)bindable;
        view.SetIsHeaderBarVisible((bool)newValue);
    }

    static void OnIsBackButtonVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBar)bindable;
        view.SetIsBackButtonVisible((bool)newValue);
    }

    static void OnIsTitleBarVisibleChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBar)bindable;
        view.UpdateSecondaryActionVisibility();
    }

    static void OnPresentationChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (HeaderBar)bindable;
        view.UpdatePresentationSizes();
    }

    void SetIsHeaderBarVisible(bool isVisible)
    {
        if (Content is null)
            return;

        _ = AnimateVisibility(Content, isVisible);
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

    void UpdateActionWidth(PageActionView pageActionView)
    {
        pageActionView.WidthRequest = string.IsNullOrEmpty(pageActionView?.Action?.Svg)
            ? HeaderBarConstants.Auto // Allow to size to text content
            : Presentation is NavigationPresentation.Sheet
                ? HeaderBarConstants.SheetButtonWidth // Fixed size for icon-only actions in sheet presentation
                : HeaderBarConstants.RegionButtonWidth; // Slightly larger fixed size for icon-only actions in region presentation to accommodate potential caption buttons on Windows desktop
    }

    void UpdatePresentationSizes()
    {
        // Keep height fixed for consistency; allow width to be measured by content so text-only actions size dynamically.

        var buttonGrid = Content as Grid;

        if (Presentation is NavigationPresentation.Sheet)
        {
            buttonGrid.ColumnDefinitions[0].Width = HeaderBarConstants.SheetSideMargin;
            buttonGrid.ColumnDefinitions[^1].Width = HeaderBarConstants.SheetSideMargin;
        }
        else
        {
            buttonGrid.ColumnDefinitions[0].Width = HeaderBarConstants.RegionSideMargin;

            if(IsWindowsDesktop())
            {
                var captionButtonsWidth = GetWindowsCaptionButtonsWidth();
                buttonGrid.ColumnDefinitions[^1].Width = captionButtonsWidth;
            }
            else
            {
                buttonGrid.ColumnDefinitions[^1].Width = HeaderBarConstants.RegionSideMargin;
            }
        }

        _primaryPageActionView.HeightRequest = HeaderBarConstants.Height;

        _primaryPageActionView.Padding = Presentation is NavigationPresentation.Sheet
            ? new Thickness(HeaderBarConstants.SheetButtonPadding)
            : new Thickness(HeaderBarConstants.RegionButtonPadding);

        //Test
        _primaryPageActionView.Margin = new Thickness(HeaderBarConstants.SheetButtonPadding, 0, -HeaderBarConstants.SheetButtonPadding, 0);

        _secondaryPageActionView.HeightRequest = HeaderBarConstants.Height;

        _secondaryPageActionView.Padding = Presentation is NavigationPresentation.Sheet
            ? new Thickness(HeaderBarConstants.SheetButtonPadding)
            : new Thickness(HeaderBarConstants.RegionButtonPadding);

        _secondaryPageActionView.Margin = new Thickness(HeaderBarConstants.SheetButtonPadding, 0, -HeaderBarConstants.SheetButtonPadding, 0);


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

    public HeaderBar()
    {
        _primaryPageActionView = new PageActionView
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = HeaderBarConstants.Height,
            WidthRequest = HeaderBarConstants.SheetButtonWidth,
            Padding = HeaderBarConstants.SheetButtonPadding,
            Opacity = 0,
            IsVisible = false
        };

        _secondaryPageActionView = new PageActionView
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = HeaderBarConstants.Height,
            WidthRequest = HeaderBarConstants.SheetButtonWidth,
            Padding = HeaderBarConstants.SheetButtonPadding,
            Opacity = 0,
            IsVisible = false
        };

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

        UpdatePresentationSizes();
    }

    async Task AnimateVisibility(View? target, bool show)
    {
        if (target is null)
            return;

        if (show)
        {
            target.IsVisible = true;
            await target.FadeToAsync(1, HeaderBarConstants.FadeInDuration, Easing.SinIn);
        }
        else
        {
            await target.FadeToAsync(0, HeaderBarConstants.FadeOutDuration, Easing.SinOut);
            target.IsVisible = false;
        }
    }
}
