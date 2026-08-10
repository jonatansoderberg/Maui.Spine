using System.Globalization;
using System.Xml.Linq;
using Orientera.Domain;

namespace Orientera.Backend.Eventor;

/// <summary>
/// Eventor's XML into Orienteras domain model. This is the only place in the product that
/// knows what an <c>EventClassificationId</c> is — above it, everything is a
/// <see cref="Competition"/>.
/// </summary>
public sealed class EventorNormalizer(TimeZoneInfo _zone)
{
    public static EventorNormalizer ForZone(string timeZoneId) =>
        new(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    // ---------------------------------------------------------------- competitions

    public IReadOnlyList<Competition> Competitions(XElement eventList, OrganisationDirectory organisations) =>
        [.. eventList.Children("Event")
            .Where(e => organisations.IsSwedish(OrganiserOf(e)))
            .Select(e => Competition(e, organisations))
            .OfType<Competition>()
            .OrderBy(c => c.FirstStart)];

    private static string? OrganiserOf(XElement element) =>
        element.Child("Organiser").Deep("OrganisationId").FirstOrDefault()?.Value.Trim();

    public Competition? Competition(XElement element, OrganisationDirectory organisations)
    {
        if (element.Text("EventId") is not { } id || element.Text("Name") is not { } name)
            return null;

        var race = element.Children("EventRace").FirstOrDefault();
        var organiserId = OrganiserOf(element);
        var district = organisations.DistrictOf(organiserId);

        var firstStart =
            race.Child("RaceDate").Moment(_zone)
            ?? element.Child("StartDate").Moment(_zone)
            ?? default;

        if (firstStart == default)
            return null;

        return new Competition
        {
            Id = new CompetitionId(id),
            Name = name,
            Organiser = organisations.NameOf(organiserId),
            OrganiserLogo = organisations.LogoOf(organiserId),
            District = district,
            Place = PlaceOf(race, name, district),
            Location = PositionOf(race),
            Discipline = DisciplineOf(element, race),
            Level = LevelOf(element.Text("EventClassificationId")),
            FirstStart = firstStart,
            LastFinish = LastFinishOf(element, firstStart),
            Schedule = ScheduleOf(element),
            Documents = [],
            Classes = [],
        };
    }

    /// <summary>
    /// Eventor has no arena field — the arena is described in the PM, which is M3's pipeline.
    /// The race name is the closest thing the calendar carries; the district is the fallback.
    /// </summary>
    private static string PlaceOf(XElement? race, string eventName, string district)
    {
        var raceName = race.Text("Name");

        return raceName is not null && !raceName.Equals(eventName, StringComparison.OrdinalIgnoreCase)
            ? raceName
            : district;
    }

    private static GeoPoint PositionOf(XElement? race)
    {
        var position = race.Child("EventCenterPosition");

        double? longitude = EventorXml.Number(position.Attr("x"));
        double? latitude = EventorXml.Number(position.Attr("y"));

        return longitude is { } lng && latitude is { } lat ? new GeoPoint(lat, lng) : default;
    }

    /// <summary>
    /// Eventor separates distance from light condition; the domain treats a night race as its
    /// own discipline, because that is how a runner picks competitions.
    /// </summary>
    /// <remarks>
    /// A relay is a relay whatever its legs measure: live Eventor data marks
    /// "Norrlandsmästerskapen, distriktsstafett" as <c>eventForm="RelaySingleDay"</c> with
    /// <c>raceDistance="Long"</c>, and reading only the distance called it a long-distance race.
    /// </remarks>
    private static Discipline DisciplineOf(XElement element, XElement? race)
    {
        if (element.Attr("eventForm")?.StartsWith("Relay", StringComparison.Ordinal) == true)
            return Discipline.Relay;

        if (race.Attr("raceLightCondition") is "Night")
            return Discipline.Night;

        return race.Attr("raceDistance") switch
        {
            "Sprint" or "KnockOutSprint" => Discipline.Sprint,
            "Long" or "UltraLong" => Discipline.Long,
            "Relay" or "SprintRelay" or "MixedRelay" => Discipline.Relay,
            _ => Discipline.Middle,
        };
    }

    /// <summary>
    /// 1=mästerskap, 2=nationell, 3=distrikt, 4=närtävling, 5=klubbtävling, 6=internationell.
    /// A club event is the "träning" of the domain's filter — it is what "dölj träningar" hides.
    /// </summary>
    private static CompetitionLevel LevelOf(string? classificationId) => classificationId switch
    {
        "1" => CompetitionLevel.Championship,
        "2" => CompetitionLevel.National,
        "3" => CompetitionLevel.District,
        "4" => CompetitionLevel.Local,
        "5" => CompetitionLevel.Training,
        "6" => CompetitionLevel.International,
        _ => CompetitionLevel.Local,
    };

    /// <summary>An arena closes; a finish date without a clock does not say when.</summary>
    private DateTimeOffset LastFinishOf(XElement element, DateTimeOffset firstStart)
    {
        var finish = element.Child("FinishDate").Moment(_zone);

        return finish is { } at && at > firstStart ? at : firstStart.AddHours(6);
    }

    /// <summary>
    /// Verified against live Eventor data (issue #42): an entry break is the period entry is
    /// <em>open</em>, and the publication times live in the event's hash table rather than in
    /// attributes on the event.
    /// </summary>
    private CompetitionSchedule ScheduleOf(XElement element)
    {
        var breaks = element.Children("EntryBreak").ToList();

        // The first period is ordinary entry; later ones are late entry, which costs extra and
        // is not the deadline a runner plans around.
        var opens = breaks
            .Select(b => b.Child("ValidFromDate").Moment(_zone))
            .OfType<DateTimeOffset>()
            .Order()
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();

        var deadline = breaks
            .Select(b => b.Child("ValidToDate").Moment(_zone))
            .OfType<DateTimeOffset>()
            .Order()
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();

        return new CompetitionSchedule
        {
            RegistrationOpensAt = opens,
            EntryDeadline = deadline,
            StartListPublishedAt = Published(element, "startList"),
            ResultsPublishedAt = Published(element, "officialResult"),
        };
    }

    /// <summary>
    /// Eventor records when a start list or a result list was published as a hash table entry
    /// keyed by the race — <c>startList_55507</c>, <c>officialResult_55507</c>. An exact
    /// timestamp, which is what the context engine needs to say what a competition is at.
    /// </summary>
    private DateTimeOffset? Published(XElement element, string key)
    {
        foreach (var entry in element.Children("HashTableEntry"))
        {
            if (entry.Text("Key") is not { } name || !name.StartsWith(key + "_", StringComparison.Ordinal))
                continue;

            if (Timestamp(entry.Text("Value")) is { } at)
                return at;
        }

        return null;
    }

    // ---------------------------------------------------------------- documents and classes

    public IReadOnlyList<CompetitionDocument> Documents(XElement documentList, CompetitionId competition)
    {
        var documents = new List<CompetitionDocument>();

        foreach (var element in documentList.Deep("Document"))
        {
            if (element.Attr("referenceId") is { } reference && reference != competition.Value)
                continue;

            if (element.Attr("url") is not { } url || element.Attr("name") is not { } name)
                continue;

            if (KindOf(element.Attr("type"), name) is not { } kind)
                continue;

            documents.Add(new CompetitionDocument
            {
                Kind = kind,
                Title = name,
                Url = url,
                PublishedAt = Timestamp(element.Attr("modifyDate")),
            });
        }

        return documents;
    }

    /// <summary>
    /// Eventor knows three document types; the domain knows five kinds. What the type does not
    /// answer, the title usually does — and a document that neither identifies is left out
    /// rather than given a label it might not deserve.
    /// </summary>
    private static DocumentKind? KindOf(string? type, string name)
    {
        if (type == "Program")
            return DocumentKind.Pm;

        if (type == "Invitation")
            return DocumentKind.Invitation;

        var title = name.ToLowerInvariant();

        if (title.Contains("pm"))
            return DocumentKind.Pm;

        if (title.Contains("inbjudan"))
            return DocumentKind.Invitation;

        if (title.Contains("karta"))
            return DocumentKind.OldMap;

        if (title.Contains("terräng"))
            return DocumentKind.TerrainSample;

        if (title.Contains("boende") || title.Contains("logi"))
            return DocumentKind.Accommodation;

        return null;
    }

    public IReadOnlyList<string> Classes(XElement eventClassList) =>
        [.. eventClassList.Deep("EventClass")
            .Select(c => c.Text("ClassShortName") ?? c.Text("Name"))
            .OfType<string>()
            .Distinct()];

    // ---------------------------------------------------------------- starts and results

    public IReadOnlyList<Start> Starts(XElement startList, CompetitionId competition)
    {
        var starts = new List<Start>();

        foreach (var classStart in startList.Deep("ClassStart"))
        {
            var className = ClassNameOf(classStart);

            foreach (var personStart in classStart.Children("PersonStart"))
            {
                var start = personStart.Child("Start");
                var startTime = start.Child("StartTime").Moment(_zone);

                if (startTime is not { } at)
                    continue;

                starts.Add(new Start
                {
                    Competition = competition,
                    Person = PersonOf(personStart, className),
                    Class = className,
                    StartTime = at,
                    BibNumber = EventorXml.Integer(start.Text("BibNumber")),
                });
            }
        }

        return [.. starts.OrderBy(s => s.StartTime)];
    }

    public IReadOnlyList<CompetitionResult> Results(
        XElement resultList,
        CompetitionId competition,
        OrganisationDirectory? organisations = null)
    {
        var results = new List<CompetitionResult>();

        foreach (var classResult in resultList.Deep("ClassResult"))
        {
            var className = ClassNameOf(classResult);
            int starters = EventorXml.Integer(classResult.Attr("numberOfStarts"))
                ?? EventorXml.Integer(classResult.Attr("numberOfEntries"))
                ?? classResult.Children("PersonResult").Count();

            foreach (var personResult in classResult.Children("PersonResult"))
            {
                // A multi-race event carries one Result per race; the single-race case is the
                // same shape with one of them.
                foreach (var result in personResult.Deep("Result"))
                {
                    var person = PersonOf(personResult, className);

                    results.Add(new CompetitionResult
                    {
                        Id = new ResultId(result.Text("ResultId") ?? $"{competition.Value}|{person.Value}"),
                        Competition = competition,
                        Person = person,
                        Name = NameOf(personResult),
                        Club = personResult.Child("Organisation").Text("Name") ?? string.Empty,
                        ClubLogo = organisations?.LogoOf(personResult.Child("Organisation").Text("OrganisationId")),
                        Class = className,
                        Status = StatusOf(result.Child("CompetitorStatus").Attr("value")),
                        Time = EventorXml.Duration(result.Text("Time")),
                        Place = EventorXml.Integer(result.Text("ResultPosition")),
                        BehindWinner = EventorXml.Duration(result.Text("TimeDiff")),
                        Starters = starters,
                        Splits = SplitsOf(result),
                    });
                }
            }
        }

        return [.. results
            .OrderBy(r => r.Class, StringComparer.Ordinal)
            .ThenBy(r => r.Place ?? int.MaxValue)
            .ThenBy(r => r.Time ?? TimeSpan.MaxValue)];
    }

    /// <summary>
    /// Eventor reports the elapsed time at each control; the domain also wants the leg, which
    /// is the difference between one control and the one before it.
    /// </summary>
    private static IReadOnlyList<Split> SplitsOf(XElement result)
    {
        var splits = new List<Split>();
        var previous = TimeSpan.Zero;
        int number = 0;

        foreach (var element in result.Children("SplitTime"))
        {
            if (EventorXml.Duration(element.Text("Time")) is not { } elapsed)
                continue;

            number = EventorXml.Integer(element.Attr("sequence")) ?? number + 1;

            splits.Add(new Split
            {
                ControlNumber = number,
                ControlCode = element.Text("ControlCode") ?? number.ToString(CultureInfo.InvariantCulture),
                LegTime = elapsed - previous,
                ElapsedTime = elapsed,
            });

            previous = elapsed;
        }

        return splits;
    }

    /// <summary>
    /// The domain distinguishes what a runner sees on a result list: a time that counts, one
    /// that is not final yet, and the three ways of not having one.
    /// </summary>
    private static ResultStatus StatusOf(string? status) => status switch
    {
        "OK" => ResultStatus.Ok,
        "Finished" or "Active" => ResultStatus.Preliminary,
        "MisPunch" or "Disqualified" or "OverTime" => ResultStatus.Mispunch,
        "DidNotFinish" => ResultStatus.DidNotFinish,
        _ => ResultStatus.DidNotStart,
    };

    private static string ClassNameOf(XElement classElement)
    {
        var eventClass = classElement.Child("EventClass");
        return eventClass.Text("ClassShortName") ?? eventClass.Text("Name") ?? string.Empty;
    }

    /// <summary>
    /// A walk-up starter has no person id. Their name and class still identify them well
    /// enough to keep them in the class they ran in, which is what a result list needs.
    /// </summary>
    private static PersonId PersonOf(XElement holder, string className)
    {
        var person = holder.Child("Person");

        return person.Text("PersonId") is { } id
            ? new PersonId(id)
            : new PersonId($"anon:{className}:{NameOf(holder)}");
    }

    private static string NameOf(XElement holder)
    {
        var name = holder.Child("Person").Child("PersonName");
        var given = string.Join(' ', name.Children("Given").Select(g => g.Value.Trim()));
        var family = name.Text("Family") ?? string.Empty;

        return string.Join(' ', new[] { given, family }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private DateTimeOffset? Timestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            && value.Contains('+'))
        {
            return parsed;
        }

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }
}
