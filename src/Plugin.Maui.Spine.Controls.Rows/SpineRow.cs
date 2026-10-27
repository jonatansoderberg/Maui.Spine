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
        propertyChanged: static (b, _, _) => ((SpineRow)b).Repaint());

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
    /// of truncating it. Needs SkiaSharp: <c>UseSpine()</c> registers it, or <c>UseAnimatedLabel()</c> without Spine.
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
    const string MenuGlyph = "spinerowmenu.svg";
    const string AttentionGlyph = "spinerowexclamation.svg";

    readonly Grid _grid;
    readonly Border _iconBadge;
    readonly Image _icon;
    readonly VerticalStackLayout _text;
    readonly Label _title;
    readonly Label _value;
    readonly Label _valueBelow;
    readonly ContentView _accessory;
    readonly Border _status;
    readonly Image _statusGlyph;
    readonly Image _check;
    readonly Image _chevron;
    readonly Image _menuGlyph;
    View? _detail;
    Command? _toggle;
    Button? _menuButton;
    MenuItems? _watchedMenu;
    MenuPicker? _picker;
    MenuAction? _picked;

    static SpineRow() => RowsStrings.EnsureRegistered();

    public SpineRow()
    {
        _icon = new Image { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _iconBadge = new Border
        {
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = _icon,
        };

        _title = new Label { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _text = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 1, Children = { _title } };

        _value = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };

        // Under the title on Android: a preference's summary.
        _valueBelow = new Label { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _text.Add(_valueBelow);

        _accessory = new ContentView { VerticalOptions = LayoutOptions.Center };

        _statusGlyph = new Image { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _status = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Content = _statusGlyph,
        };

        _check = new Image { VerticalOptions = LayoutOptions.Center, WidthRequest = 30, HeightRequest = 30 };
        SvgImageSource.SetSvg(_check, "check.svg");

        // The header bar's own back glyph, mirrored, so the two chevrons are one shape.
        _chevron = new Image
        {
            VerticalOptions = LayoutOptions.Center,
            WidthRequest = 28,
            HeightRequest = 28,
            Rotation = 180,
        };
        SvgImageSource.SetSvg(_chevron, Presentation.HeaderBarConstants.BackGlyph);

        // chevron.up.chevron.down, which says a tap opens a menu rather than a page.
        _menuGlyph = new Image { VerticalOptions = LayoutOptions.Center, WidthRequest = 34, HeightRequest = 34 };
        SvgImageSource.SetSvg(_menuGlyph, MenuGlyph);

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
            Children = { _iconBadge, _text, _value, _accessory, _status, _check, _chevron, _menuGlyph },
        };

        Grid.SetColumn(_text, 1);
        Grid.SetColumn(_value, 2);
        Grid.SetColumn(_accessory, 3);
        Grid.SetColumn(_status, 4);
        Grid.SetColumn(_check, 5);
        Grid.SetColumn(_chevron, 6);
        Grid.SetColumn(_menuGlyph, 6);

        Content = _grid;
        Semantic.SetMerge(this, true);

        ApplyContent();
        Repaint();
        SpineTheme.Track(this, Repaint);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == IsEnabledProperty.PropertyName)
            _grid.Opacity = IsEnabled ? 1 : SpineRowStyleOptions.Resolve(StyleOptions).DisabledOpacity;
        else if (propertyName == FlowDirectionProperty.PropertyName)
            _chevron.Rotation = FlowDirection == FlowDirection.RightToLeft ? 0 : 180;
    }

    void BuildDetail()
    {
        if (_detail is not null)
            _text.Remove(_detail);

        _detail = DetailMarquee
            ? new AnimatedLabel()
            : new Label { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _text.Insert(1, _detail);

        ApplyContent();
        Repaint();
    }

    void ApplyContent()
    {
        _iconBadge.IsVisible = !string.IsNullOrEmpty(Icon);
        if (_iconBadge.IsVisible)
            SvgImageSource.SetSvg(_icon, Icon!);

        _title.Text = Title;

        if (!string.IsNullOrEmpty(Detail) && _detail is null)
        {
            BuildDetail();
            return;
        }

        switch (_detail)
        {
            case Label label:
                label.Text = Detail;
                label.IsVisible = !string.IsNullOrEmpty(Detail);
                break;
            case AnimatedLabel marquee:
                marquee.Text = Detail ?? string.Empty;
                marquee.IsVisible = !string.IsNullOrEmpty(Detail);
                // The canvas has no text a screen reader can see; the merged row reads this.
                SemanticProperties.SetDescription(marquee, Detail);
                break;
        }

        var value = !string.IsNullOrEmpty(Value) ? Value : FindPicker(Menu)?.Selected?.Title;
        var hasValue = !string.IsNullOrEmpty(value);
        var below = ValuePlacement == SpineRowValuePlacement.Below
            || (ValuePlacement == SpineRowValuePlacement.Auto && IsAndroid);

        _value.Text = value;
        _value.IsVisible = hasValue && !below;
        _valueBelow.Text = value;
        _valueBelow.IsVisible = hasValue && below;

        _check.IsVisible = IsSelected == true;
        SemanticProperties.SetDescription(_check, SpineStrings.Current["Spine.Row.Selected"]);

        _chevron.IsVisible = Menu is null && (ShowChevron
            ?? (Command is not null && Accessory is null && IsSelected is null && Status == SpineRowStatus.None && !IsDestructive && !IsAndroid));
        _menuGlyph.IsVisible = Menu is not null && !IsAndroid;

        if (_menuButton is not null)
            SemanticProperties.SetDescription(_menuButton, string.Join(", ", new[] { Title, Detail, value }.Where(s => !string.IsNullOrEmpty(s))));

        ApplyHeight();
    }

    // Material 3 list items grow from 56 to 72 dp with a second line; iOS rows keep one height.
    void ApplyHeight()
    {
        var style = SpineRowStyleOptions.Resolve(StyleOptions);
        var twoLines = _valueBelow.IsVisible || _detail is VisualElement { IsVisible: true };
        _grid.MinimumHeightRequest = twoLines ? style.TwoLineMinimumHeight : style.MinimumHeight;
    }

    /// <summary>Where the row's text starts, from its leading edge: a <see cref="SpineSection"/> starts its separators there.</summary>
    internal double TextInset
    {
        get
        {
            var style = SpineRowStyleOptions.Resolve(StyleOptions);
            var icon = !_iconBadge.IsVisible ? 0
                : (IconBackground is null ? style.IconSize : style.IconBadgeSize) + style.Spacing;
            return style.Padding.Left + icon;
        }
    }

    void ApplyAccessory()
    {
        _accessory.Content = Accessory;
        _accessory.IsVisible = Accessory is not null;
        ApplyCommand();
    }

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
        _status.IsVisible = Status != SpineRowStatus.None;
        if (_status.IsVisible)
        {
            var ok = Status == SpineRowStatus.Ok;
            SvgImageSource.SetSvg(_statusGlyph, ok ? "check.svg" : AttentionGlyph);
            SemanticProperties.SetDescription(_status, SpineStrings.Current[ok ? "Spine.Row.StatusOk" : "Spine.Row.StatusNeedsAttention"]);
        }

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
        View[] parts = [_iconBadge, _text, _value, _accessory, _status, _check, _chevron, _menuGlyph];
        if (Menu is null)
        {
            foreach (var part in parts)
                part.ClearValue(AutomationProperties.ExcludedWithChildrenProperty);
            Semantic.SetMerge(this, true);
        }
        else if (old is null)
        {
            Semantic.SetMerge(this, false);
            foreach (var part in parts)
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

    void Repaint()
    {
        var style = SpineRowStyleOptions.Resolve(StyleOptions);

        _grid.Padding = style.Padding;
        // Spacing as margins rather than ColumnSpacing: the grid would keep the spacing of an
        // empty column, and a row without a chevron would end short of its padding.
        _grid.ColumnSpacing = 0;
        _iconBadge.Margin = new Thickness(0, 0, style.Spacing, 0);
        _value.Margin = new Thickness(style.Spacing, 0, 0, 0);
        _accessory.Margin = new Thickness(style.Spacing, 0, 0, 0);
        _check.Margin = new Thickness(style.Spacing - 4, 0, -4, 0);
        // The glyph's own margin, taken back so the chevron sits at the row's padding.
        _chevron.Margin = new Thickness(style.Spacing - 6, 0, -9, 0);
        // The glyph is narrow in its square: close to the value, its strokes at the row's padding.
        _menuGlyph.Margin = new Thickness(-6, 0, -12, 0);
        _status.Margin = new Thickness(style.Spacing, 0, 0, 0);
        if (_menuButton is not null)
            _menuButton.Margin = new Thickness(-style.Padding.Left, -style.Padding.Top, -style.Padding.Right, -style.Padding.Bottom);
        _grid.Opacity = IsEnabled ? 1 : style.DisabledOpacity;
        ApplyHeight();

        if (IconBackground is { } badge)
        {
            _iconBadge.BackgroundColor = badge;
            _iconBadge.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = style.IconBadgeCornerRadius };
            _iconBadge.WidthRequest = _iconBadge.HeightRequest = style.IconBadgeSize;
            _icon.WidthRequest = _icon.HeightRequest = style.IconBadgeGlyphSize;
            SvgImageSource.SetTintColor(_icon, style.IconBadgeGlyphColor!);
        }
        else
        {
            _iconBadge.BackgroundColor = Colors.Transparent;
            _iconBadge.WidthRequest = _iconBadge.HeightRequest = style.IconSize;
            _icon.WidthRequest = _icon.HeightRequest = style.IconSize;
            SvgImageSource.SetTintColor(_icon, style.IconColor!);
        }

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

        _value.TextColor = style.ValueColor;
        _value.FontSize = style.ValueFontSize;
        _value.FontFamily = style.FontFamily;

        _valueBelow.TextColor = style.DetailColor;
        _valueBelow.FontSize = style.DetailFontSize;
        _valueBelow.FontFamily = style.FontFamily;

        SvgImageSource.SetTintColor(_chevron, style.ChevronColor!);
        SvgImageSource.SetLineWidthScale(_chevron, style.GlyphLineWidthScale);
        SvgImageSource.SetTintColor(_check, style.CheckColor!);
        SvgImageSource.SetLineWidthScale(_check, style.GlyphLineWidthScale);
        SvgImageSource.SetTintColor(_menuGlyph, style.ValueColor!);
        SvgImageSource.SetLineWidthScale(_menuGlyph, style.GlyphLineWidthScale * 1.25);

        _status.WidthRequest = _status.HeightRequest = style.StatusSize;
        // Larger than the mark, which clips it: the glyph draws with a margin of its own.
        _statusGlyph.WidthRequest = _statusGlyph.HeightRequest = style.StatusSize * 1.5;
        _status.BackgroundColor = Status == SpineRowStatus.Ok ? style.StatusOkColor : style.StatusAttentionColor;
        SvgImageSource.SetTintColor(_statusGlyph, style.StatusGlyphColor!);
        // A heavier stroke than the chevron's: the glyph is small and white on a fill.
        SvgImageSource.SetLineWidthScale(_statusGlyph, style.GlyphLineWidthScale * 1.75);
    }
}
