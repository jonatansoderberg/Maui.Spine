using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// A group of <see cref="SpineRow"/>s with an optional header and footer, drawn the way the
/// platform's own settings are: an inset group with hairline separators on iOS, plain rows under
/// an accent-coloured header on Android, a card for each row on Windows.
/// </summary>
/// <remarks>
/// <para>
/// The section puts a separator between visible rows, never after the last, starting where the
/// row's text starts (after its icon) and ending 16 points before the edge. Rows can be any view;
/// a separator before a view that is not a <see cref="SpineRow"/> starts at the row padding.
/// </para>
/// <para>
/// On iOS the groups belong on the grouped background: give the page
/// <c>BackgroundColor="{AppThemeBinding Light={x:Static SpineSection.PageBackgroundLight}, Dark={x:Static SpineSection.PageBackgroundDark}}"</c>.
/// The section cannot colour the page itself.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;SpineSection Header="Connections" Footer="Wi-Fi turns on again tomorrow."&gt;
///     &lt;SpineRow Icon="wireless.svg" IconBackground="#007AFF" Title="Wi-Fi" Value="Home" Command="{Binding WiFiCommand}" /&gt;
///     &lt;SpineRow Icon="bluetooth.svg" IconBackground="#007AFF" Title="Bluetooth" Value="On" Command="{Binding BluetoothCommand}" /&gt;
/// &lt;/SpineSection&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Rows))]
public class SpineSection : ContentView
{
    static readonly bool IsApple = DeviceInfo.Platform == DevicePlatform.iOS || DeviceInfo.Platform == DevicePlatform.MacCatalyst;

    /// <summary>
    /// The page behind grouped sections in light mode: <c>systemGroupedBackground</c> on iOS and Mac
    /// Catalyst; transparent elsewhere, so the page keeps the background behind it.
    /// </summary>
    public static Color PageBackgroundLight { get; } = IsApple ? Color.FromArgb("#F2F2F7") : Colors.Transparent;

    /// <summary>The page behind grouped sections in dark mode; see <see cref="PageBackgroundLight"/>.</summary>
    public static Color PageBackgroundDark { get; } = IsApple ? Color.FromArgb("#000000") : Colors.Transparent;

    public static readonly BindableProperty HeaderProperty = BindableProperty.Create(
        nameof(Header), typeof(string), typeof(SpineSection), null,
        propertyChanged: static (b, _, _) => ((SpineSection)b).ApplyText());

    public static readonly BindableProperty FooterProperty = BindableProperty.Create(
        nameof(Footer), typeof(string), typeof(SpineSection), null,
        propertyChanged: static (b, _, _) => ((SpineSection)b).ApplyText());

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(SpineSectionStyleOptions), typeof(SpineSection), null,
        propertyChanged: static (b, _, _) => ((SpineSection)b).Repaint());

    /// <summary>Text above the group.</summary>
    public string? Header
    {
        get => (string?)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Text under the group: an explanation of its settings.</summary>
    public string? Footer
    {
        get => (string?)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public SpineSectionStyleOptions? StyleOptions
    {
        get => (SpineSectionStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    /// <summary>The rows, in order. Separators are added between them.</summary>
    public IList<View> Rows => _rows;

    readonly ObservableCollection<View> _rows = [];
    readonly Label _header = new() { Margin = new Thickness(0, 0, 0, 6) };
    readonly Label _footer = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly VerticalStackLayout _stack = new();
    readonly List<BoxView> _separators = [];
    // With SeparateRows (Windows): each row in a card of its own instead of separators.
    readonly List<Border> _cards = [];
    bool _separated;
    readonly Border _group;

    public SpineSection()
    {
        _group = new Border { StrokeThickness = 0, Padding = 0, Content = _stack };
        _group.SizeChanged += (_, _) => ApplyShape();
        Content = new VerticalStackLayout { Children = { _header, _group, _footer } };

        _rows.CollectionChanged += OnRowsChanged;

        ApplyText();
        Repaint();
        SpineTheme.Track(this, Repaint);
    }

    void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in e.OldItems?.OfType<View>() ?? [])
            row.PropertyChanged -= OnRowPropertyChanged;

        foreach (var row in e.NewItems?.OfType<View>() ?? [])
            row.PropertyChanged += OnRowPropertyChanged;

        Rebuild();
    }

    // Only the separators follow a row: re-parenting the rows would reset their bindings, whose
    // changes land here again.
    void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsVisible) or nameof(SpineRow.Icon) or nameof(SpineRow.IconBackground) or nameof(SpineRow.StyleOptions))
            UpdateSeparators();
    }

    // Each row gets a separator before it; UpdateSeparators shows the ones between visible rows.
    void Rebuild()
    {
        _stack.Clear();
        foreach (var card in _cards)
            card.Content = null;
        _cards.Clear();
        _separators.Clear();
        _separated = SpineSectionStyleOptions.Resolve(StyleOptions).SeparateRows;

        foreach (var row in _rows)
        {
            if (_separated)
            {
                var card = new Border { Padding = 0, Content = row };
                _cards.Add(card);
                _stack.Add(card);
                continue;
            }

            var separator = new BoxView { InputTransparent = true };
            _separators.Add(separator);
            _stack.Add(separator);
            _stack.Add(row);
        }

        PaintCards(SpineSectionStyleOptions.Resolve(StyleOptions));
        UpdateSeparators();
    }

    void PaintCards(SpineSectionStyleOptions style)
    {
        foreach (var card in _cards)
        {
            card.BackgroundColor = style.BackgroundColor;
            // A Color converts to a brush even when null, and WinUI throws on a brush without a colour.
            card.Stroke = style.BorderColor is { } border ? new SolidColorBrush(border) : null;
            card.StrokeThickness = style.BorderColor is null ? 0 : 1;
            if (card.StrokeShape is not RoundRectangle { CornerRadius: var current } || current != new CornerRadius(style.CornerRadius))
                card.StrokeShape = new RoundRectangle { CornerRadius = style.CornerRadius };
        }
    }

    void UpdateSeparators()
    {
        if (_separated)
        {
            for (var i = 0; i < _cards.Count; i++)
                _cards[i].IsVisible = _rows[i].IsVisible;
            return;
        }

        var style = SpineSectionStyleOptions.Resolve(StyleOptions);
        var hairline = 1 / Math.Max(1, DeviceDisplay.Current.MainDisplayInfo.Density);
        var anyVisibleAbove = false;

        for (var i = 0; i < _separators.Count; i++)
        {
            var row = _rows[i];
            var separator = _separators[i];

            separator.IsVisible = style.ShowSeparators && row.IsVisible && anyVisibleAbove;
            separator.HeightRequest = hairline;
            separator.Color = style.SeparatorColor;

            var leading = row is SpineRow spineRow ? spineRow.TextInset : SpineRowStyleOptions.Resolve(null).Padding.Left;
            separator.Margin = new Thickness(leading, 0, style.SeparatorTrailingInset, 0);

            anyVisibleAbove |= row.IsVisible;
        }
    }

    void ApplyText()
    {
        _header.Text = Header;
        _header.IsVisible = !string.IsNullOrEmpty(Header);
        _footer.Text = Footer;
        _footer.IsVisible = !string.IsNullOrEmpty(Footer);
    }

    // A group lower than twice the radius, such as one short row, is a capsule as in iOS Settings;
    // a larger radius would make the arcs overlap.
    void ApplyShape()
    {
        var radius = SpineSectionStyleOptions.Resolve(StyleOptions).CornerRadius;

        if (_group.Height > 0)
            radius = Math.Min(radius, _group.Height / 2);

        if (_group.StrokeShape is RoundRectangle { CornerRadius: var current } && current == new CornerRadius(radius))
            return;

        _group.StrokeShape = new RoundRectangle { CornerRadius = radius };
    }

    void Repaint()
    {
        var style = SpineSectionStyleOptions.Resolve(StyleOptions);

        if (style.SeparateRows != _separated)
            Rebuild();

        _group.BackgroundColor = _separated ? Colors.Transparent : style.BackgroundColor;
        _group.Stroke = !_separated && style.BorderColor is { } border ? new SolidColorBrush(border) : null;
        _group.StrokeThickness = _separated || style.BorderColor is null ? 0 : 1;
        _stack.Spacing = _separated ? style.RowSpacing : 0;
        PaintCards(style);
        ApplyShape();

        _header.TextColor = style.HeaderColor;
        _header.FontSize = style.HeaderFontSize;
        _header.FontAttributes = style.HeaderFontAttributes;
        _header.FontFamily = style.FontFamily;
        _header.Padding = new Thickness(style.TextInset, 0);

        _footer.TextColor = style.FooterColor;
        _footer.FontSize = style.FooterFontSize;
        _footer.FontFamily = style.FontFamily;
        _footer.Padding = new Thickness(style.TextInset, 0);

        UpdateSeparators();
    }
}
