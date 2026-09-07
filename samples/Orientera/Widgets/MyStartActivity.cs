using Orientera.Domain;
using Orientera.Presentation;
using Plugin.Maui.Spine.Widgets;

namespace Orientera.Widgets;

/// <summary>
/// Live Activityn "Din start": nedräkning på låsskärmen och i Dynamic Island, genom faserna
/// väntar → ute på banan → i mål. Faserna är samma träd med olika text, så Hem kan uppdatera
/// aktiviteten med <see cref="Layout"/> igen när klockan gått vidare.
/// </summary>
public static class MyStartActivity
{
    /// <summary>
    /// The kind an activity for <paramref name="competition"/> is started with; not shown to the
    /// user. The competition is part of it so the running activity can be found again without the
    /// app keeping a handle of its own across launches.
    /// </summary>
    public static string KindFor(CompetitionId competition) => $"din-start:{competition.Value}";

    private static readonly WidgetColor Brand =
        WidgetColor.From((Color)new Resources.Styles.LightTheme()["BrandTint"]);

    public static LiveActivityLayout Layout(Competition competition, DateTimeOffset start, DateTimeOffset now)
    {
        var phase = now < start ? "Din start" : now < competition.LastFinish ? "Ute på banan" : "I mål";

        // Före start är timern nedräkningen; efter start är den tiden ute på banan. Båda ritas av
        // systemet varje sekund, så aktiviteten tickar även när appen inte kör.
        TextLikeNode timer = now < competition.LastFinish
            ? W.Timer(start).Bold().Color(Brand)
            : W.Text(Format.Clock(start)).Bold().Color(Brand);

        return new LiveActivityLayout
        {
            LockScreen = W.VStack(4,
                W.HStack(8,
                    W.Icon("figure.run", Brand),
                    W.VStack(2,
                        W.Text(competition.Name).Headline().Bold(),
                        W.Text($"{phase} {Format.Clock(start)}").Caption().Secondary()),
                    W.Spacer(),
                    timer.Title())),

            ExpandedLeading = W.Icon("figure.run", Brand),
            ExpandedTrailing = timer.Headline(),
            ExpandedCenter = W.Text(competition.Name).Headline().Bold(),
            ExpandedBottom = W.Text($"{phase} {Format.Clock(start)} · {competition.Place}").Caption().Secondary(),

            CompactLeading = W.Icon("figure.run", Brand),
            CompactTrailing = timer.Caption(),
            Minimal = W.Icon("figure.run", Brand),
        };
    }
}
