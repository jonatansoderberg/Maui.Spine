using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Plugin.Maui.Spine.Common;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Svg;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// One row of a list or a settings screen: <c>[icon] [title / detail] [value] [accessory] [status] [check] [chevron]</c>.
/// Every part but the title is optional. Group rows in a <see cref="SpineSection"/> for the
/// platform's settings look.
/// </summary>
/// <remarks>
/// <para>
/// A row with a <see cref="Command"/> is a tap target with the platform's press feedback
/// (<see cref="Tap"/>) and shows a chevron unless <see cref="ShowChevron"/> says otherwise. A row
/// whose <see cref="Accessory"/> is a <see cref="Switch"/> and that has no command flips the switch
/// when tapped anywhere.
/// </para>
/// <para>
/// The row is one element to a screen reader (<see cref="Semantic.MergeProperty"/>): title,
/// detail and value are read together, a switch accessory makes it a toggle and a command a button.
/// A title with a value and nothing else is the key/value preset.
/// </para>
/// <para>
/// A row with a <see cref="Menu"/> opens the platform's own pop-up menu on a tap instead, and shows
/// the picked choice as its value. <see cref="Status"/> marks what is set up or needs the user, and
/// <see cref="IsDestructive"/> colours the title of a row that deletes or removes.
/// </para>
/// <para>
/// The defaults follow the platform: on Android the value sits under the title
/// (<see cref="ValuePlacement"/>) and there is no chevron, as in Android's own settings.
/// </para>
/// </remarks>
public class SpineRow : ContentView
{
    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(string), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyContent());

    public static readonly BindableProperty IconBackgroundProperty = BindableProperty.Create(
        nameof(IconBackground), typeof(Color), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).Repaint());

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyContent());

    public static readonly BindableProperty DetailProperty = BindableProperty.Create(
        nameof(Detail), typeof(string), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyContent());

    public static readonly BindableProperty DetailMarqueeProperty = BindableProperty.Create(
        nameof(DetailMarquee), typeof(bool), typeof(SpineRow), false,
        propertyChanged: static (b, _, _) => ((SpineRow)b).BuildDetail());

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(string), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyContent());

    public static readonly BindableProperty ValuePlacementProperty = BindableProperty.Create(
        nameof(ValuePlacement), typeof(SpineRowValuePlacement), typeof(SpineRow), SpineRowValuePlacement.Auto,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyContent());

    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(
        nameof(IsSelected), typeof(bool?), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyContent());

    public static readonly BindableProperty AccessoryProperty = BindableProperty.Create(
        nameof(Accessory), typeof(View), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyAccessory());

    public static readonly BindableProperty ShowChevronProperty = BindableProperty.Create(
        nameof(ShowChevron), typeof(bool?), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyContent());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyCommand());

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(SpineRow), null,
        propertyChanged: static (b, _, n) => Tap.SetCommandParameter(b, n));

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(SpineRowStyleOptions), typeof(SpineRow), null,
        propertyChanged: static (b, _, _) =>
        {
            ((SpineRow)b).Repaint();
            ((SpineRow)b).ApplyContent();
        });

    public static readonly BindableProperty MenuProperty = BindableProperty.Create(
        nameof(Menu), typeof(MenuItems), typeof(SpineRow), null,
        propertyChanged: static (b, o, _) => ((SpineRow)b).ApplyMenu((MenuItems?)o));

    public static readonly BindableProperty StatusProperty = BindableProperty.Create(
        nameof(Status), typeof(SpineRowStatus), typeof(SpineRow), SpineRowStatus.None,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyStatus());

    public static readonly BindableProperty IsDestructiveProperty = BindableProperty.Create(
        nameof(IsDestructive), typeof(bool), typeof(SpineRow), false,
        propertyChanged: static (b, _, _) => ((SpineRow)b).ApplyDestructive());

    /// <summary>Short name of an embedded SVG drawn at the start of the row, tinted with <see cref="SpineRowStyleOptions.IconColor"/>.</summary>
    public string? Icon
    {
        get => (string?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// A fill behind the icon, which then turns white: the coloured rounded squares of iOS Settings,
    /// the coloured circles of Android's. <see langword="null"/> = the tinted icon on its own.
    /// </summary>
    public Color? IconBackground
    {
        get => (Color?)GetValue(IconBackgroundProperty);
        set => SetValue(IconBackgroundProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>A second, smaller line under the title.</summary>
    public string? Detail
    {
        get => (string?)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>
    /// Draws the detail with <see cref="AnimatedLabel"/>, which scrolls text that does not fit instead
    /// of truncating it. The detail is a plain label until its text is wider than its room, so rows whose text fits
    /// cost no Skia canvas. Needs SkiaSharp: <c>UseSpine()</c> registers it, or <c>UseAnimatedLabel()</c> without Spine.
    /// </summary>
    public bool DetailMarquee
    {
        get => (bool)GetValue(DetailMarqueeProperty);
        set => SetValue(DetailMarqueeProperty, value);
    }

    /// <summary>Text at the end of the row: the current choice of a setting, or the value of a key/value row.</summary>
    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Where <see cref="Value"/> shows. <see cref="SpineRowValuePlacement.Auto"/> (the default) follows the platform.</summary>
    public SpineRowValuePlacement ValuePlacement
    {
        get => (SpineRowValuePlacement)GetValue(ValuePlacementProperty);
        set => SetValue(ValuePlacementProperty, value);
    }

    /// <summary>
    /// Makes the row one choice of a list: <see langword="true"/> shows a check mark at the end,
    /// <see langword="false"/> leaves the place empty. <see langword="null"/> (the default) = not a choice.
    /// A choice row shows no chevron unless <see cref="ShowChevron"/> asks for one.
    /// </summary>
    public bool? IsSelected
    {
        get => (bool?)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>A view at the end of the row: a <see cref="Switch"/>, a button, a badge.</summary>
    public View? Accessory
    {
        get => (View?)GetValue(AccessoryProperty);
        set => SetValue(AccessoryProperty, value);
    }

    /// <summary>
    /// Whether the chevron shows. <see langword="null"/> (the default) = when the row has a
    /// <see cref="Command"/>, no <see cref="Accessory"/> and is not a choice (<see cref="IsSelected"/>),
    /// and never on Android, whose lists have none.
    /// </summary>
    public bool? ShowChevron
    {
        get => (bool?)GetValue(ShowChevronProperty);
        set => SetValue(ShowChevronProperty, value);
    }

    /// <summary>The command a tap on the row runs.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public SpineRowStyleOptions? StyleOptions
    {
        get => (SpineRowStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    /// <summary>
    /// A menu the row opens on a tap, through the platform's own pop-up menu (<see cref="MenuButton"/>),
    /// for a choice of a few options such as a theme or a unit. The row ends in a small up-and-down
    /// chevron instead of the chevron (none on Android) and, without a <see cref="Value"/> of its own,
    /// shows the title of its first <see cref="MenuPicker"/>'s selection. The menu is the row's action:
    /// give it no <see cref="Command"/>.
    /// </summary>
    public MenuItems? Menu
    {
        get => (MenuItems?)GetValue(MenuProperty);
        set => SetValue(MenuProperty, value);
    }

    /// <summary>
    /// A mark at the end of the row: a green check on what is set up and working, an orange "!" on
    /// what needs the user. A row with a status shows no chevron unless <see cref="ShowChevron"/> asks for one.
    /// On the Mac and Windows (<see cref="SpineRowStyleOptions.StatusAsDot"/>) it is a small dot before the
    /// <see cref="Detail"/> instead, as they mark a connected network or device, and the chevron stays.
    /// </summary>
    public SpineRowStatus Status
    {
        get => (SpineRowStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>
    /// A row that deletes, removes or signs out for good: the title turns red
    /// (<see cref="SpineRowStyleOptions.DestructiveColor"/>) and the row shows no chevron, as a button
    /// rather than a way somewhere. Ask before the command does what cannot be undone.
    /// </summary>
    public bool IsDestructive
    {
        get => (bool)GetValue(IsDestructiveProperty);
        set => SetValue(IsDestructiveProperty, value);
    }

    static readonly bool IsAndroid = DeviceInfo.Platform == DevicePlatform.Android;
    static readonly bool IsWindows = DeviceInfo.Platform == DevicePlatform.WinUI;
    const string MenuGlyph = "spinerowmenu.svg";
    const string AttentionGlyph = "spinerowexclamation.svg";
    static readonly double DefaultSvgPadding = ((Thickness)SvgImageSource.PaddingProperty.DefaultValue).Left;

    // The title and its column are always there; every other part is made the first time the row needs it, and only
    // hidden after that. A settings page of sixty rows made about a thousand platform views for parts most rows never
    // show (a status mark, a check, a chevron, a menu glyph, a combo box), which took seconds on Windows.
    readonly Grid _grid;
    readonly VerticalStackLayout _text;
    readonly Label _title;
    Border? _iconBadge;
    Image? _icon;
    Label? _value;
    // "On" or "Off" before a switch accessory, as Windows Settings (SpineRowStyleOptions.ShowSwitchState).
    Label? _switchState;
    Switch? _watchedSwitch;
    // Under the title on Android: a preference's summary.
    Label? _valueBelow;
    ContentView? _accessory;
    Border? _status;
    Image? _statusGlyph;
    // The detail's line: the status dot (SpineRowStyleOptions.StatusAsDot) and the detail.
    Grid? _detailLine;
    Border? _statusDot;
    Image? _check;
    Image? _chevron;
    Image? _menuGlyph;
    // A menu's value in a combo box, as Windows Settings (SpineRowStyleOptions.MenuAsDropDown).
    Border? _dropDown;
    Label? _dropDownText;
    Image? _dropDownGlyph;
    View? _detail;
    Command? _toggle;
    Button? _menuButton;
    MenuItems? _watchedMenu;
    MenuPicker? _picker;
    MenuAction? _picked;
    // The style of the last Repaint, for parts made after it.
    SpineRowStyleOptions? _style;

    static SpineRow() => RowsStrings.EnsureRegistered();

    public SpineRow()
    {
        _title = new Label { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _text = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 1, Children = { _title } };

        _grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            ],
            Children = { _text },
        };
        Grid.SetColumn(_text, 1);

        Content = _grid;
        Semantic.SetMerge(this, true);

        ApplyContent();
        Repaint();
        SpineTheme.Track(this, Repaint);
    }

    SpineRowStyleOptions RowStyle => _style ??= SpineRowStyleOptions.Resolve(StyleOptions);

    // A part joins the grid under the menu's button, which takes the taps over the whole row, and like the other parts
    // is hidden from a screen reader while the button reads the row.
    T AddPart<T>(T part, int column) where T : View
    {
        Grid.SetColumn(part, column);
        if (_menuButton is not null)
        {
            _grid.Insert(_grid.IndexOf(_menuButton), part);
            AutomationProperties.SetExcludedWithChildren(part, true);
        }
        else
        {
            _grid.Add(part);
        }

        return part;
    }

    Border MakeIconBadge()
    {
        if (_iconBadge is not null)
            return _iconBadge;
        _icon = new Image { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _iconBadge = AddPart(new Border { StrokeThickness = 0, VerticalOptions = LayoutOptions.Center, Content = _icon }, 0);
        PaintIcon(RowStyle);
        return _iconBadge;
    }

    Grid DetailLineView()
    {
        if (_detailLine is not null)
            return _detailLine;
        _detailLine = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)] };
        _text.Insert(1, _detailLine);
        return _detailLine;
    }

    Border MakeStatusDot()
    {
        if (_statusDot is not null)
            return _statusDot;
        _statusDot = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(), VerticalOptions = LayoutOptions.Center };
        DetailLineView().Insert(0, _statusDot);
        PaintStatus(RowStyle);
        return _statusDot;
    }

    Label MakeValue()
    {
        if (_value is not null)
            return _value;
        _value = AddPart(new Label
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        }, 2);
        PaintValue(RowStyle);
        return _value;
    }

    Label MakeValueBelow()
    {
        if (_valueBelow is not null)
            return _valueBelow;
        _valueBelow = new Label { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _text.Add(_valueBelow);
        PaintValue(RowStyle);
        return _valueBelow;
    }

    Label MakeSwitchState()
    {
        if (_switchState is not null)
            return _switchState;
        // The switch reads its own state; the row's merged description doesn't need it twice.
        _switchState = AddPart(new Label { VerticalOptions = LayoutOptions.Center, MaxLines = 1 }, 2);
        AutomationProperties.SetExcludedWithChildren(_switchState, true);
        PaintValue(RowStyle);
        return _switchState;
    }

    Border MakeDropDown()
    {
        if (_dropDown is not null)
            return _dropDown;
        // The header bar's back glyph turned to point down: the combo box's chevron.
        _dropDownText = new Label { VerticalOptions = LayoutOptions.Center, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(12, 0, 4, 1) };
        _dropDownGlyph = new Image { VerticalOptions = LayoutOptions.Center, WidthRequest = 20, HeightRequest = 20, Rotation = -90, Margin = new Thickness(0, 0, 10, 0) };
        _dropDown = AddPart(new Border
        {
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = 32,
            Padding = 0,
            InputTransparent = true,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Content = new HorizontalStackLayout { Children = { _dropDownText, _dropDownGlyph } },
        }, 2);
        PaintDropDown(RowStyle);
        SvgImageSource.SetSvg(_dropDownGlyph, Presentation.HeaderBarConstants.BackGlyph);
        return _dropDown;
    }

    ContentView MakeAccessory()
    {
        if (_accessory is not null)
            return _accessory;
        _accessory = AddPart(new ContentView { VerticalOptions = LayoutOptions.Center }, 3);
        return _accessory;
    }

    Border MakeStatusMark()
    {
        if (_status is not null)
            return _status;
        _statusGlyph = new Image { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        // The mark's own size, without the canvas' margin: a glyph larger than the mark relied on the
        // mark clipping it, which WinUI doesn't do; it widened the mark's slot and cut the circle in half.
        SvgImageSource.SetPadding(_statusGlyph, new Thickness(0));
        _status = AddPart(new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
            VerticalOptions = LayoutOptions.Center,
            Content = _statusGlyph,
        }, 4);
        PaintStatus(RowStyle);
        return _status;
    }

    Image MakeCheck()
    {
        if (_check is not null)
            return _check;
        _check = AddPart(new Image { VerticalOptions = LayoutOptions.Center, WidthRequest = 30, HeightRequest = 30 }, 5);
        SemanticProperties.SetDescription(_check, SpineStrings.Current["Spine.Row.Selected"]);
        PaintGlyphs(RowStyle);
        SvgImageSource.SetSvg(_check, "check.svg");
        return _check;
    }

    Image MakeChevron()
    {
        if (_chevron is not null)
            return _chevron;
        // The header bar's own back glyph, mirrored, so the two chevrons are one shape.
        _chevron = AddPart(new Image
        {
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 28,
            HeightRequest = 28,
            Rotation = FlowDirection == FlowDirection.RightToLeft ? 0 : 180,
        }, 6);
        PaintGlyphs(RowStyle);
        SvgImageSource.SetSvg(_chevron, Presentation.HeaderBarConstants.BackGlyph);
        return _chevron;
    }

    Image MakeMenuGlyph()
    {
        if (_menuGlyph is not null)
            return _menuGlyph;
        // chevron.up.chevron.down, which says a tap opens a menu rather than a page.
        _menuGlyph = AddPart(new Image { VerticalOptions = LayoutOptions.Center, WidthRequest = 34, HeightRequest = 34 }, 6);
        PaintGlyphs(RowStyle);
        SvgImageSource.SetSvg(_menuGlyph, MenuGlyph);
        return _menuGlyph;
    }

    // Shows a part when it is wanted, making it first; hides it when it was made before.
    static void Show<T>(ref T? part, bool shown, Func<T> make) where T : View
    {
        if (shown)
            (part ?? make()).IsVisible = true;
        else if (part is not null)
            part.IsVisible = false;
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == IsEnabledProperty.PropertyName)
            _grid.Opacity = IsEnabled ? 1 : RowStyle.DisabledOpacity;
        else if (propertyName == FlowDirectionProperty.PropertyName && _chevron is not null)
            _chevron.Rotation = FlowDirection == FlowDirection.RightToLeft ? 0 : 180;
    }

    void BuildDetail()
    {
        if (_detail is not null)
        {
            _detail.SizeChanged -= OnDetailSizeChanged;
            DetailLineView().Remove(_detail);
        }

        // A marquee is a Skia canvas of its own, costly on Windows: the detail is a label until its text does not fit.
        _detail = DetailMarquee && _detailOverflows
            ? new AnimatedLabel()
            : new Label { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        if (_detail is Label)
            _detail.SizeChanged += OnDetailSizeChanged;
        Grid.SetColumn(_detail, 1);
        DetailLineView().Add(_detail);

        ApplyContent();
        Repaint();
    }

    // The detail's text was wider than its room: it scrolls (DetailMarquee) from then on.
    bool _detailOverflows;

    void OnDetailSizeChanged(object? sender, EventArgs e) => CheckDetailOverflow();

    // After layout, when the label has its width: the text's own width against it.
    void CheckDetailOverflow()
    {
        if (!DetailMarquee || _detailOverflows || _detail is not Label { Width: > 0, Handler: not null } label || string.IsNullOrEmpty(label.Text))
            return;

        var needed = label.Measure(double.PositiveInfinity, double.PositiveInfinity).Width;
        if (needed <= label.Width + 1)
            return;

        _detailOverflows = true;
        // Not inside the layout pass that reported the size.
        Dispatcher.Dispatch(BuildDetail);
    }

    void ApplyContent()
    {
        var style = RowStyle;

        Show(ref _iconBadge, !string.IsNullOrEmpty(Icon), MakeIconBadge);
        if (_iconBadge is { IsVisible: true })
            SvgImageSource.SetSvg(_icon!, Icon!);

        _title.Text = Title;

        var hasDetail = !string.IsNullOrEmpty(Detail);
        if (hasDetail && _detail is null)
        {
            BuildDetail();
            return;
        }

        switch (_detail)
        {
            case Label label:
                var changed = label.Text != Detail;
                label.Text = Detail;
                label.IsVisible = hasDetail;
                // A longer text in the same room may no longer fit.
                if (changed && DetailMarquee && label.Width > 0)
                    Dispatcher.Dispatch(CheckDetailOverflow);
                break;
            case AnimatedLabel marquee:
                marquee.Text = Detail ?? string.Empty;
                marquee.IsVisible = hasDetail;
                // The canvas has no text a screen reader can see; the merged row reads this.
                SemanticProperties.SetDescription(marquee, Detail);
                break;
        }

        var value = !string.IsNullOrEmpty(Value) ? Value : FindPicker(Menu)?.Selected?.Title;
        var hasValue = !string.IsNullOrEmpty(value);
        var below = ValuePlacement == SpineRowValuePlacement.Below
            || (ValuePlacement == SpineRowValuePlacement.Auto && IsAndroid);

        // Windows Settings shows a menu's choice in a combo box rather than as a value with a chevron.
        var dropDown = Menu is not null && !IsAndroid && style.MenuAsDropDown;
        var valueShown = hasValue && !below && !dropDown;
        Show(ref _value, valueShown, MakeValue);
        if (_value is not null)
            _value.Text = value;
        Show(ref _dropDown, dropDown, MakeDropDown);
        if (_dropDownText is not null)
            _dropDownText.Text = value;

        // Windows Settings: "On" / "Off" before a switch, in the value's place when the row has no value.
        Show(ref _switchState, _watchedSwitch is not null && !valueShown && style.ShowSwitchState, MakeSwitchState);
        if (_switchState is { IsVisible: true })
            _switchState.Text = SpineStrings.Current[_watchedSwitch!.IsToggled ? "Spine.Row.On" : "Spine.Row.Off"];

        Show(ref _valueBelow, hasValue && below, MakeValueBelow);
        if (_valueBelow is not null)
            _valueBelow.Text = value;

        Show(ref _check, IsSelected == true, MakeCheck);

        // The status as a dot before the detail (the Mac, Windows), or as the mark at the end when there is no detail.
        var dot = Status != SpineRowStatus.None && hasDetail && style.StatusAsDot;
        var mark = Status != SpineRowStatus.None && !dot;
        Show(ref _statusDot, dot, MakeStatusDot);
        Show(ref _status, mark, MakeStatusMark);
        if (_detailLine is not null)
            _detailLine.IsVisible = hasDetail;

        Show(ref _chevron, Menu is null && (ShowChevron
            ?? (Command is not null && Accessory is null && IsSelected is null && !mark && !IsDestructive && !IsAndroid)), MakeChevron);
        Show(ref _menuGlyph, Menu is not null && !IsAndroid && !dropDown, MakeMenuGlyph);

        if (_menuButton is not null)
            SemanticProperties.SetDescription(_menuButton, string.Join(", ", new[] { Title, Detail, value }.Where(s => !string.IsNullOrEmpty(s))));

        ApplyHeight();
    }

    // Material 3 list items grow from 56 to 72 dp with a second line; iOS rows keep one height.
    void ApplyHeight()
    {
        var style = RowStyle;
        var twoLines = _valueBelow is { IsVisible: true } || _detail is VisualElement { IsVisible: true };
        _grid.MinimumHeightRequest = twoLines ? style.TwoLineMinimumHeight : style.MinimumHeight;
    }

    /// <summary>Where the row's text starts, from its leading edge: a <see cref="SpineSection"/> starts its separators there.</summary>
    internal double TextInset
    {
        get
        {
            var style = RowStyle;
            var icon = _iconBadge is not { IsVisible: true } ? 0
                : (IconBackground is null || !style.ShowIconBadges ? style.IconSize : style.IconBadgeSize) + style.IconSpacing;
            return style.Padding.Left + icon;
        }
    }

    void ApplyAccessory()
    {
        if (_watchedSwitch is not null)
            _watchedSwitch.Toggled -= OnSwitchToggled;
        _watchedSwitch = Accessory as Switch;
        if (_watchedSwitch is not null)
            _watchedSwitch.Toggled += OnSwitchToggled;

        if (Accessory is not null || _accessory is not null)
        {
            var view = MakeAccessory();
            view.Content = Accessory;
            view.IsVisible = Accessory is not null;
            // Only with an accessory: an empty one kept its margin in the grid on Windows, 12 points before the edge.
            view.Margin = new Thickness(Accessory is null ? 0 : RowStyle.Spacing, 0, 0, 0);
        }

        ApplyCommand();
    }

    void OnSwitchToggled(object? sender, ToggledEventArgs e) => ApplyContent();

    void ApplyCommand()
    {
        // A switch row without a command of its own toggles its switch, which is also what a
        // screen reader's activation does on the merged row.
        if (Command is null && Accessory is Switch toggle)
        {
            _toggle = new Command(() => toggle.IsToggled = !toggle.IsToggled, () => toggle.IsEnabled);
            Tap.SetCommand(this, _toggle);
        }
        else
        {
            _toggle = null;
            Tap.SetCommand(this, Command);
        }

        ApplyContent();
    }

    void ApplyStatus()
    {
        ApplyContent();
        Repaint();
    }

    void ApplyDestructive()
    {
        ApplyContent();
        Repaint();
    }

    void ApplyMenu(MenuItems? old)
    {
        if (Menu is null)
        {
            if (_menuButton is not null)
            {
                _grid.Remove(_menuButton);
                _menuButton = null;
            }
        }
        else if (_menuButton is null)
        {
            // The platform opens a tap-to-open menu only from a button, so one lies over the whole row.
            _menuButton = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0 };
            Grid.SetColumnSpan(_menuButton, _grid.ColumnDefinitions.Count);
            _grid.Add(_menuButton);
        }

        if (_menuButton is not null)
            MenuButton.SetItems(_menuButton, Menu);

        // A screen reader reaches the menu through that button; a merged row would hide it with the
        // rest of its parts, so the parts are hidden instead and the button reads the row.
        View?[] parts = [_iconBadge, _text, _value, _dropDown, _accessory, _status, _check, _chevron, _menuGlyph];
        if (Menu is null)
        {
            foreach (var part in parts)
                part?.ClearValue(AutomationProperties.ExcludedWithChildrenProperty);
            Semantic.SetMerge(this, true);
        }
        else if (old is null)
        {
            Semantic.SetMerge(this, false);
            foreach (var part in parts)
                if (part is not null)
                    AutomationProperties.SetExcludedWithChildren(part, true);
        }

        WatchMenu();
        ApplyContent();
        Repaint();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        WatchMenu();
    }

    // Follows the picked choice while the row is on screen, so its value changes with the menu.
    void WatchMenu()
    {
        var menu = Handler is null ? null : Menu;
        if (_watchedMenu != menu)
        {
            if (_watchedMenu is not null)
                _watchedMenu.CollectionChanged -= OnMenuChanged;
            _watchedMenu = menu;
            if (menu is not null)
                menu.CollectionChanged += OnMenuChanged;
        }

        var picker = FindPicker(menu);
        if (_picker != picker)
        {
            if (_picker is not null)
                _picker.PropertyChanged -= OnPickerChanged;
            _picker = picker;
            if (picker is not null)
                picker.PropertyChanged += OnPickerChanged;
        }

        var picked = picker?.Selected;
        if (_picked != picked)
        {
            if (_picked is not null)
                _picked.PropertyChanged -= OnPickedChanged;
            _picked = picked;
            if (picked is not null)
                picked.PropertyChanged += OnPickedChanged;
        }
    }

    void OnMenuChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        WatchMenu();
        ApplyContent();
    }

    void OnPickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MenuPicker.Selected))
            return;

        WatchMenu();
        ApplyContent();
    }

    void OnPickedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MenuAction.Title))
            ApplyContent();
    }

    static MenuPicker? FindPicker(IEnumerable<MenuElement>? items)
    {
        foreach (var item in items ?? [])
        {
            var picker = item switch
            {
                MenuPicker p => p,
                MenuSection section => FindPicker(section),
                _ => null,
            };
            if (picker is not null)
                return picker;
        }

        return null;
    }

    // The row's own parts, then each part that has been made; a part made later is painted when it is made.
    void Repaint()
    {
        var style = _style = SpineRowStyleOptions.Resolve(StyleOptions);

        _grid.Padding = style.Padding;
        // Spacing as margins rather than ColumnSpacing: the grid would keep the spacing of an
        // empty column, and a row without a chevron would end short of its padding.
        _grid.ColumnSpacing = 0;
        if (_accessory is not null)
            // Only with an accessory: an empty one kept its margin in the grid on Windows, 12 points before the edge.
            _accessory.Margin = new Thickness(Accessory is null ? 0 : style.Spacing, 0, 0, 0);
        if (_menuButton is not null)
            _menuButton.Margin = new Thickness(-style.Padding.Left, -style.Padding.Top, -style.Padding.Right, -style.Padding.Bottom);
        _grid.Opacity = IsEnabled ? 1 : style.DisabledOpacity;
        ApplyHeight();

        _title.TextColor = IsDestructive ? style.DestructiveColor : style.TitleColor;
        _title.FontSize = style.TitleFontSize;
        _title.FontFamily = style.FontFamily;
        _title.FontAttributes = style.TitleFontAttributes;

        switch (_detail)
        {
            case Label label:
                label.TextColor = style.DetailColor;
                label.FontSize = style.DetailFontSize;
                label.FontFamily = style.FontFamily;
                break;
            case AnimatedLabel marquee:
                marquee.TextColor = style.DetailColor!;
                marquee.FontSize = style.DetailFontSize;
                marquee.FontFamily = style.FontFamily ?? string.Empty;
                marquee.HeightRequest = Math.Ceiling(style.DetailFontSize * 1.35);
                break;
        }

        PaintIcon(style);
        PaintValue(style);
        PaintGlyphs(style);
        PaintDropDown(style);
        PaintStatus(style);
    }

    void PaintIcon(SpineRowStyleOptions style)
    {
        if (_iconBadge is null || _icon is null)
            return;

        _iconBadge.Margin = new Thickness(0, 0, style.IconSpacing, 0);
        if (IconBackground is { } badge && style.ShowIconBadges)
        {
            _iconBadge.BackgroundColor = badge;
            _iconBadge.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = style.IconBadgeCornerRadius };
            _iconBadge.WidthRequest = _iconBadge.HeightRequest = style.IconBadgeSize;
            // A glyph larger than the badge relied on the badge clipping it, which WinUI doesn't do: it
            // sized the badge's shape to the glyph and cut its corners off. The glyph keeps to the badge
            // and gives up as much of the canvas' margin, so it draws as large as before.
            var overflow = Math.Max(0, (style.IconBadgeGlyphSize - style.IconBadgeSize) / 2);
            _icon.WidthRequest = _icon.HeightRequest = style.IconBadgeGlyphSize - 2 * overflow;
            SvgImageSource.SetPadding(_icon, new Thickness(DefaultSvgPadding - overflow));
            SvgImageSource.SetTintColor(_icon, style.IconBadgeGlyphColor!);
        }
        else
        {
            _iconBadge.BackgroundColor = Colors.Transparent;
            _iconBadge.WidthRequest = _iconBadge.HeightRequest = style.IconSize;
            _icon.WidthRequest = _icon.HeightRequest = style.IconSize;
            if (style.IconPadding is { } padding)
                SvgImageSource.SetPadding(_icon, new Thickness(padding));
            else
                _icon.ClearValue(SvgImageSource.PaddingProperty);
            if (style.IconLineWidthScale is { } scale)
                SvgImageSource.SetLineWidthScale(_icon, scale);
            else
                _icon.ClearValue(SvgImageSource.LineWidthScaleProperty);
            SvgImageSource.SetTintColor(_icon, style.IconColor!);
        }
    }

    void PaintValue(SpineRowStyleOptions style)
    {
        if (_value is not null)
        {
            _value.Margin = new Thickness(style.Spacing, 0, 0, 0);
            _value.TextColor = style.ValueColor;
            _value.FontSize = style.ValueFontSize;
            _value.FontFamily = style.FontFamily;
        }

        if (_switchState is not null)
        {
            _switchState.Margin = new Thickness(style.Spacing, 0, 0, 0);
            _switchState.TextColor = style.TitleColor;
            _switchState.FontSize = style.TitleFontSize;
            _switchState.FontFamily = style.FontFamily;
        }

        if (_valueBelow is not null)
        {
            _valueBelow.TextColor = style.DetailColor;
            _valueBelow.FontSize = style.DetailFontSize;
            _valueBelow.FontFamily = style.FontFamily;
        }
    }

    void PaintGlyphs(SpineRowStyleOptions style)
    {
        if (_check is not null)
        {
            _check.Margin = new Thickness(style.Spacing - 4, 0, -4, 0);
            SvgImageSource.SetTintColor(_check, style.CheckColor!);
            SvgImageSource.SetLineWidthScale(_check, style.GlyphLineWidthScale);
        }

        if (_chevron is not null)
        {
            // The glyph's own margin, taken back so the chevron sits at the row's padding.
            // Windows Settings keeps its chevron 22 points from the card's edge, further in than iOS.
            _chevron.Margin = new Thickness(style.Spacing - 6, 0, IsWindows ? -5 : -9, 0);
            SvgImageSource.SetTintColor(_chevron, style.ChevronColor!);
            SvgImageSource.SetLineWidthScale(_chevron, style.GlyphLineWidthScale);
        }

        if (_menuGlyph is not null)
        {
            // The glyph is narrow in its square: close to the value, its strokes at the row's padding.
            _menuGlyph.Margin = new Thickness(-6, 0, -12, 0);
            SvgImageSource.SetTintColor(_menuGlyph, style.ValueColor!);
            SvgImageSource.SetLineWidthScale(_menuGlyph, style.GlyphLineWidthScale * 1.25);
        }
    }

    void PaintDropDown(SpineRowStyleOptions style)
    {
        if (_dropDown is null || _dropDownText is null || _dropDownGlyph is null)
            return;

        _dropDown.BackgroundColor = style.DropDownColor;
        _dropDown.Stroke = style.DropDownBorderColor is { } dropDownBorder ? new SolidColorBrush(dropDownBorder) : null;
        _dropDown.StrokeThickness = style.DropDownBorderColor is null ? 0 : 1;
        _dropDown.Margin = new Thickness(style.Spacing, 0, 0, 0);
        _dropDownText.TextColor = style.TitleColor;
        _dropDownText.FontSize = style.TitleFontSize;
        _dropDownText.FontFamily = style.FontFamily;
        SvgImageSource.SetTintColor(_dropDownGlyph, style.TitleColor!);
        SvgImageSource.SetLineWidthScale(_dropDownGlyph, style.GlyphLineWidthScale);
    }

    void PaintStatus(SpineRowStyleOptions style)
    {
        var ok = Status == SpineRowStatus.Ok;
        var fill = ok ? style.StatusOkColor : style.StatusAttentionColor;
        var description = Status == SpineRowStatus.None ? null
            : SpineStrings.Current[ok ? "Spine.Row.StatusOk" : "Spine.Row.StatusNeedsAttention"];

        if (_status is not null && _statusGlyph is not null)
        {
            _status.Margin = new Thickness(style.Spacing, 0, 0, 0);
            _status.WidthRequest = _status.HeightRequest = style.StatusSize;
            _statusGlyph.WidthRequest = _statusGlyph.HeightRequest = style.StatusSize;
            _status.BackgroundColor = fill;
            SemanticProperties.SetDescription(_status, description);
            SvgImageSource.SetTintColor(_statusGlyph, style.StatusGlyphColor!);
            // A heavier stroke than the chevron's: the glyph is small and white on a fill.
            SvgImageSource.SetLineWidthScale(_statusGlyph, style.GlyphLineWidthScale * 1.75);
            if (Status != SpineRowStatus.None)
                SvgImageSource.SetSvg(_statusGlyph, ok ? "check.svg" : AttentionGlyph);
        }

        if (_statusDot is not null)
        {
            _statusDot.BackgroundColor = fill;
            _statusDot.WidthRequest = _statusDot.HeightRequest = style.StatusDotSize;
            _statusDot.Margin = new Thickness(0, 0, Math.Round(style.StatusDotSize * 0.9), 0);
            SemanticProperties.SetDescription(_statusDot, description);
        }
    }
}
