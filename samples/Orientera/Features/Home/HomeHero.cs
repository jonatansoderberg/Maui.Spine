using Microsoft.Maui.Controls.Shapes;

namespace Orientera.Features.Home;

/// <summary>
/// Hälsningen på sin bild: vem sidan talar till, vilken dag det är, och vad det är för väder.
/// </summary>
/// <remarks>
/// En egen vy och inte XAML på sidan, eftersom den behövs på fem ställen — som listans huvud i
/// innehållsläget, och överst i vart och ett av de tre andra lägena (P10). Hjälten står kvar i
/// alla fyra: hälsningen är sann utan nätverk, och en sida som tappar sin rubrik när hämtningen
/// misslyckas ser trasig ut snarare än offline.
/// <para>
/// I innehållsläget ligger den i <c>CollectionView.Header</c> och skrollar alltså bort med
/// korten. Det är vad som gör att första kortet kan ligga *över* bilden: en hjälte som står
/// still hade tvingat korten att antingen klippas mot dess kant eller täcka hälsningen på väg
/// upp. Fällan som dokumenterades här tidigare — en header som mätts som tom växer inte när
/// texten kommer — undviks av att höjden är satt och känd innan något bundits.
/// </para>
/// <para>
/// Det är gradienten och inte bilden som gör vit text läsbar. Mätt mot den ljusaste pixeln inom
/// varje textrads egen bredd — inte inom en generös ruta runt den, vilket mäter sådant texten
/// aldrig ligger på: 3.84:1 på den svagaste raden utan det mjuka stoppet, 5.69:1 med. Klarar en
/// bild inte kravet ens med nedtoningen byts bilden, aldrig texten.
/// </para>
/// </remarks>
public sealed class HomeHero : ContentView
{
    public HomeHero()
    {
        // Bilden är stämning och inte plats (beslut D12) — det enda undantaget från P7, och ett
        // räknebart: exakt en bundlad icke-terrängbild, på exakt den här ytan.
        var image = new Image
        {
            Source = ImageSource.FromFile("hero_home.jpg"),
            Aspect = Aspect.AspectFill,
        };

        AutomationProperties.SetIsInAccessibleTree(image, false);

        Content = new Grid { Children = { image, Scrim(), Fade(), Text() } };

        SetBinding(HeightRequestProperty, new Binding(nameof(HomePageViewModel.HeroHeight)));
    }

    /// <summary>
    /// Nedtoningen som gör vit text läsbar.
    /// </summary>
    /// <remarks>
    /// Tre stopp och inte två. En rak toning från toppen är som svagast just där väderraden står,
    /// och där mäter bilden en solbelyst prick som ger 3.84:1 — under kravet. Ett andra stopp vid
    /// 40 % lyfter raden till 5.69:1.
    /// <para>
    /// Stoppet är mjukt och inte fullt: att hålla <c>HeroScrim</c> hela vägen genom textbandet
    /// hade gett 9.38:1 och en bild man inte ser. Nedtoningen ska göra texten läsbar, inte göra
    /// fotografiet till en yta.
    /// </para>
    /// </remarks>
    private static Border Scrim()
    {
        var top = new GradientStop { Offset = 0 };
        top.SetDynamicResource(GradientStop.ColorProperty, "HeroScrim");

        var soft = new GradientStop { Offset = 0.40f };
        soft.SetDynamicResource(GradientStop.ColorProperty, "HeroScrimSoft");

        return new Border
        {
            StrokeThickness = 0,
            InputTransparent = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                [
                    top,
                    soft,
                    new GradientStop { Color = Colors.Transparent, Offset = 1 },
                ],
            },
        };
    }

    /// <summary>
    /// Uttoningen i underkanten, som löser upp fotot i sidans yta i stället för att kapa det.
    /// </summary>
    /// <remarks>
    /// Ritad och inte inbakad i bilden. Originalet kom med en uttoning mot vitt, och en vit
    /// uttoning är rätt i exakt ett av två teman — i mörkt läge hade den lyst som ett band längs
    /// kanten. Den bakade uttoningen är därför bortbeskuren, och den här toningen går mot
    /// <c>SurfacePage</c>, vilket är vad som faktiskt ligger under bilden i båda temana.
    /// <para>
    /// Det som syns av den är de sexton punkterna på var sida om första kortet, och kanten under
    /// det. Det är precis där bilden annars slutar tvärt.
    /// </para>
    /// </remarks>
    private static Border Fade()
    {
        var bottom = new GradientStop { Offset = 1 };
        bottom.SetDynamicResource(GradientStop.ColorProperty, "SurfacePage");

        return new Border
        {
            StrokeThickness = 0,
            InputTransparent = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                [
                    new GradientStop { Color = Colors.Transparent, Offset = 0.62f },
                    bottom,
                ],
            },
        };
    }

    /// <summary>Bilden går under statusfältet; texten insetar sig själv med den mätta höjden.</summary>
    private static View Text()
    {
        var greeting = Label("HeroGreetingLabel");
        greeting.SetBinding(Microsoft.Maui.Controls.Label.TextProperty,
            nameof(HomePageViewModel.Greeting));

        var today = Label("HeroMetaLabel");
        today.SetBinding(Microsoft.Maui.Controls.Label.TextProperty,
            nameof(HomePageViewModel.TodayText));

        // Ingen rad alls hellre än en gissad, och symbolen säger ingenting högt — därav den egna
        // beskrivningen.
        var weather = Label("HeroMetaLabel");
        weather.SetBinding(Microsoft.Maui.Controls.Label.TextProperty,
            nameof(HomePageViewModel.WeatherText));
        weather.SetBinding(IsVisibleProperty, nameof(HomePageViewModel.HasWeather));
        weather.SetBinding(SemanticProperties.DescriptionProperty,
            nameof(HomePageViewModel.WeatherDescription));

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Start,
            Children = { greeting, today, weather },
        };

        stack.SetBinding(PaddingProperty, nameof(HomePageViewModel.HeroPadding));

        return stack;
    }

    private static Label Label(string style)
    {
        var label = new Label();
        label.SetDynamicResource(StyleProperty, style);

        return label;
    }
}
