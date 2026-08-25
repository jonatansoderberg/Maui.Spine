#if IOS || MACCATALYST
using CoreAnimation;
using Foundation;
using CoreGraphics;
using UIKit;
#endif

namespace Orientera.Controls;

/// <summary>
/// Bandet bakom statusfältet som gör klockan läsbar när innehåll skrollar in under den.
/// </summary>
/// <remarks>
/// Apples egen scroll edge-effekt, byggd för hand eftersom MAUI inte exponerar någon oskärpa. På
/// Apple-plattformarna är det en riktig <c>UIVisualEffectView</c> med systemets ultratunna
/// material — samma sort som flikraden vilar på. Det följer temat av sig självt och suddar det som passerar
/// under. Där ingen oskärpa finns att låna ritas i stället ett nästan täckande band i sidans egen
/// färg; sämre, men samma jobb.
/// <para>
/// Underkanten tonas ut över <see cref="FadeHeight"/> punkter. Utan det slutar oskärpan i en rak
/// linje tvärs över innehållet, och en skarp kant mitt på ett fotografi läses som ett fel snarare
/// än som en yta. Höjden ska därför vara statusfältet <em>plus</em> uttoningen — se
/// <see cref="DefaultFadeHeight"/>, som är det måttet.
/// </para>
/// <para>
/// Bandet tonas in av den som skrollar, inte av bandet självt. Att veta *när* det ska synas är
/// sidans sak: det beror på var texten under det står och hur fort den rör sig.
/// </para>
/// <para>
/// Genomskinlig för tryck och utanför tillgänglighetsträdet. Den är en yta ovanpå innehåll, och
/// får varken fånga en knapptryckning eller bli ett stopp för skärmläsaren.
/// </para>
/// </remarks>
public sealed class EdgeBlur : ContentView
{
    /// <summary>Hur många punkter uttoningen i underkanten tar. Sidan lägger till dem i höjden.</summary>
    public const double DefaultFadeHeight = 24;

    public static readonly BindableProperty FadeHeightProperty =
        BindableProperty.Create(nameof(FadeHeight), typeof(double), typeof(EdgeBlur), DefaultFadeHeight);

    public EdgeBlur()
    {
        InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(this, false);

        Content = Band();

#if IOS || MACCATALYST
        // Materialet tar över helt; färgbandet finns kvar som en tom platta under det.
        Content.Opacity = 0;
        HandlerChanged += (_, _) => AttachBlur();
        SizeChanged += (_, _) => LayoutBlur();
#else
        Content.Opacity = 0.8;
#endif
    }

    /// <summary>Uttoningens höjd i punkter, räknad från underkanten.</summary>
    public double FadeHeight
    {
        get => (double)GetValue(FadeHeightProperty);
        set => SetValue(FadeHeightProperty, value);
    }

    /// <summary>
    /// Reserven för plattformar utan oskärpa: sidans färg som tonar ut nedåt, med samma
    /// uttoningshöjd som masken ger materialet.
    /// </summary>
    private View Band()
    {
        var top = new GradientStop { Offset = 0 };
        top.SetDynamicResource(GradientStop.ColorProperty, "SurfacePage");

        var hold = new GradientStop { Offset = 0.7f };
        hold.SetDynamicResource(GradientStop.ColorProperty, "SurfacePage");

        return new Border
        {
            StrokeThickness = 0,
            InputTransparent = true,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = [top, hold, new GradientStop { Color = Colors.Transparent, Offset = 1 }],
            },
        };
    }

#if IOS || MACCATALYST
    private CAGradientLayer? _mask;

    /// <summary>
    /// Lägger systemets material i den plattformsvy MAUI redan skapat, i stället för att byta ut
    /// hela handlern. Autoresizing i stället för egna constraints: vyn får sin storlek av MAUI:s
    /// layout, och materialet ska bara följa med den.
    /// </summary>
    private void AttachBlur()
    {
        if (Handler?.PlatformView is not UIView view)
            return;

        if (view.Subviews.OfType<UIVisualEffectView>().Any())
            return;

        // Ultratunt och inte tunt: det tunna materialet lägger till en ljus ton som gör bandet
        // till en platta, och poängen är att se vad som passerar under den. Det ultratunna suddar
        // lika mycket men släpper igenom mer av färgen bakom.
        var blur = new UIVisualEffectView(
            UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemUltraThinMaterial))
        {
            Frame = view.Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
            UserInteractionEnabled = false,
        };

        // Masken tonar ut oskärpan mot underkanten. Svart är ogenomskinligt i en mask och klart
        // är genomskinligt — det är alfat och inte färgen som räknas.
        _mask = new CAGradientLayer
        {
            Colors = [UIColor.Black.CGColor, UIColor.Black.CGColor, UIColor.Clear.CGColor],
            StartPoint = new CGPoint(0.5, 0),
            EndPoint = new CGPoint(0.5, 1),
        };

        blur.Layer.Mask = _mask;

        view.AddSubview(blur);

        LayoutBlur();
    }

    /// <summary>
    /// Lager följer inte med autoresizing som vyer gör, så maskens ram och stopp sätts om när
    /// bandet byter storlek — vilket det gör när insetsen mätts och när skärmen vänds.
    /// </summary>
    private void LayoutBlur()
    {
        if (_mask is null || Height <= 0)
            return;

        // Inga implicita animationer: masken ska följa layouten i samma bildruta som den, inte
        // glida efter den.
        CATransaction.Begin();
        CATransaction.DisableActions = true;

        _mask.Frame = new CGRect(0, 0, Width, Height);
        _mask.Locations = [0, new NSNumber(Math.Max(0, (Height - FadeHeight) / Height)), 1];

        CATransaction.Commit();
    }
#endif
}
