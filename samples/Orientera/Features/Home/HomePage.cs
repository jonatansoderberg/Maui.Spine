namespace Orientera.Features.Home;

/// <summary>
/// Hem, utan rubrikrad och utan toppinset.
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
    IsHeaderBarVisible = false,
    // Hjälten går under statusfältet. Spine paddar annars toppen ur UIWindow.SafeAreaInsets, och
    // MAUI-nivåns SafeAreaEdges kommer inte åt den paddingen — den sitter på Spines innehållsvärd.
    // Med toppen borttagen rapporteras statusfältets höjd i SafeAreaInsets i stället, och
    // hälsningen paddar sig själv med den.
    SafeAreaEdges = SafeAreaEdges.Left | SafeAreaEdges.Right)]
public partial class HomePage
{
    /// <summary>
    /// Hur stor del av skrollsträckan hjälten följer med. Noll skulle spika fast den, ett vore
    /// ingen parallax alls.
    /// </summary>
    private const double ParallaxFactor = 0.5;

    public HomePage() => InitializeComponent();

    /// <summary>
    /// Bilden skrollar långsammare än korten, så djupet mellan dem syns i rörelsen och inte bara
    /// i överlappet.
    /// </summary>
    /// <remarks>
    /// Hjälten ligger i skrollytan och flyttas alltså redan uppåt med hela sträckan; översättningen
    /// nedåt tar tillbaka hälften. Nettot blir att den rör sig halva vägen, och eftersom
    /// översättningen aldrig överstiger sträckan kan bildens överkant inte hamna nedanför
    /// skärmkanten och lämna en glipa efter sig.
    /// <para>
    /// Klämd vid noll för studsen i överkanten: där är sträckan negativ, och en negativ
    /// översättning hade dragit upp bilden och blottat ytan ovanför den.
    /// </para>
    /// </remarks>
    private void OnScrolled(object? sender, ScrolledEventArgs e) =>
        Hero.TranslationY = Math.Max(0, e.ScrollY) * ParallaxFactor;
}
