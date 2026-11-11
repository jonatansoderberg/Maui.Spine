using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages.Masonry;

/// <summary>A card on the page: a tag in the card's colour, a title and a text of any length.</summary>
public sealed record MasonryCard(string Tag, string Title, string Body, Color Accent);

public partial class MasonryPageViewModel : SampleViewModel
{
    // Texts of different lengths, so the cards have different heights and each goes to the shortest column.
    public ObservableCollection<MasonryCard> Cards { get; } =
    [
        new("WEATHER", "Sunny, 21°", "Light wind from the west.", Color.FromArgb("#FF9500")),
        new("CALENDAR", "Three meetings", "Standup at 9:30, design review at 13:00 and a one-to-one at 16:00. The review has a new room: the one on the fourth floor, by the stairs.", Color.FromArgb("#FF3B30")),
        new("STEPS", "8 412", "Two thirds of today's goal.", Color.FromArgb("#34C759")),
        new("NEWS", "Rail line reopens", "After four months of work the line through the valley opens again on Monday, with trains every half hour and a new stop at the hospital. Buses keep running until Friday.", Color.FromArgb("#007AFF")),
        new("MUSIC", "Now playing", "A long album, quietly.", Color.FromArgb("#AF52DE")),
        new("HOME", "Front door locked", "Locked at 08:12. The garage is closed, the lights are off, and the heating holds 20°.", Color.FromArgb("#5AC8FA")),
        new("BATTERY", "Phone 64 %", "Watch 81 %, headphones 30 %.", Color.FromArgb("#30B0C7")),
        new("NOTES", "Shopping", "Bread, coffee, apples, the good cheese, candles for Friday and something for the neighbours' cat while they are away.", Color.FromArgb("#FFCC00")),
        new("STOCKS", "+1.2 %", "The index rose after lunch.", Color.FromArgb("#32D74B")),
    ];

    [ObservableProperty]
    public partial double MinColumnWidth { get; set; } = 160;

    [ObservableProperty]
    public partial int MaxColumns { get; set; } = 3;

    [ObservableProperty]
    public partial double Spacing { get; set; } = 12;

    [ObservableProperty]
    public partial string LastMove { get; set; } = "Hold a card, or press it and move the pointer, then drop it on another card's place.";

    /// <summary>How wide the window must be for a number of columns (set by the page).</summary>
    public Func<int, double>? WindowWidthFor { get; set; }

    [RelayCommand]
    private Task ShowOptions() => ShowOptionsAsync("Columns",
        new SliderOption("Narrowest column, points", 100, 400, () => MinColumnWidth, v => MinColumnWidth = Math.Round(v), "0"),
        new SliderOption("Most columns", 1, 5, () => MaxColumns, v => MaxColumns = (int)Math.Round(v), "0"),
        new SliderOption("Spacing, points", 0, 32, () => Spacing, v => Spacing = Math.Round(v), "0"));

    // Spine has already moved the card in Cards; the command is where an app saves the order.
    [RelayCommand]
    private void Moved(ReorderMove move) =>
        LastMove = $"{((MasonryCard)move.Item).Title}: from {move.From + 1} to {move.To + 1}.";

    /// <summary>
    /// On the desktop, right-clicking the header bar (Mac) or the title row (Windows, also Alt+Space) offers a window for
    /// one column of cards, two or three, as high as the page needs.
    /// </summary>
    public override IReadOnlyList<WindowSize> SuggestWindowSizes(Size screen)
    {
        if (WindowWidthFor is not { } widthFor)
            return [];
        var sizes = new List<WindowSize>();
        string[] symbols = ["rectangle", "rectangle.split.2x1", "rectangle.split.3x1"];
        for (var columns = 1; columns <= Math.Min(3, MaxColumns); columns++)
        {
            var width = widthFor(columns);
            if (width > screen.Width || WindowHeightFor(width) is not { } height)
                break;
            sizes.Add(new WindowSize(columns == 1 ? "One column" : $"{columns} columns",
                new Size(Math.Ceiling(width), Math.Ceiling(Math.Min(screen.Height, height))), symbols[columns - 1]));
        }
        return sizes;
    }
}
