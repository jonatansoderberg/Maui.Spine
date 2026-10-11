using Microsoft.Maui.Layouts;

namespace SpineAvatarLab.Lab;

/// <summary>The lab's look: one palette for light and dark, cards, chips and segmented choices.</summary>
internal static class LabUi
{
    public static readonly Color Accent = Color.FromArgb("#167E8B");
    public static readonly Color AccentDark = Color.FromArgb("#2BB3C0");

    public static void Page(ContentPage page) => page.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#F3F5F8"), Color.FromArgb("#0E1217"));

    public static Label Title(string text) => Text(new Label { Text = text, FontSize = 13, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 0, 6) }, secondary: true);

    public static Label Text(Label label, bool secondary = false)
    {
        if (secondary)
            label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#5B6B7B"), Color.FromArgb("#93A1AF"));
        else
            label.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#16202B"), Color.FromArgb("#E7ECF2"));
        return label;
    }

    public static Label Small(bool mono = false, bool secondary = true) =>
        Text(new Label { FontSize = 12, FontFamily = mono ? "Menlo" : null, LineBreakMode = LineBreakMode.WordWrap }, secondary);

    public static Border Card(string title, params View[] content)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Add(Title(title.ToUpperInvariant()));
        foreach (var view in content)
            stack.Add(view);

        var card = new Border
        {
            Content = stack,
            Padding = new Thickness(14, 12),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
        };
        card.SetAppThemeColor(VisualElement.BackgroundColorProperty, Colors.White, Color.FromArgb("#181D24"));
        card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#E2E7EE"), Color.FromArgb("#252C36"));
        return card;
    }

    public static Button Chip(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 13,
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0, 0, 6, 6),
            CornerRadius = 15,
            MinimumHeightRequest = 30,
            BorderWidth = 0,
        };
        Select(button, false);
        button.Clicked += (_, _) => action();
        return button;
    }

    public static void Select(Button button, bool selected)
    {
        if (selected)
        {
            button.SetAppThemeColor(VisualElement.BackgroundColorProperty, Accent, AccentDark);
            button.SetAppThemeColor(Button.TextColorProperty, Colors.White, Color.FromArgb("#07181B"));
        }
        else
        {
            button.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#EDF1F6"), Color.FromArgb("#232A34"));
            button.SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#16202B"), Color.FromArgb("#DCE3EA"));
        }
    }

    public static FlexLayout Row(params View[] views)
    {
        var row = new FlexLayout { Wrap = FlexWrap.Wrap, AlignItems = FlexAlignItems.Center };
        foreach (var view in views)
            row.Add(view);
        return row;
    }

    public static Grid Labeled(string label, View view)
    {
        var grid = new Grid { ColumnDefinitions = [new(new GridLength(118)), new(GridLength.Star)], ColumnSpacing = 8 };
        grid.Add(Text(new Label { Text = label, VerticalOptions = LayoutOptions.Center, FontSize = 13 }));
        grid.Add(view, 1);
        return grid;
    }

    public static Switch Switch(bool on = false)
    {
        var view = new Switch { IsToggled = on };
        view.SetAppThemeColor(Microsoft.Maui.Controls.Switch.OnColorProperty, Accent, AccentDark);
        return view;
    }

    public static Slider Slider(double min, double max, double value)
    {
        var view = new Slider(min, max, value);
        view.SetAppThemeColor(Microsoft.Maui.Controls.Slider.MinimumTrackColorProperty, Accent, AccentDark);
        return view;
    }

    public static Picker Picker(IList<string> items, int selected)
    {
        var picker = new Picker { ItemsSource = items.ToList(), SelectedIndex = selected, FontSize = 13 };
        picker.SetAppThemeColor(Microsoft.Maui.Controls.Picker.TextColorProperty, Color.FromArgb("#16202B"), Color.FromArgb("#E7ECF2"));
        return picker;
    }
}

/// <summary>Chips where one is selected at a time: avatars, states, theme, motion, fps, size.</summary>
internal sealed class ChipGroup
{
    private readonly Dictionary<string, Button> _chips = new(StringComparer.Ordinal);

    public FlexLayout View { get; } = LabUi.Row();

    public string? Selected { get; private set; }

    public ChipGroup Add(string key, string text, Action action)
    {
        var chip = LabUi.Chip(text, () =>
        {
            Select(key);
            action();
        });
        _chips[key] = chip;
        View.Add(chip);
        return this;
    }

    public void Select(string? key)
    {
        Selected = key;
        foreach (var (name, chip) in _chips)
            LabUi.Select(chip, name == key);
    }

    public void Clear()
    {
        _chips.Clear();
        View.Clear();
    }
}
