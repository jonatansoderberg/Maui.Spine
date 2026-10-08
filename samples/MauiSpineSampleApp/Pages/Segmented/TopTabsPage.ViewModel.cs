namespace MauiSpineSampleApp.Pages.Segmented;

public partial class TopTabsPageViewModel : SampleViewModel
{
    [ObservableProperty]
    public partial int Section { get; set; }

    public IReadOnlyList<RaceResult> ClassResults { get; } =
    [
        new(1, "Elin Andersson", "OK Linné", "24:12"),
        new(2, "Sara Lund", "IFK Göteborg", "24:48"),
        new(3, "Maja Berg", "Järfälla OK", "25:03"),
        new(4, "Karin Holm", "OK Linné", "25:40"),
        new(5, "Anna Ek", "Södertälje-Nykvarn", "26:11"),
        new(6, "Lisa Nord", "Hellas", "26:35"),
        new(7, "Emma Falk", "IFK Lidingö", "27:02"),
        new(8, "Ida Sjöberg", "OK Ravinen", "27:19"),
        new(9, "Moa Strand", "Tullinge SK", "27:48"),
        new(10, "Hanna Ljung", "Stora Tuna OK", "28:05"),
        new(11, "Tove Wik", "OK Linné", "28:31"),
        new(12, "Alva Dahl", "Lunds OK", "29:10"),
        new(13, "Wilma Ros", "Malungs OK", "29:44"),
        new(14, "Ebba Lind", "OK Hällen", "30:02"),
        new(15, "Klara Sund", "Bodens BK", "30:39"),
        new(16, "Saga Björk", "OK Tyr", "31:02"),
        new(17, "Elsa Nyberg", "Ärla IF", "31:25"),
        new(18, "Freja Lund", "OK Kåre", "31:58"),
        new(19, "Signe Ahl", "Gävle OK", "32:14"),
        new(20, "Astrid Hed", "OK Orion", "32:40"),
        new(21, "Nora Eklund", "Kalmar OK", "33:05"),
        new(22, "Vera Blom", "OK Gipan", "33:31"),
        new(23, "Ines Wall", "Sundsvalls OK", "34:02"),
        new(24, "Tilda Mark", "OK Pan Kristianstad", "34:45"),
        new(25, "Stina Rask", "Skogsluffarna", "35:20"),
        new(26, "Majken Ros", "OK Österåker", "36:08"),
        new(27, "Ronja Vik", "Leksands OK", "37:15"),
    ];

    public IReadOnlyList<RaceResult> ClubResults { get; } =
    [
        new(1, "Elin Andersson", "D21", "24:12"),
        new(4, "Karin Holm", "D21", "25:40"),
        new(2, "Johan Ek", "H21", "21:55"),
        new(7, "Per Lind", "H21", "23:08"),
        new(1, "Tove Wik", "D20", "26:31"),
        new(3, "Olle Berg", "H16", "19:47"),
        new(5, "Nils Holm", "H50", "27:20"),
        new(2, "Greta Falk", "D45", "25:58"),
    ];

    public DateTime OpenedAt { get; } = DateTime.Now;

    // Read when a binding asks: the third tab's template binds it the first time the tab is picked.
    public DateTime Now => DateTime.Now;
}

public sealed record RaceResult(int Place, string Name, string Detail, string Time);
