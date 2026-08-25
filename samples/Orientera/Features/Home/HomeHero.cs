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
/// Det är gradienten och inte bilden som gör vit text läsbar. Mätt mot bildens ljusaste pixel
/// under textytan: 2.25:1 utan den, 6.4:1 med. Klarar en bild inte kravet byts bilden, aldrig
/// texten.
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

        Content = new Grid { Children = { image, Scrim(), Text() } };

        SetBinding(HeightRequestProperty, new Binding(nameof(HomePageViewModel.HeroHeight)));
    }

    private static Border Scrim()
    {
        var top = new GradientStop { Offset = 0 };
        top.SetDynamicResource(GradientStop.ColorProperty, "HeroScrim");

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
                    new GradientStop { Color = Colors.Transparent, Offset = 1 },
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
