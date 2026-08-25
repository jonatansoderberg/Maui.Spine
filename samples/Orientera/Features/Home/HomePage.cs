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

    /// <summary>
    /// Var oskärpan bakom statusfältet börjar respektive är helt inne, mätt i skrollsträcka.
    /// </summary>
    /// <remarks>
    /// Den stora hälsningen rör sig halva skrollsträckan, så dess underkant möter statusfältet
    /// efter dryga hundra punkters skroll. Bandet och den lilla rubriken är helt inne strax innan
    /// dess — de ska ligga där när den stora texten passerar, inte tona fram medan den redan är i
    /// vägen.
    /// </remarks>
    private const double BlurFadeStart = 60;
    private const double BlurFadeEnd = 100;

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
    /// I studsen ovanför toppen är sträckan negativ, och då tas den tillbaka helt i stället för
    /// till hälften: hjälten står stilla i överkanten medan korten dras nedåt. Utan det följer den
    /// med studsen ned och blottar sidans tomma yta ovanför sig — och eftersom korten fortsätter
    /// nedåt är det mer av fotografiet som kommer fram, vilket är vad gesten borde ge.
    /// </para>
    /// </remarks>
    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        Hero.TranslationY = e.ScrollY < 0 ? e.ScrollY : e.ScrollY * ParallaxFactor;

        // Bandet och den lilla rubriken är en och samma sak för ögat, och tonas därför som en.
        // Den stora hälsningen tonas mot dem: när den ena är inne är den andra borta, och
        // överlämningen följer fingret i stället för att spelas upp som en egen animation — en
        // animation med egen längd hinner ifatt sig själv när man skrollar upp igen.
        var collapsed = Math.Clamp((e.ScrollY - BlurFadeStart) / (BlurFadeEnd - BlurFadeStart), 0, 1);

        TopBlur.Opacity = TopTitle.Opacity = collapsed;
        Hero.TextOpacity = 1 - collapsed;
    }
}
