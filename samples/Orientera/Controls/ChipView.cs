using System.Windows.Input;

namespace Orientera.Controls;

/// <summary>
/// A selectable chip: quick filters on Tävlingar, the result tabs, the live scope switch.
/// </summary>
/// <remarks>
/// Selection swaps between two pre-built Borders rather than flipping colours with a
/// <c>DataTrigger</c>. A trigger remembers the property value it replaced, and that memory is
/// captured once — after a light/dark swap it restores the *old* theme's colour, which left
/// unselected chips rendering as light pills on a dark page. Two styled Borders and an
/// <c>IsVisible</c> toggle keep every colour resolving through <c>{DynamicResource}</c>.
/// </remarks>
public sealed class ChipView : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(ChipView), string.Empty,
            propertyChanged: (b, _, _) => ((ChipView)b).ApplyText());

    public static readonly BindableProperty IsSelectedProperty =
        BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(ChipView), false,
            propertyChanged: (b, _, _) => ((ChipView)b).ApplySelection());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(ChipView));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(ChipView));

    private readonly Label _restLabel = new();
    private readonly Label _selectedLabel = new();
    private readonly Border _rest;
    private readonly Border _selected;

    public ChipView()
    {
        _rest = Build(_restLabel, "Chip", "ChipLabel");
        _selected = Build(_selectedLabel, "ChipSelected", "ChipSelectedLabel");

        Content = new Grid { Children = { _rest, _selected } };

        GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (Command?.CanExecute(CommandParameter) == true)
                    Command.Execute(CommandParameter);
            }),
        });

        ApplySelection();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

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

    private static Border Build(Label label, string borderStyle, string labelStyle)
    {
        label.SetDynamicResource(StyleProperty, labelStyle);

        var border = new Border { Content = label };
        border.SetDynamicResource(StyleProperty, borderStyle);

        return border;
    }

    private void ApplyText()
    {
        _restLabel.Text = Text;
        _selectedLabel.Text = Text;
    }

    private void ApplySelection()
    {
        _rest.IsVisible = !IsSelected;
        _selected.IsVisible = IsSelected;
    }
}
