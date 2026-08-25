using System.Collections;
using System.Collections.Specialized;
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
            propertyChanged: (b, old, now) => ((SegmentBar)b).Adopt(old, now));

    public static readonly BindableProperty SelectedValueProperty =
        BindableProperty.Create(nameof(SelectedValue), typeof(object), typeof(SegmentBar), null,
            propertyChanged: (b, _, _) => ((SegmentBar)b).ApplySelection());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SegmentBar),
            propertyChanged: (b, _, _) => ((SegmentBar)b).Rebuild());

    private readonly ScrollView _scroll = new()
    {
        Orientation = ScrollOrientation.Horizontal,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
    };

    public SegmentBar() => Content = _scroll;

    private IEnumerable<ChipView> Chips =>
        _scroll.Content is HorizontalStackLayout row ? row.OfType<ChipView>() : [];

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

    /// <summary>
    /// Takes a new source, and keeps listening to it if it is one that changes.
    /// </summary>
    /// <remarks>
    /// Rebuilding only when the property is *replaced* is enough for a fixed set of segments —
    /// which is all this control had until the participant list's four modes, whose availability
    /// is decided after the page has loaded and its bindings are up. Filled into the collection
    /// that was already bound, the segments never reached the bar and the switcher rendered as
    /// nothing at all.
    /// </remarks>
    private void Adopt(object? previous, object? current)
    {
        if (previous is INotifyCollectionChanged before)
            before.CollectionChanged -= OnItemsChanged;

        if (current is INotifyCollectionChanged after)
            after.CollectionChanged += OnItemsChanged;

        Rebuild();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>
    /// Builds the row afresh and hands it to the scroll view whole.
    /// </summary>
    /// <remarks>
    /// A new layout rather than adding to the one already there. A horizontal
    /// <see cref="ScrollView"/> takes its content size from its content when that content is
    /// <em>set</em>; segments added to a layout it had already measured at nothing stayed at
    /// nothing, and the bar drew an empty row of the right height. Harmless for a fixed set of
    /// segments — which is all this control had until availability began arriving after the page
    /// was up.
    /// </remarks>
    private void Rebuild()
    {
        var row = new HorizontalStackLayout { Spacing = 8 };

        foreach (var item in ItemsSource?.OfType<Segment>() ?? [])
        {
            row.Add(new ChipView
            {
                Text = item.Text,
                Command = Command,
                CommandParameter = item.Value,
                IsEnabled = item.IsEnabled,
                // A segment with nothing behind it is still readable — it says what will be there.
                Opacity = item.IsEnabled ? 1 : 0.5,
            });
        }

        _scroll.Content = row;

        ApplySelection();
    }

    private void ApplySelection()
    {
        foreach (var chip in Chips)
            chip.IsSelected = Equals(chip.CommandParameter, SelectedValue);
    }
}
