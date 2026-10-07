using System.Collections.ObjectModel;
using System.ComponentModel;
using Plugin.Maui.Spine.Extensions;

namespace MauiSpineSampleApp.Pages.Reorder;

public partial class ReorderPageViewModel : SampleViewModel
{
    private readonly ChoiceGroup _mode, _layout;
    private readonly PageAction _edit;

    public ReorderPageViewModel()
    {
        _mode = new("Mode",
        [
            new("Long-press", "Hold a card anywhere until it lifts, then drag it. A normal drag still scrolls.", () => (Mode, EditButton) = (ReorderMode.LongPress, false)),
            new("Handle", "The grip picks the card up the moment it is touched; the rest of the card scrolls and taps as usual.", () => (Mode, EditButton) = (ReorderMode.Handle, false)),
            new("Edit button", "Handles with Reorder.IsEnabled bound to an edit state: the grips show only while editing. Tap Edit at the top right, drag, then Done.", () => (Mode, EditButton) = (ReorderMode.Handle, true)),
        ]);

        _layout = new("Layout",
        [
            new("List", "One card per row; a card moves up and down only.", () => Grid = false),
            new("Grid", "Two cards per row; a card moves in any direction.", () => Grid = true),
        ]);

        _mode.Select(1);
        _layout.Select(0);

        // The header bar has one trailing slot; Edit takes it from the base class's theme menu.
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
        _edit = new PageAction("Edit", ToggleEditingCommand) { IsVisible = false };
        PageActions.Add(_edit);
    }

    // Spine moves the card here when it is dropped, and the list animates the move.
    public ObservableCollection<ReorderCard> Cards { get; } =
    [
        new("Sunrise and sunset", "When the sun comes up and goes down today", "weather1.svg"),
        new("Moon", "The phase, and when the moon rises", "weather1n.svg"),
        new("Day length", "How much longer the day is than yesterday", "timer.svg"),
        new("Name days", "Whose name day it is", "calendar.svg"),
        new("Week", "The week number and what is left of it", "stack.svg"),
        new("Weather", "Today's high and low, and rain by the hour", "water.svg"),
        new("Battery", "How long the phone lasts at this pace", "energy.svg"),
        new("Steps", "Steps so far against today's goal", "vertical.svg"),
        new("Alarm", "The next alarm and how long until it rings", "bell.svg"),
        new("Notes", "The last note you wrote", "edit.svg"),
        new("Language", "The word of the day in another language", "globe.svg"),
        new("Photos", "One picture from this day in earlier years", "image.svg"),
    ];

    public IReadOnlyList<ChoiceGroup> Groups => [_mode, _layout];

    [ObservableProperty]
    public partial ReorderMode Mode { get; set; } = ReorderMode.Handle;

    // Whether an Edit button in the header turns reordering on and off.
    [ObservableProperty]
    public partial bool EditButton { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    // Reorder.IsEnabled: always without the Edit button, only while editing with it.
    public bool CanReorder => !EditButton || IsEditing;

    [ObservableProperty]
    public partial bool Grid { get; set; }

    [ObservableProperty]
    public partial string LastMove { get; set; } = "Nothing moved yet.";

    public bool IsList => !Grid;

    [RelayCommand]
    private Task ShowOptions() => ShowOptionsAsync("Reorder", [.. Groups]);

    [RelayCommand]
    private void ToggleEditing() => IsEditing = !IsEditing;

    // Runs once after the drop, with the card already in its new place: save the order here.
    [RelayCommand]
    private void Moved(ReorderMove move) =>
        LastMove = $"{((ReorderCard)move.Item).Title}: from {move.From + 1} to {move.To + 1}.";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(EditButton):
                _edit.IsVisible = EditButton;
                IsEditing = false;
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanReorder)));
                break;
            case nameof(IsEditing):
                _edit.Text = IsEditing ? "Done" : "Edit";
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(CanReorder)));
                break;
            case nameof(Grid):
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsList)));
                break;
        }

        if (e.PropertyName is nameof(Mode) or nameof(EditButton) or nameof(Grid))
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Code)));
    }

    /// <summary>The XAML and C# for the combination on screen.</summary>
    public string Code
    {
        get
        {
            var layout = Grid ? "\n    ItemsLayout=\"VerticalGrid, 2\"" : "";
            var editing = EditButton ? "\n    Reorder.IsEnabled=\"{Binding IsEditing}\"" : "";
            var handle = Mode == ReorderMode.LongPress
                ? ""
                : "\n      <Image Grid.Column=\"2\"\n             SvgImageSource.Svg=\"griphorizontal.svg\"\n             Reorder.IsHandle=\"True\" />";

            return $"<CollectionView\n    ItemsSource=\"{{Binding Cards}}\"{layout}\n    Reorder.Mode=\"{Mode}\"{editing}\n    Reorder.Command=\"{{Binding MovedCommand}}\">\n  <CollectionView.ItemTemplate>\n    <DataTemplate>\n      <Grid ColumnDefinitions=\"Auto,*,Auto\"\n            Semantic.Merge=\"True\">\n      ...{handle}\n      </Grid>\n    </DataTemplate>\n  </CollectionView.ItemTemplate>\n</CollectionView>\n\n// After the drop; Cards has the new order:\n[RelayCommand]\nvoid Moved(ReorderMove move) =>\n  Save(Cards);";
        }
    }
}

public sealed record ReorderCard(string Title, string Text, string Icon);
