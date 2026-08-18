using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;
using Orientera.Presentation;

namespace Orientera.Controls;

/// <summary>
/// The one way out: says where you are going and what goes with you, before anything opens (P11).
/// </summary>
/// <remarks>
/// Every route out of the app — the Eventor entry, Livelox, a PM, map navigation — goes through
/// this shape. Nothing opens quietly.
/// <para>
/// <see cref="StaysInApp"/> is the difference between two promises that look alike. The Eventor
/// entry form opens in the app's own web view, not in Safari, because Safari has its own cookie
/// jar and an external open logs the runner out (measured, see <c>EventorEntrySheet</c>). The card
/// is then the right screen with the wrong mark if it shows the departure arrow, so it says
/// "Eventor's page" and keeps the chevron instead (beslut D5).
/// </para>
/// </remarks>
public sealed class HandoffCard : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(HandoffCard), string.Empty,
            propertyChanged: (b, _, _) => ((HandoffCard)b).Apply());

    public static readonly BindableProperty DestinationProperty =
        BindableProperty.Create(nameof(Destination), typeof(string), typeof(HandoffCard), string.Empty,
            propertyChanged: (b, _, _) => ((HandoffCard)b).Apply());

    public static readonly BindableProperty TransfersProperty =
        BindableProperty.Create(nameof(Transfers), typeof(string), typeof(HandoffCard), string.Empty,
            propertyChanged: (b, _, _) => ((HandoffCard)b).Apply());

    public static readonly BindableProperty StaysInAppProperty =
        BindableProperty.Create(nameof(StaysInApp), typeof(bool), typeof(HandoffCard), false,
            propertyChanged: (b, _, _) => ((HandoffCard)b).Apply());

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(HandoffCard));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(HandoffCard));

    private readonly Border _card = new();
    private readonly Label _title = new();
    private readonly Label _destination = new();
    private readonly Label _transfers = new();
    private readonly Path _mark = new()
    {
        Aspect = Stretch.Uniform,
        WidthRequest = 18,
        HeightRequest = 18,
        StrokeThickness = 1.5,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        VerticalOptions = LayoutOptions.Center,
    };

    public HandoffCard()
    {
        _title.SetDynamicResource(StyleProperty, "Heading2Label");
        _destination.SetDynamicResource(StyleProperty, "BodySecondaryLabel");
        _transfers.SetDynamicResource(StyleProperty, "CaptionLabel");
        _mark.SetDynamicResource(Shape.StrokeProperty, "AccentAction");
        _card.SetDynamicResource(StyleProperty, "Card");

        var text = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { _title, _destination, _transfers },
        };

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 12,
            Children = { text, _mark },
        };

        Grid.SetColumn(_mark, 1);

        foreach (var child in new View[] { _title, _destination, _transfers, _mark })
            AutomationProperties.SetIsInAccessibleTree(child, false);

        _card.Content = grid;
        Content = _card;

        GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (Command?.CanExecute(CommandParameter) == true)
                    Command.Execute(CommandParameter);
            }),
        });

        Apply();
    }

    /// <summary>What the runner is about to do: "Anmäl dig", "Läs PM".</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Where it happens: "Eventors sida", "Livelox".</summary>
    public string Destination
    {
        get => (string)GetValue(DestinationProperty);
        set => SetValue(DestinationProperty, value);
    }

    /// <summary>What goes along — the class, the runner's login, nothing.</summary>
    public string Transfers
    {
        get => (string)GetValue(TransfersProperty);
        set => SetValue(TransfersProperty, value);
    }

    /// <summary>True when the destination opens in the app's own web view rather than a browser.</summary>
    public bool StaysInApp
    {
        get => (bool)GetValue(StaysInAppProperty);
        set => SetValue(StaysInAppProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private void Apply()
    {
        _title.Text = Title;
        _destination.Text = Destination;
        _transfers.Text = Transfers;
        _transfers.IsVisible = !string.IsNullOrWhiteSpace(Transfers);

        _mark.Data = new PathGeometryConverter().ConvertFromInvariantString(
            StaysInApp ? RowGlyph.Chevron : RowGlyph.External) as Geometry;

        SemanticProperties.SetDescription(this, string.Join(", ",
            new[] { Title, Destination, Transfers }.Where(s => !string.IsNullOrWhiteSpace(s))));

        SemanticProperties.SetHint(this, StaysInApp
            ? $"Öppnar {Destination} i appen"
            : $"Öppnar {Destination} utanför appen");
    }
}
