using System.Collections;
using System.Windows.Input;

namespace Orientera.Controls;

/// <summary>One choice in a <see cref="SegmentBar"/>.</summary>
/// <param name="Text">What the segment says.</param>
/// <param name="Key">What the command receives. The text itself when left out.</param>
/// <param name="IsEnabled">A segment with nothing behind it yet — a class the runner has not picked.</param>
public sealed record Segment(string Text, object? Key = null, bool IsEnabled = true)
{
    public object Value => Key ?? Text;
}

/// <summary>
/// The sub-tab row the concept puts on every page: Hem, Tävlingar, Live, Resultat.
/// </summary>
/// <remarks>
/// Wraps <see cref="ChipView"/> rather than replacing it — the chip already solves selection across
/// a theme swap, and that reasoning belongs in one place. What the bar adds is the row: even
/// spacing, horizontal scrolling when the segments do not fit, and a single selected value instead
/// of one bound boolean per chip.
/// </remarks>
public sealed class SegmentBar : ContentView
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(SegmentBar), null,
            propertyChanged: (b, _, _) => ((SegmentBar)b).Rebuild());

    public static readonly BindableProperty SelectedValueProperty =
        BindableProperty.Create(nameof(SelectedValue), typeof(object), typeof(SegmentBar), null,
            propertyChanged: (b, _, _) => ((SegmentBar)b).ApplySelection());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SegmentBar),
            propertyChanged: (b, _, _) => ((SegmentBar)b).Rebuild());

    private readonly HorizontalStackLayout _row = new() { Spacing = 8 };

    public SegmentBar()
    {
        Content = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _row,
        };
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    private void Rebuild()
    {
        _row.Clear();

        if (ItemsSource is null)
            return;

        foreach (var item in ItemsSource.OfType<Segment>())
        {
            _row.Add(new ChipView
            {
                Text = item.Text,
                Command = Command,
                CommandParameter = item.Value,
                IsEnabled = item.IsEnabled,
                // A segment with nothing behind it is still readable — it says what will be there.
                Opacity = item.IsEnabled ? 1 : 0.5,
            });
        }

        ApplySelection();
    }

    private void ApplySelection()
    {
        foreach (var chip in _row.OfType<ChipView>())
            chip.IsSelected = Equals(chip.CommandParameter, SelectedValue);
    }
}
