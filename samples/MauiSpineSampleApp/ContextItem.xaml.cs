namespace MauiBottomSheetPoc;

public partial class ContextItem : ContentView
{
    public ContextItem()
    {
        InitializeComponent();
    }

    // --- Icon (SVG filename, required) ---
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(ContextItem));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    // --- Title (required) ---
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(ContextItem));

    public new string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    // --- Description (optional, shown as AnimatedLabel) ---
    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(ContextItem),
            propertyChanged: (b, _, n) => ((ContextItem)b).OnDescriptionChanged((string)n));

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    private void OnDescriptionChanged(string? value)
    {
        if (DescriptionLabel is not null)
            DescriptionLabel.IsVisible = !string.IsNullOrWhiteSpace(value);
    }

    // --- Actions (optional right-aligned content: Switch, ImageButton, Slider, Label, etc.) ---
    public static readonly BindableProperty ActionsProperty =
        BindableProperty.Create(nameof(Actions), typeof(View), typeof(ContextItem),
            propertyChanged: (b, _, n) => ((ContextItem)b).OnActionsChanged((View?)n));

    public View? Actions
    {
        get => (View?)GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    private void OnActionsChanged(View? value)
    {
        if (ActionsContainer is not null)
        {
            ActionsContainer.Content = value;
            ActionsContainer.IsVisible = value is not null;
        }
    }
}