using System.Collections;
using Microsoft.Maui.Controls.Shapes;

namespace Orientera.Controls;

/// <summary>One identity in a stack: a picture if there is one, initials if there is not.</summary>
/// <remarks>
/// A record and not a person id, for the reason <see cref="IdentityView"/> takes an
/// <see cref="ImageSource"/>: the stack does not know where a face comes from, so the day a
/// server supplies them instead of <c>LocalIdentityStore</c>, no view changes (beslut D3).
/// </remarks>
public sealed record Face(ImageSource? Source, string Initials);

/// <summary>
/// Who is in this, shown as faces rather than as a number — with the number for the rest.
/// </summary>
/// <remarks>
/// The stack shows the people the reader knows and counts the ones they do not. That order is the
/// point: "Anna, Erik, Johan och 24 till" is a race with your people in it, while "27 anmälda" is
/// a figure. It never stands alone — the count is written out beside it in words as well (P8).
/// <para>
/// The whole stack is one element to a screen reader. Eight faces are eight swipes and no
/// information; the sentence the consumer sets in <see cref="Description"/> is the information.
/// </para>
/// </remarks>
public sealed class AvatarStack : ContentView
{
    public static readonly BindableProperty FacesProperty =
        BindableProperty.Create(nameof(Faces), typeof(IEnumerable), typeof(AvatarStack), null,
            propertyChanged: (b, _, _) => ((AvatarStack)b).Apply());

    public static readonly BindableProperty TotalProperty =
        BindableProperty.Create(nameof(Total), typeof(int), typeof(AvatarStack), 0,
            propertyChanged: (b, _, _) => ((AvatarStack)b).Apply());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(nameof(Size), typeof(double), typeof(AvatarStack), 34.0,
            propertyChanged: (b, _, _) => ((AvatarStack)b).Apply());

    public static readonly BindableProperty RingColorProperty =
        BindableProperty.Create(nameof(RingColor), typeof(Color), typeof(AvatarStack), Colors.Transparent,
            propertyChanged: (b, _, _) => ((AvatarStack)b).Apply());

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(AvatarStack), string.Empty,
            propertyChanged: (b, _, _) => ((AvatarStack)b).Apply());

    private readonly HorizontalStackLayout _row = new();

    public AvatarStack()
    {
        Content = _row;

        Apply();
    }

    public IEnumerable? Faces
    {
        get => (IEnumerable?)GetValue(FacesProperty);
        set => SetValue(FacesProperty, value);
    }

    /// <summary>The whole field, not the part with faces. The difference is what "+N" counts.</summary>
    public int Total
    {
        get => (int)GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// The surface the stack stands on. Overlapping circles need a gap between them or they read
    /// as one shape, and the gap is the card showing through — so the ring cannot be a token of
    /// its own; it is whatever is behind.
    /// </summary>
    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    /// <summary>What the screen reader says instead of the faces.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    private void Apply()
    {
        var faces = Faces?.OfType<Face>().ToList() ?? [];
        var rest = Math.Max(0, Total - faces.Count);

        _row.Children.Clear();

        // Negative spacing rather than a margin on each child: the overlap is a property of the
        // row, and a margin would have to be undone on whichever child happens to be first.
        //
        // A fifth of the circle, not a third. The faces without a picture carry initials centred
        // in the plate, and at a third the neighbour in front cuts the second letter off — the
        // overlap has to leave room for the case where the identity is two letters wide.
        _row.Spacing = -Size * 0.2;

        foreach (var face in faces)
            _row.Children.Add(Ring(new IdentityView
            {
                Source = face.Source,
                Fallback = face.Initials,
                Size = Size,
            }));

        if (rest > 0)
            _row.Children.Add(Ring(Overflow(rest)));

        _row.IsVisible = _row.Children.Count > 0;

        SemanticProperties.SetDescription(this, Description);
        AutomationProperties.SetIsInAccessibleTree(_row, false);
    }

    /// <summary>The gap that keeps the circle in front from swallowing the one behind it.</summary>
    private Border Ring(View content)
    {
        var ring = new Border
        {
            BackgroundColor = RingColor,
            Stroke = null,
            StrokeThickness = 0,
            Padding = 2,
            StrokeShape = new RoundRectangle { CornerRadius = (Size / 2) + 2 },
            Content = content,
        };

        AutomationProperties.SetIsInAccessibleTree(ring, false);

        return ring;
    }

    private View Overflow(int rest)
    {
        // Samma skärning och samma centrering som initialerna bredvid: talet är den sista av
        // ansiktena, inte ett värde i en kolumn, så det behöver inte de tabulära siffrorna.
        var label = new Label
        {
            Text = $"+{rest}",
            FontSize = Size * 0.34,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,

            // Optisk centrering, av samma skäl och med samma mått som initialerna bredvid.
            Margin = new Thickness(0, -Size * 0.09, 0, Size * 0.09),
        };

        label.SetDynamicResource(Label.TextColorProperty, "AccentAction");
        label.SetDynamicResource(Label.FontFamilyProperty, "FontHeader");

        // The same plate the faces without a picture stand on, so the count reads as the last of
        // them rather than as a badge stuck on the end.
        var plate = new Border
        {
            WidthRequest = Size,
            HeightRequest = Size,
            Stroke = null,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = Size / 2 },
            Content = label,
        };

        plate.SetDynamicResource(BackgroundColorProperty, "AvatarBackground");

        return plate;
    }
}
