using Microsoft.Maui.Controls.Shapes;

namespace Orientera.Controls;

/// <summary>How the identity is framed: a person is round, a club's badge is not.</summary>
public enum IdentityShape
{
    Circle,
    Rounded,
}

/// <summary>
/// The identity on a row: a person's picture or a club's badge, falling back to initials and then
/// to a plain plate (P8). Never an empty circle in a list where other rows carry a picture.
/// </summary>
/// <remarks>
/// The view does not know where its image comes from. It takes an <see cref="ImageSource"/>, not a
/// person id and not a store — today the picture and the following list are local
/// (<c>LocalIdentityStore</c>, <c>LocalGroupStore</c>), and when a server supplies them instead
/// (M5), no view has to change. That is the whole point of building the avatar's place now and
/// leaving its contents alone (beslut D3).
/// <para>
/// Like <see cref="ChipView"/>, the states are pre-built children toggled with
/// <c>IsVisible</c> rather than properties flipped by a trigger, so every colour keeps resolving
/// through <c>{DynamicResource}</c> across a theme swap.
/// </para>
/// </remarks>
public sealed class IdentityView : ContentView
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(ImageSource), typeof(IdentityView), null,
            propertyChanged: (b, _, _) => ((IdentityView)b).Apply());

    public static readonly BindableProperty FallbackProperty =
        BindableProperty.Create(nameof(Fallback), typeof(string), typeof(IdentityView), string.Empty,
            propertyChanged: (b, _, _) => ((IdentityView)b).Apply());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(IdentityView), 40.0,
            propertyChanged: (b, _, _) => ((IdentityView)b).Apply());

    public static readonly BindableProperty ShapeProperty =
        BindableProperty.Create(nameof(Shape), typeof(IdentityShape), typeof(IdentityView),
            IdentityShape.Circle, propertyChanged: (b, _, _) => ((IdentityView)b).Apply());

    private readonly Border _frame = new();
    private readonly RoundRectangle _corners = new();
    private readonly Image _image = new() { Aspect = Aspect.AspectFill };
    private readonly Label _initials = new()
    {
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
    };

    public IdentityView()
    {
        _initials.SetDynamicResource(Label.TextColorProperty, "AccentAction");
        _initials.SetDynamicResource(Label.FontFamilyProperty, "FontSemiBold");

        // The identity is decoration on a row that already reads its own name aloud; announcing it
        // separately turns one row into two swipes.
        AutomationProperties.SetIsInAccessibleTree(_image, false);
        AutomationProperties.SetIsInAccessibleTree(_initials, false);

        _frame.Content = new Grid { Children = { _image, _initials } };
        Content = _frame;

        Apply();
    }

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Initials shown when there is no picture. Empty leaves the plate bare.</summary>
    public string Fallback
    {
        get => (string)GetValue(FallbackProperty);
        set => SetValue(FallbackProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IdentityShape Shape
    {
        get => (IdentityShape)GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    private void Apply()
    {
        _frame.WidthRequest = Size;
        _frame.HeightRequest = Size;

        // Both shapes are stated in full rather than one of them borrowing the ClubBadge style.
        // A locally assigned value outranks a style's setter and is never given back, so a view
        // that started round would keep the round background and the missing hairline after being
        // told it is a club — the appearance would depend on the order the properties were set.
        if (Shape is IdentityShape.Rounded)
        {
            // Club badges arrive as they are — own background, own colours. The frame gives them a
            // shared shape so they read as part of the list instead of as stickers on top of it.
            _frame.SetDynamicResource(BackgroundColorProperty, "SurfaceRaised");
            _frame.SetDynamicResource(Border.StrokeProperty, "OutlineBrush");
            _frame.StrokeThickness = 1;
            _frame.Padding = 2;
            _corners.CornerRadius = 5;
        }
        else
        {
            _frame.SetDynamicResource(BackgroundColorProperty, "AvatarBackground");
            _frame.Stroke = null;
            _frame.StrokeThickness = 0;
            _frame.Padding = 0;
            _corners.CornerRadius = Size / 2;
        }

        _frame.StrokeShape = _corners;

        var hasImage = Source is not null;
        var hasFallback = !string.IsNullOrWhiteSpace(Fallback);

        _image.Source = Source;
        _image.IsVisible = hasImage;

        _initials.Text = Fallback;
        _initials.FontSize = Size * 0.38;
        _initials.IsVisible = !hasImage && hasFallback;

        // With neither a picture nor initials there is nothing to be identified by, and P8 rules
        // out the empty circle: a plate with nothing in it reads as data that failed to arrive.
        // The slot collapses instead, and the row closes over it.
        _frame.IsVisible = hasImage || hasFallback;
    }
}
