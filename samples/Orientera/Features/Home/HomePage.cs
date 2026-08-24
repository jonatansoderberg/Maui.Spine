namespace Orientera.Features.Home;

/// <summary>
/// Hem, utan rubrikrad.
/// </summary>
/// <remarks>
/// Sidan säger redan vem den talar till — "Hej Jonatan" står först på den — och "HEM" ovanför det
/// är fliken man just tryckte på, upprepad. Tidsmaskinen låg här som enda knapp och finns under
/// Jag, där resten av inställningarna bor. Med båda borta finns det inget kvar i raden att rita,
/// och Spine fäller ihop den helt i stället för att lämna ett tomt band.
/// </remarks>
[NavigableTab(
    Title = "Hem",
    Icon = "tab_home.svg",
    Order = 0,
    IsHeaderBarVisible = false)]
public partial class HomePage { public HomePage() => InitializeComponent(); }
