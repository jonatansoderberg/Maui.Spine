using Orientera.Domain;
using Orientera.Presentation;

namespace Orientera.Features.Events.Participants;

/// <summary>
/// One runner at one radio control: accumulated time, the standing at that control, and the
/// time behind whoever leads it. The finish is the last column and reads the same way.
/// </summary>
public sealed partial class ParticipantCell : ObservableObject
{
    /// <summary>The control as it is written in the forest, or "Mål" for the finish column.</summary>
    public required string Control { get; init; }

    [ObservableProperty] public partial string TimeText { get; set; } = "—";

    /// <summary>Place and time behind on one line: "(3) +1:07".</summary>
    [ObservableProperty] public partial string DetailText { get; set; } = string.Empty;

    /// <summary>The control's leader, marked in the accent colour.</summary>
    [ObservableProperty] public partial bool IsLeading { get; set; }

    /// <summary>
    /// A cell is its own element to a screen reader — a row of twelve unlabelled numbers is
    /// unreadable, so every cell says which control it belongs to.
    /// </summary>
    [ObservableProperty] public partial string Accessibility { get; set; } = string.Empty;

    public void Update(TimeSpan? time, int? place, TimeSpan? behind)
    {
        TimeText = time is { } t ? Format.Time(t) : "—";
        IsLeading = place == 1;

        DetailText = (place, behind) switch
        {
            (null, _) => string.Empty,
            ({ } p, { Ticks: > 0 } b) => $"({p}) {Format.Delta(b)}",
            ({ } p, _) => $"({p})",
        };

        Accessibility = time is null
            ? $"{Control}, ingen tid"
            : string.Join(", ", new[]
            {
                Control,
                Format.SpokenTime(time),
                place is null ? null : Format.SpokenPlace(place),
                behind is { Ticks: > 0 } ? Format.SpokenDelta(behind) : null,
            }.OfType<string>());
    }
}

/// <summary>
/// One runner in a competition's participant list, in whichever mode the list is showing.
/// </summary>
/// <remarks>
/// One row anatomy for all four modes (P9): the leading mark, the identity, and the value with
/// what qualifies it. What changes between modes is what goes in each place — a start time or a
/// placing, points on Sverigelistan or a gap to the winner — never the places themselves.
/// <para>
/// The live table is the exception that proves it: the same row grows a cell per radio control.
/// The cells are built once and written into by each poll, so the table never relays under a
/// finger that is scrolling it.
/// </para>
/// </remarks>
public sealed partial class ParticipantRow : ObservableObject
{
    public required PersonId Person { get; init; }
    public required string Name { get; init; }
    public required string Club { get; init; }

    /// <summary>The club's badge, or null for a club that has not uploaded one.</summary>
    public string? ClubLogo { get; init; }

    public bool HasClubLogo => !string.IsNullOrEmpty(ClubLogo);
    public required string Class { get; init; }

    /// <summary>The row for the user gets an accent tone, per the live-list design rule.</summary>
    public required bool IsMe { get; init; }

    public required bool IsInMyGroup { get; init; }

    public string GroupGlyph => IsInMyGroup ? "★" : string.Empty;

    /// <summary>
    /// The mark in front of the name: the order on Sverigelistan, or the placing. Empty where the
    /// list's own order is the answer — an entry list has no ranking, and a live table's places
    /// belong to the control they were measured at.
    /// </summary>
    [ObservableProperty] public partial string LeadText { get; set; } = string.Empty;

    /// <summary>A medal instead of a number, for the three placings that have one.</summary>
    [ObservableProperty] public partial string MedalText { get; set; } = string.Empty;

    public bool HasMedal => MedalText.Length > 0;

    /// <summary>The value: a start time, a finishing time. Empty when the mode has none.</summary>
    [ObservableProperty] public partial string ValueText { get; set; } = string.Empty;

    /// <summary>What qualifies the value: the gap to the winner, the points, the national rank.</summary>
    [ObservableProperty] public partial string ValueDetailText { get; set; } = string.Empty;

    /// <summary>
    /// Whether there is a race behind the row to open. True only where the source carries one —
    /// a published result has splits and an analysis; an entry has a name and nothing more.
    /// </summary>
    public bool CanOpen { get; init; }

    /// <summary>Only what the row cannot otherwise say: a start time, a broken race, a mispunch.</summary>
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty] public partial bool HasStatus { get; set; }

    /// <summary>
    /// One cell per column of the class' split table, in course order with the finish last.
    /// Empty in every mode but Live.
    /// </summary>
    public IReadOnlyList<ParticipantCell> Cells { get; init; } = [];

    /// <summary>
    /// Who the row is, for a screen reader. In the live table the numbers stay in the cells, each
    /// read as its own element with the control it belongs to.
    /// </summary>
    [ObservableProperty]
    public partial string Accessibility { get; set; } = string.Empty;

    public void UpdateAccessibility()
    {
        var parts = new List<string>(8);

        if (IsMe)
            parts.Add("du");

        parts.Add(Name);

        if (IsInMyGroup)
            parts.Add("i min grupp");

        parts.Add($"{Club}, klass {Class}");

        if (SpokenValue.Length > 0)
            parts.Add(SpokenValue);

        if (HasStatus)
            parts.Add(StatusText);

        Accessibility = string.Join(", ", parts);
    }

    /// <summary>
    /// The value as it should be read aloud rather than as it is drawn — "3:e" is read "3 e",
    /// and a clock time is not a duration.
    /// </summary>
    [ObservableProperty] public partial string SpokenValue { get; set; } = string.Empty;
}

/// <summary>One class' rows, with the class and its radio controls as the table's heading.</summary>
public sealed class ParticipantClassGroup(string _name, IReadOnlyList<string> _columns)
    : System.Collections.ObjectModel.ObservableCollection<ParticipantRow>
{
    public string Name => _name;

    /// <summary>The column headings: each radio control, then the finish. Empty outside Live.</summary>
    public IReadOnlyList<string> Columns => _columns;

    public string Accessibility => $"Klass {_name}";
}
