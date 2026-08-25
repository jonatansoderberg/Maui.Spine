using System.Collections;

namespace Orientera.Controls;

/// <summary>One key figure: what it is, what it says, and what it is measured in.</summary>
public sealed record Stat(string Caption, string Value, string Unit = "");

/// <summary>
/// Two or three key figures side by side, separated by hairlines.
/// </summary>
/// <remarks>
/// The figures are set at heading size rather than display size. Three numbers in display beside
/// each other are three headings competing, and the row is meant to be read as one thing — a
/// result, not a scoreboard.
/// <para>
/// The hairline does the separating, not space. Three columns held apart by gaps alone drift into
/// one another as soon as one value is long, and "1:12:48" beside "5:21" is exactly that case.
/// </para>
/// <para>
/// One element to a screen reader: six labels are six swipes for a sentence that is read in one.
/// </para>
/// </remarks>
public sealed class StatRow : ContentView
{
    public static readonly BindableProperty StatsProperty =
        BindableProperty.Create(nameof(Stats), typeof(IEnumerable), typeof(StatRow), null,
            propertyChanged: (b, _, _) => ((StatRow)b).Apply());

    private readonly Grid _grid = new() { ColumnSpacing = 14 };

    public StatRow()
    {
        Content = _grid;

        Apply();
    }

    public IEnumerable? Stats
    {
        get => (IEnumerable?)GetValue(StatsProperty);
        set => SetValue(StatsProperty, value);
    }

    private void Apply()
    {
        var stats = Stats?.OfType<Stat>().ToList() ?? [];

        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();

        for (var i = 0; i < stats.Count; i++)
        {
            if (i > 0)
            {
                _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                Add(Hairline(), _grid.ColumnDefinitions.Count - 1);
            }

            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Add(Column(stats[i]), _grid.ColumnDefinitions.Count - 1);
        }

        _grid.IsVisible = stats.Count > 0;

        SemanticProperties.SetDescription(this, string.Join(", ", stats.Select(Sentence)));
        AutomationProperties.SetIsInAccessibleTree(_grid, false);

        void Add(View view, int column)
        {
            Grid.SetColumn(view, column);
            _grid.Children.Add(view);
        }
    }

    private static string Sentence(Stat stat) =>
        string.IsNullOrWhiteSpace(stat.Unit)
            ? $"{stat.Caption} {stat.Value}"
            : $"{stat.Caption} {stat.Value} {stat.Unit}";

    private static View Column(Stat stat)
    {
        var caption = Text(stat.Caption, "StatCaptionLabel");
        var value = Text(stat.Value, "StatValueLabel");

        var column = new VerticalStackLayout { Spacing = 1, Children = { caption, value } };

        if (!string.IsNullOrWhiteSpace(stat.Unit))
            column.Children.Add(Text(stat.Unit, "StatCaptionLabel"));

        return column;
    }

    private static Label Text(string text, string style)
    {
        var label = new Label { Text = text };

        label.SetDynamicResource(StyleProperty, style);
        AutomationProperties.SetIsInAccessibleTree(label, false);

        return label;
    }

    /// <summary>
    /// Inset top and bottom rather than run the full height: a line that reaches the row's edges
    /// reads as a table's border, and this is a separator inside one card.
    /// </summary>
    private static View Hairline()
    {
        var line = new BoxView { WidthRequest = 1, Margin = new Thickness(0, 4) };

        line.SetDynamicResource(BoxView.ColorProperty, "Outline");
        AutomationProperties.SetIsInAccessibleTree(line, false);

        return line;
    }
}
