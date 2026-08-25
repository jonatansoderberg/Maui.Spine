using System.Collections.ObjectModel;

namespace Orientera.Controls;

/// <summary>One chip in a chip row: a choice, and whether it is taken.</summary>
/// <remarks>
/// The toggle sits on the option rather than on the sheet, so the chip binds to itself. Reaching
/// the sheet's command through <c>RelativeSource AncestorType</c> from inside a bindable layout
/// resolved to nothing once already, and a chip that silently does not toggle is worse than one
/// line of command here.
/// </remarks>
public sealed partial class ChipOption : ObservableObject
{
    public required string Label { get; init; }

    /// <summary>A district name, a level, a discipline, a period, a radius — or null for "any".</summary>
    public required object? Value { get; init; }

    public required ChipGroup Group { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Toggle() => Group.Toggle(this);
}

/// <summary>
/// A row of chips. Multi-select rows toggle; single-select rows move the selection, because one
/// window and one radius is all a filter can mean, and their first chip is the "any" that means
/// the row is unset.
/// </summary>
public sealed partial class ChipGroup(bool single, string emptyLabel) : ObservableObject
{
    public ObservableCollection<ChipOption> Options { get; } = [];

    /// <summary>Raised after every tap, so the button can say what the filter would show.</summary>
    public Action? Changed { get; set; }

    /// <summary>
    /// What is chosen, for the collapsed header: "Mitt distrikt", "Mästerskap +2", or the row's
    /// own word for nothing chosen. This is what makes a closed group safe — the choices are out
    /// of sight, the choice never is.
    /// </summary>
    [ObservableProperty]
    public partial string Summary { get; set; } = emptyLabel;

    public IEnumerable<ChipOption> Selected => Options.Where(o => o.IsSelected);

    public void Toggle(ChipOption option)
    {
        if (single)
        {
            foreach (var other in Options)
                other.IsSelected = ReferenceEquals(other, option);
        }
        else
        {
            option.IsSelected = !option.IsSelected;
        }

        Describe();
        Changed?.Invoke();
    }

    public void Reset()
    {
        for (int i = 0; i < Options.Count; i++)
            Options[i].IsSelected = single && i == 0;

        Describe();
    }

    public ChipOption Add(string label, object? value, bool isSelected = false)
    {
        var option = new ChipOption { Label = label, Value = value, Group = this, IsSelected = isSelected };
        Options.Add(option);
        Describe();
        return option;
    }

    /// <summary>
    /// The "any" chip in a single-select row carries a null value, and it is not a choice — it is
    /// the absence of one, and reads as the empty label rather than as its own word.
    /// </summary>
    public void Describe()
    {
        var chosen = Options.Where(o => o is { IsSelected: true, Value: not null }).Select(o => o.Label).ToList();

        Summary = chosen.Count switch
        {
            0 => emptyLabel,
            1 => chosen[0],
            2 => $"{chosen[0]}, {chosen[1]}",
            _ => $"{chosen[0]} +{chosen.Count - 1}",
        };
    }
}
