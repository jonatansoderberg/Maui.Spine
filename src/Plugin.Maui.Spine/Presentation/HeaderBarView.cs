using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
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
            foreach (var view in bar.ActionViews)
                view.Foreground = (Color?)v;
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
            foreach (var view in bar.ActionViews)
                view.Glass = (HeaderBarGlass)v;
            bar.UpdateTrailingGroup();
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
            foreach (var view in bar.ActionViews)
                view.OverContent = (bool)v;
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
            foreach (var view in bar.ActionViews)
                view.BackgroundProgress = (double)v;
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

    // The trailing actions after the secondary one, innermost first, left of it in _trailing.
    readonly HorizontalStackLayout _trailing;
    readonly List<PageActionView> _moreTrailing = [];

    // Holds _trailing; with GroupHeaderActions it is the glass capsule the grouped actions share.
    readonly Border _trailingGroup;

    IEnumerable<PageActionView> ActionViews => [_primaryPageActionView, _secondaryPageActionView, .. _moreTrailing];

    /// <summary>The page's further trailing actions, from the edge inward (see NavigationRegionViewModel.MoreSecondaryPageActions).</summary>
    public static readonly BindableProperty MoreTrailingActionsProperty = BindableProperty.Create(
        nameof(MoreTrailingActions), typeof(IReadOnlyList<PageAction>), typeof(HeaderBarView), null,
        propertyChanged: static (b, _, v) => ((HeaderBarView)b).SyncMoreTrailing((IReadOnlyList<PageAction>?)v ?? []));

    public IReadOnlyList<PageAction>? MoreTrailingActions
    {
        get => (IReadOnlyList<PageAction>?)GetValue(MoreTrailingActionsProperty);
        set => SetValue(MoreTrailingActionsProperty, value);
    }

    /// <summary>
    /// Keeps one view per further trailing action: a view whose action stays keeps its place, a new one
    /// materializes like the secondary action, and one whose action left fades out first.
    /// </summary>
    void SyncMoreTrailing(IReadOnlyList<PageAction> actions)
    {
        foreach (var view in _moreTrailing.Where(v => !actions.Contains(v.Action!)).ToList())
        {
            _moreTrailing.Remove(view);
            _ = RemoveTrailingAsync(view);
        }

        var added = new List<PageActionView>();
        for (var i = 0; i < actions.Count; i++)
        {
            var view = _moreTrailing.FirstOrDefault(v => ReferenceEquals(v.Action, actions[i]));
            if (view is null)
                added.Add(view = NewTrailingView(actions[i]));
            _moreTrailing.Remove(view);
            _moreTrailing.Insert(i, view);
        }

        // _trailing holds the views still fading out first, then the further actions innermost first, then
        // the secondary action at the edge.
        IView[] desired = [.. Enumerable.Reverse(_moreTrailing), _secondaryPageActionView];
        var leaving = _trailing.Children.Count(c => !desired.Contains(c));
        for (var k = 0; k < desired.Length; k++)
        {
            if (_trailing.Children.Count > leaving + k && ReferenceEquals(_trailing.Children[leaving + k], desired[k]))
                continue;
            _trailing.Children.Remove(desired[k]);
            _trailing.Children.Insert(leaving + k, desired[k]);
        }

        // Only the new ones: showing one that is already there would play its arrival again.
        foreach (var view in added)
            UpdateMoreTrailingVisibility(view);
        UpdateTrailingGroup();
        PublishActionSlots();
    }

    PageActionView NewTrailingView(PageAction action)
    {
        var view = new PageActionView
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = ButtonHeight,
            IconWidth = IconButtonWidth,
            ButtonPadding = _secondaryPageActionView.ButtonPadding,
            Foreground = Foreground,
            Glass = Glass,
            OverContent = OverContent,
            BackgroundProgress = BackgroundProgress,
            Opacity = 0,
            IsVisible = false,
        };
        view.Action = action;
        view.VisibilityChanged += () => UpdateMoreTrailingVisibility(view);
        view.SizeChanged += (_, _) => PublishActionSlots();
        return view;
    }

    void UpdateMoreTrailingVisibility(PageActionView view)
    {
        UpdateTrailingGroup();
        _ = AnimateVisibility(view, ShowsTrailing(view), scales: !_searchInButtonPlace);
    }

    // Whether a trailing action is, or is on its way to being, on screen: the group follows where the
    // actions are going, not the frame they are fading through.
    bool ShowsTrailing(PageActionView view) =>
        (ReferenceEquals(view, _secondaryPageActionView) || _moreTrailing.Contains(view))
        && !(IsWindowsDesktop() && IsTitleBarVisible)
        && view.Action is { IsVisible: true };

    async Task RemoveTrailingAsync(PageActionView view)
    {
        await AnimateVisibility(view, show: false);
        if (!_moreTrailing.Contains(view))
            _trailing.Children.Remove(view);
        PublishActionSlots();
    }
    SearchField? _searchField;

    // Whether the field in the bar is there only for the search that is going on (PageSearch.ShownForSearch): the
    // page's own button started it, and the field takes that button's place, as Finder's search button opens into
    // its field. Such a field leaves the title alone and opens from, and closes to, the button's size.
    bool _searchInButtonPlace;

    // How far such a field has opened, from 0 (the button's size) to 1 (its width).
    double _searchOpen = 1;

    const string SearchOpenAnimation = "SpineSearchOpen";
    const uint SearchOpenDuration = 250;

    /// <summary>
    /// The page's search field when it goes at the trailing end of the bar (iPad, Mac Catalyst),
    /// left of the trailing action; <see langword="null"/> for none.
    /// </summary>
    public PageSearch? Search
    {
        get => _searchField?.Search;
        set
        {
            if (ReferenceEquals(value, Search) || (value is null && _searchField is null))
                return;

            if (_searchField is null)
            {
                _searchField = new SearchField
                {
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Center,
                };
                ApplySearchHeight();
                _searchField.SizeChanged += (_, _) => PublishActionSlots();
                ((Grid)Content).Add(_searchField, 2);
            }

            if (Search is { } old)
                old.PropertyChanged -= OnSearchWidthChanged;
            if (value is not null)
                value.PropertyChanged += OnSearchWidthChanged;

            this.AbortAnimation(SearchOpenAnimation);

            // The field that was there only for a search closes to the button coming back before it goes.
            if (value is null && _searchInButtonPlace && _searchField.IsVisible && CanAnimate(this))
            {
                AnimateSearchOpen(0, () =>
                {
                    _searchInButtonPlace = false;
                    _searchField.Search = null;
                    _searchField.IsVisible = false;
                    PublishActionSlots();
                });
                return;
            }

            _searchInButtonPlace = value is { ShownForSearch: true };
            // In the button's place the field runs on over the trailing actions' column, so it ends where the
            // button did whether the button has gone yet or not.
            Grid.SetColumnSpan(_searchField, _searchInButtonPlace ? 2 : 1);
            _searchField.Search = value;
            _searchField.IsVisible = value is not null;
            ApplySearchGap();

            if (_searchInButtonPlace && CanAnimate(this))
            {
                _searchOpen = 0;
                ApplySearchWidth();
                AnimateSearchOpen(1, null);
            }
            else
            {
                _searchOpen = 1;
                _searchField.Opacity = 1;
                ApplySearchWidth();
            }

            PublishActionSlots();
        }
    }

    void OnSearchWidthChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PageSearch.Width))
            ApplySearchWidth();
    }

    void AnimateSearchOpen(double to, Action? finished)
    {
        new Animation(open =>
        {
            _searchOpen = open;
            ApplySearchWidth();
        }, _searchOpen, to).Commit(this, SearchOpenAnimation, length: SearchOpenDuration, easing: Easing.CubicOut,
            finished: (_, cancelled) =>
            {
                if (!cancelled)
                    finished?.Invoke();
            });
    }

    /// <summary>
    /// How far the bar has given way to the page's search, 0 to 1: its buttons slide up out of the
    /// way and fade, as a navigation bar does under a <c>UISearchController</c>.
    /// </summary>
    public double SearchProgress
    {
        get;
        set
        {
            // Only on a change: the region sets it with every change of the header's page.
            if (field == value || Content is null)
                return;

            field = value;
            Content.TranslationY = -HeaderBarConstants.BarHeight * value;
            UpdateInputTransparent();

            // A hidden bar's own fade owns the opacity then.
            if (IsHeaderBarVisible)
                Content.Opacity = 1 - value;
        }
    }

    // As tall as the bar's buttons: in a Mac window's title bar row they are smaller than the bar, and a taller field
    // made the row grow as it came, which moved the buttons, and stood taller than the button it opens from.
    void ApplySearchHeight()
    {
        if (_searchField is null)
            return;

        var height = LeadingInset > 0 ? ButtonHeight : HeaderBarConstants.Height;
        _searchField.HeightRequest = height;
        _searchField.FieldHeight = height;
    }

    // As wide as the page asks (PageSearch.Width) or a toolbar's search field is, but never more than two fifths of
    // the bar. A field in the button's place is between the button's size and that, by how far it has opened, and
    // comes in over the first part of the way.
    void ApplySearchWidth()
    {
        if (_searchField is null)
            return;

        var asked = _searchField.Search is { Width: > 0 } search ? search.Width : SearchField.TrailingWidth;
        var full = Width > 0 ? Math.Min(asked, Width * 0.4) : asked;
        if (!_searchInButtonPlace)
        {
            _searchField.WidthRequest = full;
            return;
        }

        var closed = Math.Min(full, IconButtonWidth);
        _searchField.WidthRequest = closed + (full - closed) * _searchOpen;
        _searchField.Opacity = Math.Clamp(_searchOpen / 0.4, 0, 1);
    }

    void ApplySearchGap()
    {
        if (_searchField is not null)
            _searchField.Margin = new Thickness(0, 0, !_searchInButtonPlace && _secondaryPageActionView.IsVisible ? HeaderBarConstants.TitleActionGap : 0, 0);
    }

    /// <summary>
    /// Room kept free at the leading edge for the Mac window buttons when the bar is in the title bar row
    /// (<c>SpineOptions.MacOS.UnifiedTitleBar</c>), 0 otherwise. The leading action and the title start after it.
    /// </summary>
    public double LeadingInset
    {
        get;
        set
        {
            if (Math.Abs(field - value) < 0.5) return;
            field = value;
            UpdatePresentationSizes();
            ApplySearchHeight();
            PublishActionSlots();
        }
    }

    // A Mac toolbar's first item starts this far after the zoom button.
    const double WindowButtonsGap = 12;

    double LeadingColumn => LeadingInset > 0 ? LeadingInset + WindowButtonsGap : SideMargin;

    // A Mac toolbar's last item ends this far from the window's edge, as its first starts after the window
    // buttons: closer than the page's margin, which the bar's buttons keep everywhere else.
    const double MacToolbarEdge = WindowButtonsGap;

    double TrailingColumn => LeadingInset > 0 && PlatformValue.IsMacIdiom ? MacToolbarEdge : SideMargin;

    // In the title bar row the buttons are a toolbar's, smaller than the row.
    double ButtonHeight => LeadingInset > 0 ? HeaderBarConstants.CompactButtonHeight : HeaderBarConstants.Height;

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

    /// <summary>
    /// Whether a lightbox has faded the bar out. The bar takes touches only while it is neither hidden
    /// by its page nor faded out, so either one alone keeps it from swallowing touches meant for the page.
    /// </summary>
    public bool IsChromeHidden
    {
        get => _isChromeHidden;
        set
        {
            _isChromeHidden = value;
            UpdateInputTransparent();
        }
    }

    private bool _isChromeHidden;

    // A bar that has given way to a search lies over the field that took its place.
    void UpdateInputTransparent() => InputTransparent = _isChromeHidden || !IsHeaderBarVisible || SearchProgress > 0;

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
        view.UpdatePrimaryActionVisibility();
        view.UpdateSecondaryActionVisibility();
        foreach (var more in view._moreTrailing)
            view.UpdateMoreTrailingVisibility(more);
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
        UpdateInputTransparent();

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
        // The window's title bar shows a region page's leading action (Back) when it is visible; a second one here would repeat it.
        bool shouldShow = IsBackButtonVisible
            && !(IsWindowsDesktop() && IsTitleBarVisible && Presentation is NavigationPresentation.Region)
            && _primaryPageActionView.Action is { IsVisible: true };
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

        UpdateTrailingGroup();
        // The button a search field opens from and closes to keeps its size: it changes places with the field, and
        // one that shrank as it went, or grew as it came back, read as a smaller button beside the field.
        _ = AnimateVisibility(_secondaryPageActionView, shouldShow, scales: !_searchInButtonPlace);
    }

    /// <summary>
    /// Publishes how much room each action takes, so the page title can keep exactly that much
    /// free — no more, and no less for a text action that is wider than an icon.
    /// </summary>
    void PublishActionSlots()
    {
        var primary = Math.Max(SlotFor(_primaryPageActionView, leading: true), LeadingInset > 0 ? LeadingColumn : 0);
        var secondary = TrailingSlot();

        // The title keeps clear of a search field at the trailing end too. Not of one in the button's place: the
        // title stays where it stood beside the button, through the button going and the field opening.
        if (_searchInButtonPlace)
            secondary = Math.Max(secondary, TrailingColumn + IconButtonWidth);
        else if (_searchField is { IsVisible: true, Bounds.Width: > 0 } field && Width > 0)
            secondary = Math.Max(secondary, Width - field.Bounds.Left);

        if (primary.Equals(PrimaryActionSlot) && secondary.Equals(SecondaryActionSlot))
            return;

        PrimaryActionSlot = primary;
        SecondaryActionSlot = secondary;

        ActionSlotsChanged?.Invoke();
    }

    // Actions on their way out. Their room is given back when they start to leave, not when the fade ends: the
    // title of a page coming back then travels to where it will rest, rather than stopping beside a back button
    // that is going away and moving again once it has gone.
    readonly HashSet<View> _leaving = [];

    double SlotFor(PageActionView view, bool leading)
    {
        if (!view.IsVisible || _leaving.Contains(view))
            return 0;

        // The arranged frame is the honest answer: it already carries the side margin, the
        // button's own padding and — for a text action — the width the text actually needed.
        var bounds = view.Bounds;
        if (bounds.Width > 0 && Width > 0)
            return Math.Max(0, leading ? bounds.Right : Width - bounds.Left);

        // Before the first layout there is no frame yet. An icon action has a known width, so
        // seed with that rather than let the title start full-width and jump on the next pass.
        return (leading ? LeadingColumn : TrailingColumn) + IconButtonWidth;
    }

    /// <summary>How far the trailing actions reach in from the right edge: the leftmost visible one's left edge.</summary>
    double TrailingSlot()
    {
        var visible = _trailing.Children.OfType<PageActionView>().Where(v => v.IsVisible && !_leaving.Contains(v)).ToList();
        if (visible.Count == 0)
            return 0;
        if (Width > 0 && _trailing.Width > 0 && visible.All(v => v.Width > 0))
            return Math.Max(0, Width - (_trailingGroup.X + _trailing.X + visible.Min(v => v.X)));
        return TrailingColumn + visible.Count * IconButtonWidth;
    }

    void UpdateActionWidth(PageActionView pageActionView) => pageActionView.IconWidth = IconButtonWidth;

    static bool GroupsActions =>
        PageActionView.UseGlassHeaderActions
        && IPlatformApplication.Current?.Services.GetService<SpineOptions>()?.Apple.GroupHeaderActions == true;

    /// <summary>
    /// Two or more trailing actions share one glass capsule, as the bar button items of one group do in a
    /// UINavigationBar on iOS 26, and each shows its own glass only while pressed. A prominent action (a
    /// Confirm, a selected action) stays in the capsule and shows its prominent glass there.
    /// </summary>
    void UpdateTrailingGroup()
    {
        if (_trailingGroup is null)
            return;

        var showing = _trailing.Children.OfType<PageActionView>().Where(ShowsTrailing).ToList();
        var grouped = GroupsActions && Glass != HeaderBarGlass.Transient && showing.Count > 1;

        foreach (var view in _trailing.Children.OfType<PageActionView>())
            view.InGroup = grouped;

        Material.SetPreset(_trailingGroup, !grouped ? MaterialPreset.None
            : Glass == HeaderBarGlass.Clear ? MaterialPreset.GlassClear
            : MaterialPreset.GlassRegular);
        // The Border clips its content to its shape. Without glass the group is only a container: a capsule there cut a
        // lone trailing button's square hover into a circle (Windows sheets: a round X beside a square leading button).
        _trailingGroup.StrokeShape = PageActionView.UseGlassHeaderActions
            ? new RoundRectangle { CornerRadius = ButtonHeight / 2 }
            : new Microsoft.Maui.Controls.Shapes.Rectangle();

        // Transient glass buttons have no shape at rest, so the gap between their circles read as a wide gap
        // between bare icons; they touch instead, as the buttons in one group of a UINavigationBar do. On Windows a
        // region page's actions touch as the caption buttons beside them do.
        _trailing.Spacing = grouped
            || PageActionView.UseGlassHeaderActions && Glass == HeaderBarGlass.Transient
            || IsWindowsDesktop() && Presentation is NavigationPresentation.Region
            ? 0
            : HeaderBarConstants.TitleActionGap;
    }

    // Glass buttons are 44-point circles whose edge lines up with the page's content, as a
    // UINavigationBar's do; without glass the older, platform-specific slots stay.
    internal double SideMargin => PageActionView.UseGlassHeaderActions
        ? HeaderBarConstants.PageMargin
        : Presentation is NavigationPresentation.Sheet ? HeaderBarConstants.SheetSideMargin : HeaderBarConstants.RegionSideMargin;

    internal double IconButtonWidth => PageActionView.UseGlassHeaderActions
        ? ButtonHeight
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
            buttonGrid.ColumnDefinitions[0].Width = LeadingColumn;

            if(IsWindowsDesktop())
            {
                var captionButtonsWidth = GetWindowsCaptionButtonsWidth();
                buttonGrid.ColumnDefinitions[^1].Width = captionButtonsWidth;
            }
            else
            {
                buttonGrid.ColumnDefinitions[^1].Width = TrailingColumn;
            }
        }

        _primaryPageActionView.HeightRequest = ButtonHeight;
        UpdateActionWidth(_primaryPageActionView);

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

        _secondaryPageActionView.HeightRequest = ButtonHeight;
        UpdateActionWidth(_secondaryPageActionView);

        _secondaryPageActionView.ButtonPadding = Presentation is NavigationPresentation.Sheet
            ? new Thickness(HeaderBarConstants.SheetButtonPadding)
            : new Thickness(HeaderBarConstants.RegionButtonPadding);

        _secondaryPageActionView.Margin = new Thickness(0);

        foreach (var more in _moreTrailing)
        {
            more.HeightRequest = ButtonHeight;
            more.IconWidth = IconButtonWidth;
            more.ButtonPadding = _secondaryPageActionView.ButtonPadding;
        }


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
        _trailing = new HorizontalStackLayout
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Spacing = HeaderBarConstants.TitleActionGap,
            Children = { _secondaryPageActionView },
        };
        _trailingGroup = new Border
        {
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 0,
            Padding = 0,
            BackgroundColor = Colors.Transparent,
            Content = _trailing,
        };
        UpdateTrailingGroup();
        _trailing.SizeChanged += (_, _) => PublishActionSlots();
        buttonGrid.Add(_trailingGroup, 3);

        Content = buttonGrid;

        _primaryPageActionView.SizeChanged += (_, _) => PublishActionSlots();
        _secondaryPageActionView.SizeChanged += (_, _) => PublishActionSlots();
        _secondaryPageActionView.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsVisible))
                ApplySearchGap();
        };
        SizeChanged += (_, _) =>
        {
            ApplySearchWidth();
            PublishActionSlots();
        };

        UpdatePresentationSizes();
    }

    readonly Dictionary<View, int> _visibilityVersions = [];

    // An animation runs on the animation manager of the window the view is in. A view that is in none
    // yet (a title bar's action set before the title bar is shown) takes its end state at once;
    // FadeToAsync would throw, unobserved, from the discarded task.

#if IOS || MACCATALYST
    const string RenderServerAnimationKey = "Spine.HeaderBar.Visibility";

    /// <summary>
    /// Fades and scales <paramref name="target"/>'s platform view with Core Animation instead of the MAUI animations.
    /// </summary>
    /// <remarks>
    /// A MAUI animation is stepped on the main thread, and so is the continuation of an awaited delay. After a
    /// click that navigates, the main thread is taken for the better part of a second: by building the page and, in
    /// an active Mac window, by UIKit's focus system mapping the whole view tree. The page transition is not held up
    /// by that, as the render server runs it, but an action stepped by MAUI stood still and then jumped to its end
    /// when the page had long arrived. Handed to Core Animation here, the action keeps time with the page.
    /// <para>
    /// The animation is explicit: it only changes what is drawn, from the given values (or where the view is drawn
    /// now) to the given ones, and leaves the view's own alpha and transform to MAUI. The caller sets Opacity and
    /// Scale to where the view is to rest; with <paramref name="hold"/> the animation stays at its end until
    /// <see cref="EndRenderServerAnimation"/>.
    /// </para>
    /// </remarks>
    static void RenderServerAnimate(View target, double? fromOpacity, double opacity, double? fromScale, double scale, uint duration, bool hold)
    {
        if (PlatformViewOf(target)?.Layer is not { } layer)
            return;

        var drawn = layer.PresentationLayer ?? layer;
        var drawnScale = (drawn.ValueForKeyPath(new Foundation.NSString("transform.scale")) as Foundation.NSNumber)?.DoubleValue ?? 1;

        var fade = CoreAnimation.CABasicAnimation.FromKeyPath("opacity");
        fade.From = Foundation.NSNumber.FromDouble(fromOpacity ?? drawn.Opacity);
        fade.To = Foundation.NSNumber.FromDouble(opacity);

        var grow = CoreAnimation.CABasicAnimation.FromKeyPath("transform.scale");
        grow.From = Foundation.NSNumber.FromDouble(fromScale ?? drawnScale);
        grow.To = Foundation.NSNumber.FromDouble(scale);

        var both = new CoreAnimation.CAAnimationGroup
        {
            Animations = [fade, grow],
            Duration = duration / 1000.0,
            TimingFunction = CoreAnimation.CAMediaTimingFunction.FromName(CoreAnimation.CAMediaTimingFunction.EaseOut),
            RemovedOnCompletion = !hold,
        };
        if (hold)
            both.FillMode = CoreAnimation.CAFillMode.Forwards;

        layer.AddAnimation(both, RenderServerAnimationKey);
    }

    static void EndRenderServerAnimation(View target) =>
        PlatformViewOf(target)?.Layer.RemoveAnimation(RenderServerAnimationKey);

    // The view MAUI puts opacity and transforms on: the wrapper when the view has one.
    static UIKit.UIView? PlatformViewOf(View target) =>
        target.Handler is Microsoft.Maui.IPlatformViewHandler handler ? handler.ContainerView ?? handler.PlatformView : null;
#endif

    static bool CanAnimate(Element view)
    {
        for (Element? element = view; element is not null; element = element.Parent)
        {
            if (element.Handler?.MauiContext is not null)
                return true;
        }

        return false;
    }

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
            var fresh = !target.IsVisible || target.Opacity == 0;
            if (fresh)
                target.Scale = scale;

            target.IsVisible = true;
            _leaving.Remove(target);
            PublishActionSlots();

#if IOS || MACCATALYST
            // A hide that was overtaken by this show left its animation held at its end (see below), and the action
            // undrawn for good under it: a glass action on the Mac never appeared (its views were there, visible and
            // taking clicks). Only the render-server show below puts a new animation in its place, from where the
            // action is drawn now; every other way of showing lets go of it here.
            var handsOver = CanAnimate(target) && !GlassAppearance.Applies(target)
                && (!PageActionView.UseGlassHeaderActions || Glass == HeaderBarGlass.Transient);
            if (!handsOver)
                EndRenderServerAnimation(target);
#endif

            if (!CanAnimate(target))
            {
                target.Opacity = 1;
                target.Scale = 1;
                return;
            }

#if IOS || MACCATALYST
            if (GlassAppearance.Applies(target))
            {
                target.Opacity = 1;
                await Task.WhenAll(
                    GlassAppearance.AnimateAsync(target, show: true, duration),
                    target.ScaleToAsync(1, duration, PageActionView.TransitionEasing));
                return;
            }

            // No glass at rest (none at all, or only while pressed): nothing to wait for, and Core Animation
            // does the fade. See RenderServerAnimate for why not the MAUI animations.
            if (!PageActionView.UseGlassHeaderActions || Glass == HeaderBarGlass.Transient)
            {
                target.Opacity = 1;
                target.Scale = 1;
                RenderServerAnimate(target, fresh ? 0 : null, 1, fresh ? scale : null, 1, duration, hold: false);
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

            _leaving.Add(target);
            PublishActionSlots();

#if IOS || MACCATALYST
            if (GlassAppearance.Applies(target))
            {
                // UIKit reports a glass effect's dissolve as finished when the system's own animation is, which
                // on a Mac was measured at up to a second after the duration asked for, with the action in plain
                // sight all the while. The action is gone when its time is up, wherever the glass has got to.
                await Task.WhenAny(
                    Task.WhenAll(
                        GlassAppearance.AnimateAsync(target, show: false, outDuration),
                        target.ScaleToAsync(scale, outDuration, PageActionView.TransitionEasing)),
                    Task.Delay((int)outDuration + 34));
            }
            else if (CanAnimate(target))
            {
                // Held at its end, so the action stays gone until this method gets to hide it for good.
                RenderServerAnimate(target, null, 0, null, scale, outDuration, hold: true);
                await Task.Delay((int)outDuration);
            }
#else
            if (CanAnimate(target))
            {
                await Task.WhenAll(
                    target.FadeToAsync(0, outDuration, PageActionView.TransitionEasing),
                    target.ScaleToAsync(scale, outDuration, PageActionView.TransitionEasing));
            }
#endif

            // Shown again while it went away: leave it.
            if (_visibilityVersions[target] != version)
                return;

            target.Opacity = 0;
            target.IsVisible = false;
            target.Scale = 1;
#if IOS || MACCATALYST
            EndRenderServerAnimation(target);
#endif
            _leaving.Remove(target);
            PublishActionSlots();
        }
    }
}
