namespace MauiSpineSampleApp.Pages;

/// <summary>The settings an example lets you try, shown in <see cref="OptionsSheet"/>.</summary>
/// <param name="Options"><see cref="ChoiceGroup"/>, <see cref="ToggleOption"/>, <see cref="SliderOption"/> or <see cref="TextOption"/> items.</param>
public sealed record SampleOptions(string Title, IReadOnlyList<ObservableObject> Options);

public sealed partial class Choice(string label, string description, Action choose) : ObservableObject
{
    public string Label { get; } = label;

    public string Description { get; } = description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    internal ChoiceGroup? Group { get; set; }

    // The chip moves first, so a choice whose action does not reselect its group still shows as picked.
    [RelayCommand]
    private void Choose()
    {
        Group?.Choose(this);
        choose();
    }
}

/// <summary>
/// One setting with a few values, shown as chips; the choice's action changes the page. With
/// <see cref="IsMultiple"/> several can be on at once, and the page sets each chip's <see cref="Choice.IsSelected"/>.
/// </summary>
public sealed partial class ChoiceGroup : ObservableObject
{
    public ChoiceGroup(string name, IReadOnlyList<Choice> choices)
    {
        Name = name;
        Choices = choices;

        foreach (var choice in choices)
            choice.Group = this;
    }

    public string Name { get; }

    public IReadOnlyList<Choice> Choices { get; }

    public bool IsMultiple { get; init; }

    [ObservableProperty]
    public partial Choice? Selected { get; private set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    public void Select(Choice choice) => Select(Choices.ToList().IndexOf(choice));

    // With several on, the description is the last chip tapped
    internal void Choose(Choice choice)
    {
        if (IsMultiple)
            Selected = choice;
        else
            Select(choice);
    }

    /// <summary>Selects the choice at <paramref name="index"/>; -1 selects none.</summary>
    public void Select(int index)
    {
        for (var i = 0; i < Choices.Count; i++)
            Choices[i].IsSelected = i == index;

        Selected = index >= 0 ? Choices[index] : null;
    }
}

/// <summary>An on/off setting that reads and writes a property of the page.</summary>
public sealed partial class ToggleOption : ObservableObject
{
    private readonly Action<bool> _set;

    public ToggleOption(string name, string? description, Func<bool> get, Action<bool> set)
    {
        Name = name;
        Description = description;
        IsOn = get();
        _set = set;
    }

    public string Name { get; }

    public string? Description { get; }

    public bool HasDescription => Description is not null;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value) => _set?.Invoke(value);
}

/// <summary>A number setting that reads and writes a property of the page.</summary>
public sealed partial class SliderOption : ObservableObject
{
    private readonly Action<double> _set;

    public SliderOption(string name, double minimum, double maximum, Func<double> get, Action<double> set, string format = "0.00")
    {
        Name = name;
        Minimum = minimum;
        Maximum = maximum;
        Format = format;
        Value = get();
        _set = set;
    }

    public string Name { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public string Format { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    public partial double Value { get; set; }

    public string ValueText => Value.ToString(Format);

    partial void OnValueChanged(double value) => _set?.Invoke(value);
}

/// <summary>A text setting, such as a server's address, that reads and writes a property of the page.</summary>
public sealed partial class TextOption : ObservableObject
{
    private readonly Action<string> _set;

    public TextOption(string name, string? description, Func<string> get, Action<string> set, Microsoft.Maui.Keyboard? keyboard = null)
    {
        Name = name;
        Description = description;
        Keyboard = keyboard ?? Microsoft.Maui.Keyboard.Default;
        Text = get();
        _set = set;
    }

    public string Name { get; }

    public string? Description { get; }

    public bool HasDescription => Description is not null;

    public Microsoft.Maui.Keyboard Keyboard { get; }

    [ObservableProperty]
    public partial string Text { get; set; }

    partial void OnTextChanged(string value) => _set?.Invoke(value);
}

/// <summary>Picks the row template for each kind of option.</summary>
public sealed class OptionTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Choice { get; set; }

    public DataTemplate? Toggle { get; set; }

    public DataTemplate? Slider { get; set; }

    public DataTemplate? Text { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container) => item switch
    {
        ChoiceGroup => Choice,
        ToggleOption => Toggle,
        SliderOption => Slider,
        TextOption => Text,
        _ => null,
    };
}
