using Orientera.Domain;

namespace Orientera.Services.FakeData;

/// <summary>
/// The Gästrikland demo calendar, August 2026. Deterministic: the same competitions, start
/// fields, splits and points on every launch, on every platform.
/// </summary>
/// <remarks>
/// Modelled on the spec's own examples — the Norrlandsmästerskapen weekend, the "Veckans
/// bana" series at Hemlingby and DM Sprint — because M0 is evaluated on whether the app
/// feels right, and data that does not feel real makes that judgement worthless (risk R5).
/// </remarks>
public sealed class FakeDataset
{
    /// <summary>
    /// Where the app's clock starts: Saturday 15 August 2026, 11:50 — Norrlandsmästerskapen
    /// Lång is under way, so Live has runners on the course, Hem leads with "Live nu", and the
    /// July results are behind us. The time machine moves from here.
    /// </summary>
    public static readonly DateTimeOffset DefaultNow = At(2026, 8, 15, 11, 50);

    // Lazy, not a static field initialiser: the seed reads the id fields declared further down,
    // and static initialisers run in declaration order — eager construction would see them all
    // as default and silently give every person the same id.
    private static readonly Lazy<FakeDataset> Singleton = new(() => new FakeDataset());

    public static FakeDataset Instance => Singleton.Value;

    private FakeDataset()
    {
        People = BuildPeople();
        Me = People.Single(p => p.Id == MeId);
        MyGroup = BuildMyGroup(People);
        Series = BuildSeries();
        Competitions = BuildCompetitions();
        Entries = BuildEntries();
        Runs = BuildRuns();
        Courses = BuildCourses();
        Predictions = BuildPredictions();
        Ranking = BuildRanking();
        SeriesStandings = BuildSeriesStandings();
        ClubActivities = BuildClubActivities();
    }

    public Person Me { get; }
    public IReadOnlyList<Person> People { get; }
    public IReadOnlyList<FollowedPerson> MyGroup { get; }
    public IReadOnlyList<Competition> Competitions { get; }
    public IReadOnlyList<CompetitionEntry> Entries { get; }
    public IReadOnlyDictionary<CompetitionId, IReadOnlyList<PlannedRun>> Runs { get; }
    public IReadOnlyList<Course> Courses { get; }
    public IReadOnlyList<Series> Series { get; }
    public IReadOnlyList<SeriesStanding> SeriesStandings { get; }
    public IReadOnlyList<Prediction> Predictions { get; }
    public RankingSnapshot Ranking { get; }
    public IReadOnlyList<ClubActivity> ClubActivities { get; }

    // ---------------------------------------------------------------- identifiers

    public static readonly PersonId MeId = new("p-elin");
    public static readonly PersonId ViktorId = new("p-viktor");
    public static readonly PersonId MajaId = new("p-maja");
    public static readonly PersonId AntonId = new("p-anton");

    public static readonly CompetitionId SommarsprintenId = new("c-sommarsprinten-2026");
    public static readonly CompetitionId HemlingbyloppetId = new("c-hemlingbyloppet-2026");
    public static readonly CompetitionId NmLongId = new("c-nm-lang-2026");
    public static readonly CompetitionId NmMiddleId = new("c-nm-medel-2026");
    public static readonly CompetitionId SeriesRound5Id = new("c-gastrikeserien-5-2026");
    public static readonly CompetitionId DmSprintId = new("c-dm-sprint-2026");
    public static readonly CompetitionId HosttraffenId = new("c-hosttraffen-2026");
    public static readonly CompetitionId SeriesRound6Id = new("c-gastrikeserien-6-2026");
    public static readonly CompetitionId NightChampionshipId = new("c-natt-km-2026");

    public static readonly SeriesId GastrikeserienId = new("s-gastrikeserien-2026");

    private const string HomeDistrict = "Gästrikland";

    private static readonly GeoPoint Gavle = new(60.6749, 17.1413);
    private static readonly GeoPoint Hemlingby = new(60.6489, 17.1339);
    private static readonly GeoPoint Boulognerskogen = new(60.6710, 17.1290);
    private static readonly GeoPoint SandvikenNaset = new(60.6180, 16.7760);
    private static readonly GeoPoint Amot = new(60.9800, 16.5300);
    private static readonly GeoPoint Hofors = new(60.5530, 16.2870);
    private static readonly GeoPoint Sundborn = new(60.6790, 15.7400);
    private static readonly GeoPoint Edske = new(60.5700, 16.3600);

    private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
        new(new DateTime(year, month, day, hour, minute, 0), TimeSpan.FromHours(2));

    // ---------------------------------------------------------------- people

    private static IReadOnlyList<Person> BuildPeople() =>
    [
        new() { Id = MeId, Name = "Elin Norberg", Club = "OK Gästrike", District = HomeDistrict, DefaultClass = "D21", Home = Gavle },
        new() { Id = ViktorId, Name = "Viktor Norberg", Club = "OK Gästrike", District = HomeDistrict, DefaultClass = "H14", Home = Gavle },
        new() { Id = MajaId, Name = "Maja Lund", Club = "OK Gästrike", District = HomeDistrict, DefaultClass = "D21", Home = Gavle },
        new() { Id = AntonId, Name = "Anton Ek", Club = "Rehns BK", District = HomeDistrict, DefaultClass = "H21", Home = Amot },

        new() { Id = new("p-sara"), Name = "Sara Ahlberg", Club = "Sandvikens OK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-klara"), Name = "Klara Bergström", Club = "Gävle OK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-ida"), Name = "Ida Franzén", Club = "Hofors OK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-nora"), Name = "Nora Kvist", Club = "Rehns BK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-tuva"), Name = "Tuva Sandell", Club = "Falu OK", District = "Dalarna", DefaultClass = "D21" },
        new() { Id = new("p-ellen"), Name = "Ellen Roos", Club = "Sandvikens OK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-alva"), Name = "Alva Lindqvist", Club = "Gävle OK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-moa"), Name = "Moa Persson", Club = "OK Gästrike", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-hanna"), Name = "Hanna Wik", Club = "Hofors OK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-signe"), Name = "Signe Almér", Club = "Falu OK", District = "Dalarna", DefaultClass = "D21" },
        new() { Id = new("p-linn"), Name = "Linn Öberg", Club = "Sandvikens OK", District = HomeDistrict, DefaultClass = "D21" },
        new() { Id = new("p-vera"), Name = "Vera Hult", Club = "Gävle OK", District = HomeDistrict, DefaultClass = "D21" },

        new() { Id = new("p-oskar"), Name = "Oskar Dahl", Club = "Sandvikens OK", District = HomeDistrict, DefaultClass = "H21" },
        new() { Id = new("p-emil"), Name = "Emil Strand", Club = "Gävle OK", District = HomeDistrict, DefaultClass = "H21" },
        new() { Id = new("p-jonas"), Name = "Jonas Berg", Club = "Hofors OK", District = HomeDistrict, DefaultClass = "H21" },
        new() { Id = new("p-nils"), Name = "Nils Åkerlund", Club = "OK Gästrike", District = HomeDistrict, DefaultClass = "H21" },
        new() { Id = new("p-hugo"), Name = "Hugo Falk", Club = "Falu OK", District = "Dalarna", DefaultClass = "H21" },
        new() { Id = new("p-arvid"), Name = "Arvid Lindh", Club = "Rehns BK", District = HomeDistrict, DefaultClass = "H21" },
        new() { Id = new("p-melker"), Name = "Melker Sund", Club = "Sandvikens OK", District = HomeDistrict, DefaultClass = "H21" },
        new() { Id = new("p-theo"), Name = "Theo Rask", Club = "Gävle OK", District = HomeDistrict, DefaultClass = "H21" },

        new() { Id = new("p-love"), Name = "Love Åström", Club = "Gävle OK", District = HomeDistrict, DefaultClass = "H14" },
        new() { Id = new("p-vidar"), Name = "Vidar Holm", Club = "Sandvikens OK", District = HomeDistrict, DefaultClass = "H14" },
        new() { Id = new("p-sixten"), Name = "Sixten Ryd", Club = "Hofors OK", District = HomeDistrict, DefaultClass = "H14" },
        new() { Id = new("p-elias"), Name = "Elias Norin", Club = "Rehns BK", District = HomeDistrict, DefaultClass = "H14" },
        new() { Id = new("p-folke"), Name = "Folke Vik", Club = "OK Gästrike", District = HomeDistrict, DefaultClass = "H14" },
        new() { Id = new("p-milo"), Name = "Milo Sjögren", Club = "Falu OK", District = "Dalarna", DefaultClass = "H14" },
        new() { Id = new("p-alfred"), Name = "Alfred Ek", Club = "Gävle OK", District = HomeDistrict, DefaultClass = "H14" },
    ];

    private static IReadOnlyList<FollowedPerson> BuildMyGroup(IReadOnlyList<Person> people) =>
    [
        new() { Person = people.Single(p => p.Id == ViktorId), Reason = FollowReason.Family, NotificationsEnabled = true },
        new() { Person = people.Single(p => p.Id == MajaId), Reason = FollowReason.Clubmate },
        new() { Person = people.Single(p => p.Id == AntonId), Reason = FollowReason.Favourite },
    ];

    // ---------------------------------------------------------------- series

    private static IReadOnlyList<Series> BuildSeries() =>
    [
        new() { Id = GastrikeserienId, Name = "Gästriklandsserien 2026", CountingRounds = 4 },
    ];

    // ---------------------------------------------------------------- competitions

    private static readonly string[] AdultClasses =
        ["D21", "D35", "D45", "D16", "D14", "H21", "H35", "H45", "H16", "H14", "Öppen 5", "Öppen 3"];

    private static IReadOnlyList<Competition> BuildCompetitions()
    {
        var list = new List<Competition>
        {
            // --- past: the results and analysis material ---
            new()
            {
                Id = SommarsprintenId,
                Name = "Sommarsprinten",
                Organiser = "Gävle OK",
                District = HomeDistrict,
                Place = "Boulognerskogen, Gävle",
                Location = Boulognerskogen,
                Discipline = Discipline.Sprint,
                Level = CompetitionLevel.National,
                FirstStart = At(2026, 7, 26, 10, 0),
                LastFinish = At(2026, 7, 26, 13, 0),
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 6, 1, 0, 0),
                    EntryDeadline = At(2026, 7, 20, 23, 59),
                    PmPublishedAt = At(2026, 7, 22, 17, 0),
                    StartListPublishedAt = At(2026, 7, 24, 20, 0),
                    ResultsPublishedAt = At(2026, 7, 26, 14, 0),
                    SplitsPublishedAt = At(2026, 7, 26, 15, 30),
                    MapPublishedAt = At(2026, 7, 27, 9, 0),
                },
            },
            new()
            {
                Id = HemlingbyloppetId,
                Name = "Hemlingbyloppet",
                Organiser = "OK Gästrike",
                District = HomeDistrict,
                Place = "Hemlingby, Gävle",
                Location = Hemlingby,
                Discipline = Discipline.Middle,
                Level = CompetitionLevel.District,
                FirstStart = At(2026, 8, 2, 10, 0),
                LastFinish = At(2026, 8, 2, 13, 30),
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 6, 15, 0, 0),
                    EntryDeadline = At(2026, 7, 28, 23, 59),
                    PmPublishedAt = At(2026, 7, 30, 18, 0),
                    StartListPublishedAt = At(2026, 7, 31, 20, 0),
                    ResultsPublishedAt = At(2026, 8, 2, 14, 30),
                    SplitsPublishedAt = At(2026, 8, 2, 16, 0),
                    MapPublishedAt = At(2026, 8, 3, 9, 0),
                },
            },

            // --- the lifecycle showcase ---
            new()
            {
                Id = NmLongId,
                Name = "Norrlandsmästerskapen Lång",
                Organiser = "Sandvikens OK",
                District = HomeDistrict,
                Place = "Näset, Sandviken",
                Location = SandvikenNaset,
                Discipline = Discipline.Long,
                Level = CompetitionLevel.Championship,
                FirstStart = At(2026, 8, 15, 10, 0),
                LastFinish = At(2026, 8, 15, 13, 30),
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 6, 15, 0, 0),
                    EntryDeadline = At(2026, 8, 9, 23, 59),
                    PmPublishedAt = At(2026, 8, 8, 18, 0),
                    StartListPublishedAt = At(2026, 8, 13, 20, 0),
                    ResultsPublishedAt = At(2026, 8, 15, 14, 0),
                    SplitsPublishedAt = At(2026, 8, 15, 16, 0),
                    MapPublishedAt = At(2026, 8, 16, 10, 0),
                },
                Documents =
                [
                    new() { Kind = DocumentKind.Pm, Title = "PM", Url = "https://example.invalid/nm-lang-pm.pdf", PublishedAt = At(2026, 8, 8, 18, 0) },
                    new() { Kind = DocumentKind.Invitation, Title = "Inbjudan", Url = "https://example.invalid/nm-inbjudan.pdf", PublishedAt = At(2026, 5, 20, 12, 0) },
                    new() { Kind = DocumentKind.TerrainSample, Title = "Terrängbeskrivning", Url = "https://example.invalid/nm-terrang.pdf", PublishedAt = At(2026, 6, 1, 12, 0) },
                    new() { Kind = DocumentKind.Accommodation, Title = "Boende och camping", Url = "https://example.invalid/nm-boende.pdf", PublishedAt = At(2026, 6, 1, 12, 0) },
                ],
                Profile = NmProfile(),
            },
            new()
            {
                Id = NmMiddleId,
                Name = "Norrlandsmästerskapen Medel",
                Organiser = "Sandvikens OK",
                District = HomeDistrict,
                Place = "Näset, Sandviken",
                Location = SandvikenNaset,
                Discipline = Discipline.Middle,
                Level = CompetitionLevel.Championship,
                FirstStart = At(2026, 8, 16, 10, 0),
                LastFinish = At(2026, 8, 16, 13, 0),
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 6, 15, 0, 0),
                    EntryDeadline = At(2026, 8, 9, 23, 59),
                    PmPublishedAt = At(2026, 8, 8, 18, 0),
                    StartListPublishedAt = At(2026, 8, 15, 18, 0),
                    ResultsPublishedAt = At(2026, 8, 16, 13, 30),
                    SplitsPublishedAt = At(2026, 8, 16, 15, 0),
                },
                Documents =
                [
                    new() { Kind = DocumentKind.Pm, Title = "PM", Url = "https://example.invalid/nm-medel-pm.pdf", PublishedAt = At(2026, 8, 8, 18, 0) },
                ],
            },

            // --- upcoming ---
            new()
            {
                Id = SeriesRound5Id,
                Name = "Gästriklandsserien deltävling 5",
                Organiser = "Rehns BK",
                District = HomeDistrict,
                Place = "Åmot",
                Location = Amot,
                Discipline = Discipline.Middle,
                Level = CompetitionLevel.District,
                FirstStart = At(2026, 8, 22, 11, 0),
                LastFinish = At(2026, 8, 22, 14, 0),
                Series = GastrikeserienId,
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 7, 1, 0, 0),
                    EntryDeadline = At(2026, 8, 17, 23, 59),
                },
            },
            new()
            {
                Id = DmSprintId,
                Name = "DM Sprint Gästrikland",
                Organiser = "Hofors OK",
                District = HomeDistrict,
                Place = "Hofors centrum",
                Location = Hofors,
                Discipline = Discipline.Sprint,
                Level = CompetitionLevel.Championship,
                FirstStart = At(2026, 8, 29, 10, 0),
                LastFinish = At(2026, 8, 29, 12, 30),
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 7, 1, 0, 0),
                    EntryDeadline = At(2026, 8, 24, 23, 59),
                    PmPublishedAt = At(2026, 8, 26, 18, 0),
                },
            },
            new()
            {
                Id = HosttraffenId,
                Name = "Höstträffen",
                Organiser = "Falu OK",
                District = "Dalarna",
                Place = "Sundborn, Falun",
                Location = Sundborn,
                Discipline = Discipline.Long,
                Level = CompetitionLevel.National,
                FirstStart = At(2026, 9, 5, 10, 0),
                LastFinish = At(2026, 9, 5, 14, 0),
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 7, 10, 0, 0),
                    EntryDeadline = At(2026, 8, 31, 23, 59),
                },
            },
            new()
            {
                Id = SeriesRound6Id,
                Name = "Gästriklandsserien deltävling 6",
                Organiser = "Hofors OK",
                District = HomeDistrict,
                Place = "Edske",
                Location = Edske,
                Discipline = Discipline.Middle,
                Level = CompetitionLevel.District,
                FirstStart = At(2026, 9, 12, 11, 0),
                LastFinish = At(2026, 9, 12, 14, 0),
                Series = GastrikeserienId,
                Classes = AdultClasses,
                Schedule = new CompetitionSchedule
                {
                    RegistrationOpensAt = At(2026, 7, 1, 0, 0),
                    EntryDeadline = At(2026, 9, 7, 23, 59),
                },
            },
            new()
            {
                Id = NightChampionshipId,
                Name = "Natt-KM",
                Organiser = "OK Gästrike",
                District = HomeDistrict,
                Place = "Hemlingby, Gävle",
                Location = Hemlingby,
                Discipline = Discipline.Night,
                Level = CompetitionLevel.Local,
                FirstStart = At(2026, 9, 18, 20, 0),
                LastFinish = At(2026, 9, 18, 22, 0),
                Classes = ["Lång", "Kort"],
                Schedule = new CompetitionSchedule { RegistrationOpensAt = At(2026, 8, 20, 0, 0) },
            },
        };

        // Veckans bana: a weekly training loop, open Tuesday–Sunday. Two consecutive weeks,
        // which is exactly the case the grouper has to get right — one card per week, not one
        // card for twelve rows and not twelve separate rows.
        list.AddRange(VeckansBana(week: 1, firstDay: new DateOnly(2026, 8, 4)));
        list.AddRange(VeckansBana(week: 2, firstDay: new DateOnly(2026, 8, 11)));

        return list;
    }

    private static IEnumerable<Competition> VeckansBana(int week, DateOnly firstDay)
    {
        for (int day = 0; day < 6; day++)
        {
            var date = firstDay.AddDays(day);
            int occurrence = ((week - 1) * 6) + day + 1;

            yield return new Competition
            {
                Id = new CompetitionId($"c-veckans-bana-{date:yyyyMMdd}"),
                Name = $"Veckans bana etapp {occurrence}",
                Organiser = "Gävle OK",
                District = HomeDistrict,
                Place = "Hemlingby, Gävle",
                Location = Hemlingby,
                Discipline = Discipline.Middle,
                Level = CompetitionLevel.Recreational,
                FirstStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 0)), TimeSpan.FromHours(2)),
                LastFinish = new DateTimeOffset(date.ToDateTime(new TimeOnly(20, 0)), TimeSpan.FromHours(2)),
                Classes = ["Lång", "Mellan", "Kort"],
            };
        }
    }

    private static CompetitionProfile NmProfile() => new()
    {
        Facts =
        [
            new() { Group = ProfileGroup.Terrain, Label = "Kupering", Value = "Måttligt kuperat", Confidence = 0.86, SourceDocument = "PM", Page = 2 },
            new() { Group = ProfileGroup.Terrain, Label = "Framkomlighet", Value = "God, med inslag av blockig terräng", Confidence = 0.74, SourceDocument = "PM", Page = 2 },
            new() { Group = ProfileGroup.Terrain, Label = "Teknisk svårighet", Value = "Hög — detaljrik moränterräng", Confidence = 0.68, SourceDocument = "Inbjudan", Page = 1 },
            new() { Group = ProfileGroup.Terrain, Label = "Sikt", Value = "Halvöppen skog, god sikt i höjdpartierna", Confidence = 0.61, SourceDocument = "PM", Page = 2 },

            new() { Group = ProfileGroup.Logistics, Label = "Parkering", Value = "Näsets IP, 900 m till arena", Confidence = 0.92, SourceDocument = "PM", Page = 1 },
            new() { Group = ProfileGroup.Logistics, Label = "Avstånd arena–start", Value = "1 200 m, blå-vit snitsel", Confidence = 0.9, SourceDocument = "PM", Page = 1 },
            new() { Group = ProfileGroup.Logistics, Label = "Avgift", Value = "Betalas via klubbfaktura", Confidence = 0.8, SourceDocument = "Inbjudan", Page = 1 },

            new() { Group = ProfileGroup.Competition, Label = "Första start", Value = "10:00", Confidence = 0.97, SourceDocument = "PM", Page = 1 },
            new() { Group = ProfileGroup.Competition, Label = "Karta", Value = "1:10 000, ekvidistans 5 m, reviderad 2026", Confidence = 0.95, SourceDocument = "PM", Page = 1 },
            new() { Group = ProfileGroup.Competition, Label = "Stämpling", Value = "Sportident Air+, touch free", Confidence = 0.93, SourceDocument = "PM", Page = 1 },
            new() { Group = ProfileGroup.Competition, Label = "Vätska", Value = "Vätskekontroll på banor över 6 km", Confidence = 0.82, SourceDocument = "PM", Page = 2 },

            new()
            {
                Group = ProfileGroup.ClassSpecific,
                Label = "Ungdomsbanor",
                Value = "Ungdomsbanorna går i stigrikt område nordost om arenan",
                Confidence = 0.71,
                SourceDocument = "PM",
                Page = 3,
                Classes = ["H14", "D14", "H16", "D16"],
            },

            new() { Group = ProfileGroup.Risk, Label = "Förbjuden passage", Value = "Järnvägen får inte passeras — se karta", Confidence = 0.88, SourceDocument = "PM", Page = 3 },
            new() { Group = ProfileGroup.Risk, Label = "Trafik", Value = "Vägpassage vid kontroll 9, funktionär på plats", Confidence = 0.79, SourceDocument = "PM", Page = 3 },
        ],
    };

    // ---------------------------------------------------------------- entries

    private static IReadOnlyList<CompetitionEntry> BuildEntries() =>
    [
        new() { Competition = SommarsprintenId, Person = MeId, Class = "D21", RegisteredAt = At(2026, 7, 12, 19, 30) },
        new() { Competition = HemlingbyloppetId, Person = MeId, Class = "D21", RegisteredAt = At(2026, 7, 20, 21, 15) },

        // The entry that lets the time machine rewind past "Anmäld" for NM Lång.
        new() { Competition = NmLongId, Person = MeId, Class = "D21", RegisteredAt = At(2026, 8, 5, 20, 12) },
        new() { Competition = NmMiddleId, Person = MeId, Class = "D21", RegisteredAt = At(2026, 8, 5, 20, 12) },

        new() { Competition = NmLongId, Person = ViktorId, Class = "H14", RegisteredAt = At(2026, 8, 5, 20, 14) },
        new() { Competition = NmMiddleId, Person = ViktorId, Class = "H14", RegisteredAt = At(2026, 8, 5, 20, 14) },
        new() { Competition = NmLongId, Person = MajaId, Class = "D21", RegisteredAt = At(2026, 8, 3, 12, 0) },
        new() { Competition = NmLongId, Person = AntonId, Class = "H21", RegisteredAt = At(2026, 8, 7, 8, 45) },

        // Min grupp is entered here but I am not — the "någon i min grupp springer" signal.
        new() { Competition = SeriesRound5Id, Person = ViktorId, Class = "H14", RegisteredAt = At(2026, 8, 9, 18, 0) },
        new() { Competition = SeriesRound5Id, Person = AntonId, Class = "H21", RegisteredAt = At(2026, 8, 11, 9, 30) },
    ];

    // ---------------------------------------------------------------- runs

    private static readonly Dictionary<string, RunShape> ScriptedRuns = new()
    {
        // NM Lång D21 — Elin runs well but drops over two minutes on the long legs 4 and 8.
        [$"{nameof(NmLongId)}|p-elin"] = new RunShape(1.055, new Dictionary<int, int> { [4] = 96, [8] = 141 }),
        [$"{nameof(NmLongId)}|p-maja"] = new RunShape(1.09, new Dictionary<int, int> { [11] = 68 }),
        [$"{nameof(NmLongId)}|p-sara"] = new RunShape(1.00),
        [$"{nameof(NmLongId)}|p-klara"] = new RunShape(1.02),
        [$"{nameof(NmLongId)}|p-ida"] = new RunShape(1.035, new Dictionary<int, int> { [2] = 55 }),
        [$"{nameof(NmLongId)}|p-nora"] = new RunShape(1.12, Status: ResultStatus.Mispunch),
        [$"{nameof(NmLongId)}|p-tuva"] = new RunShape(1.045),
        [$"{nameof(NmLongId)}|p-ellen"] = new RunShape(1.07),

        // H14 — Viktor near the front.
        [$"{nameof(NmLongId)}|p-viktor"] = new RunShape(1.03, new Dictionary<int, int> { [5] = 47 }),
        [$"{nameof(NmLongId)}|p-love"] = new RunShape(1.00),

        // H21 — Anton at the sharp end.
        [$"{nameof(NmLongId)}|p-anton"] = new RunShape(1.00),

        // Hemlingbyloppet — Elin near the podium, one clear mistake.
        [$"{nameof(HemlingbyloppetId)}|p-elin"] = new RunShape(1.03, new Dictionary<int, int> { [6] = 112 }),
        [$"{nameof(HemlingbyloppetId)}|p-sara"] = new RunShape(1.00),
        [$"{nameof(HemlingbyloppetId)}|p-klara"] = new RunShape(1.015),

        // Sommarsprinten — sprint mistakes are small by nature.
        [$"{nameof(SommarsprintenId)}|p-elin"] = new RunShape(1.04, new Dictionary<int, int> { [7] = 41 }),
        [$"{nameof(SommarsprintenId)}|p-klara"] = new RunShape(1.00),
        [$"{nameof(SommarsprintenId)}|p-sara"] = new RunShape(1.01),
        [$"{nameof(SommarsprintenId)}|p-ida"] = new RunShape(1.02),
        [$"{nameof(SommarsprintenId)}|p-maja"] = new RunShape(1.03),
    };

    private IReadOnlyDictionary<CompetitionId, IReadOnlyList<PlannedRun>> BuildRuns()
    {
        var runs = new Dictionary<CompetitionId, IReadOnlyList<PlannedRun>>
        {
            [NmLongId] = BuildField(
                NmLongId, nameof(NmLongId), At(2026, 8, 15, 10, 20),
                (Class: "D21", Course: RunGenerator.LongCourse),
                (Class: "H21", Course: RunGenerator.LongCourse),
                (Class: "H14", Course: RunGenerator.YouthCourse)),

            [HemlingbyloppetId] = BuildField(
                HemlingbyloppetId, nameof(HemlingbyloppetId), At(2026, 8, 2, 10, 0),
                (Class: "D21", Course: RunGenerator.MiddleCourse)),

            [SommarsprintenId] = BuildField(
                SommarsprintenId, nameof(SommarsprintenId), At(2026, 7, 26, 10, 0),
                (Class: "D21", Course: RunGenerator.SprintCourse)),
        };

        return runs;
    }

    /// <summary>
    /// Start slots for the people the demo narrates. Elin's 11:04 is what puts her out on the
    /// course at the default "now", so the Live tab always has a "jag"-row still running.
    /// </summary>
    private static readonly Dictionary<string, int> ScriptedStartSlots = new()
    {
        [$"{nameof(NmLongId)}|p-elin"] = 11,
        [$"{nameof(NmLongId)}|p-maja"] = 6,
        [$"{nameof(NmLongId)}|p-viktor"] = 4,
        [$"{nameof(NmLongId)}|p-anton"] = 13,
    };

    private IReadOnlyList<PlannedRun> BuildField(
        CompetitionId competition,
        string competitionKey,
        DateTimeOffset firstStart,
        params (string Class, IReadOnlyList<int> Course)[] classes)
    {
        var runs = new List<PlannedRun>();
        var startInterval = TimeSpan.FromMinutes(4);

        foreach (var (className, course) in classes)
        {
            var starters = People.Where(p => p.DefaultClass == className).ToList();
            var takenSlots = new HashSet<int>();

            foreach (var person in starters)
            {
                if (ScriptedStartSlots.TryGetValue($"{competitionKey}|{person.Id.Value}", out int slot))
                    takenSlots.Add(slot);
            }

            int nextFreeSlot = 0;

            foreach (var person in starters)
            {
                string key = $"{competitionKey}|{person.Id.Value}";

                if (!ScriptedStartSlots.TryGetValue(key, out int startSlot))
                {
                    while (takenSlots.Contains(nextFreeSlot))
                        nextFreeSlot++;

                    startSlot = nextFreeSlot++;
                }

                var shape = ScriptedRuns.TryGetValue(key, out var scripted)
                    ? scripted
                    : new RunShape(RunGenerator.PaceFor(person.Id, competition));

                runs.Add(new PlannedRun
                {
                    Person = person,
                    Competition = competition,
                    Class = className,
                    StartTime = firstStart + (startInterval * startSlot),
                    Splits = RunGenerator.Build(course, shape, $"{competitionKey}|{className}", key),
                    Status = shape.Status,
                });
            }
        }

        return runs;
    }

    // ---------------------------------------------------------------- courses

    private static IReadOnlyList<Course> BuildCourses() =>
    [
        new()
        {
            Competition = NmLongId,
            Class = "D21",
            LengthKm = 7.8,
            ClimbMeters = 215,
            Controls = Enumerable.Range(1, RunGenerator.LongCourse.Count)
                .Select(n => new Control { Number = n, Code = (30 + n).ToString(), Location = SandvikenNaset })
                .ToList(),
        },
        new()
        {
            Competition = HemlingbyloppetId,
            Class = "D21",
            LengthKm = 4.2,
            ClimbMeters = 95,
            Controls = Enumerable.Range(1, RunGenerator.MiddleCourse.Count)
                .Select(n => new Control { Number = n, Code = (30 + n).ToString(), Location = Hemlingby })
                .ToList(),
        },
    ];

    // ---------------------------------------------------------------- predictions

    private static IReadOnlyList<Prediction> BuildPredictions() =>
    [
        new()
        {
            Competition = NmLongId,
            Person = MeId,
            Class = "D21",
            LowPlace = 4,
            HighPlace = 9,
            FieldSize = 12,
            Confidence = 0.64,
            ModelVersion = "fake-1",
            Drivers =
            [
                "Sverigelistan 1 043 p — 4:e bäst i startfältet",
                "Två långdistanser i år, båda över snittet",
                "Hög teknisk svårighet enligt PM breddar intervallet",
                "Litet startfält (12) ger större slumputslag",
            ],
        },
        new()
        {
            Competition = DmSprintId,
            Person = MeId,
            Class = "D21",
            LowPlace = 3,
            HighPlace = 7,
            FieldSize = 18,
            Confidence = 0.71,
            ModelVersion = "fake-1",
            Drivers =
            [
                "Sprint är din starkaste disciplin (1 088 p)",
                "5:e på Sommarsprinten med små tidsförluster",
                "Startfältet saknar tre av distriktets snabbaste",
            ],
        },
    ];

    // ---------------------------------------------------------------- ranking

    /// <summary>
    /// The club's activity list. Relays first, because that is what a club list is mostly for:
    /// one closes soon and one closed long ago, so both states are visible in the demo.
    /// </summary>
    private static IReadOnlyList<ClubActivity> BuildClubActivities() =>
    [
        new()
        {
            Id = "a-25manna",
            Name = "25-manna 10/10 Huddinge",
            Organisation = "OK Gästrike",
            EntryDeadline = At(2026, 8, 30, 20, 0),
            EntryCount = 6,
            IsOpen = true,
            Url = "https://eventor.orientering.se/Activities/Show/26686",
        },
        new()
        {
            Id = "a-dm-stafett",
            Name = "DM-stafett 30/8 Ockelbo",
            Organisation = "OK Gästrike",
            EntryDeadline = At(2026, 8, 23, 20, 0),
            EntryCount = 14,
            IsOpen = true,
            Url = "https://eventor.orientering.se/Activities/Show/26684",
        },
        new()
        {
            Id = "a-stafettraning",
            Name = "Stafettträning vid Kronkojan",
            Organisation = "OK Gästrike",
            StartsAt = At(2026, 8, 19, 18, 0),
            EntryDeadline = At(2026, 8, 18, 12, 0),
            EntryCount = 32,
            IsOpen = true,
            Url = "https://eventor.orientering.se/Activities/Show/26404",
        },
        new()
        {
            Id = "a-usm-traning",
            Name = "Träningsdag inför USM",
            Organisation = "Gästriklands OF",
            StartsAt = At(2026, 8, 22, 8, 0),
            EntryDeadline = At(2026, 8, 16, 22, 0),
            EntryCount = 3,
            IsOpen = true,
            Url = "https://eventor.orientering.se/Activities/Show/26713",
        },
    ];

    private static RankingSnapshot BuildRanking() => new()
    {
        Person = MeId,
        Date = new DateOnly(2026, 8, 15),
        Points = 1043,
        NationalPlace = 187,
        Trend = 12,
        Class = new ClassStanding { Class = "D21", Place = 24 },
        Club = new ClubStanding { Club = "OK Gästrike", Place = 2, Section = RankingSection.Women },
        DisciplinePoints = new Dictionary<Discipline, double>
        {
            [Discipline.Sprint] = 1088,
            [Discipline.Middle] = 1051,
            [Discipline.Long] = 1004,
        },
        Results =
        [
            new() { Competition = SommarsprintenId, CompetitionName = "Sommarsprinten", Date = new DateOnly(2026, 7, 26), Points = 1071, IsCounting = true, ExpiresOn = new DateOnly(2027, 7, 26) },
            new() { Competition = HemlingbyloppetId, CompetitionName = "Hemlingbyloppet", Date = new DateOnly(2026, 8, 2), Points = 1094, IsCounting = true, ExpiresOn = new DateOnly(2027, 8, 2) },
            new() { Competition = new("c-varsprinten-2026"), CompetitionName = "Vårsprinten", Date = new DateOnly(2026, 5, 9), Points = 1058, IsCounting = true, ExpiresOn = new DateOnly(2027, 5, 9) },
            new() { Competition = new("c-medeldistans-uppsala-2026"), CompetitionName = "Uppsala Medel", Date = new DateOnly(2026, 6, 6), Points = 1030, IsCounting = true, ExpiresOn = new DateOnly(2027, 6, 6) },
            new() { Competition = new("c-vastmanland-lang-2025"), CompetitionName = "Västmanland Lång", Date = new DateOnly(2025, 9, 20), Points = 1021, IsCounting = true, ExpiresOn = new DateOnly(2026, 9, 20) },
            new() { Competition = new("c-hostmedel-2025"), CompetitionName = "Höstmedel Sandviken", Date = new DateOnly(2025, 9, 6), Points = 986, IsCounting = true, ExpiresOn = new DateOnly(2026, 9, 6) },
            new() { Competition = new("c-natt-sm-2025"), CompetitionName = "Natt-SM", Date = new DateOnly(2025, 10, 4), Points = 964, IsCounting = false, ExpiresOn = new DateOnly(2026, 10, 4) },
            new() { Competition = new("c-vintercupen-2026"), CompetitionName = "Vintercupen final", Date = new DateOnly(2026, 3, 14), Points = 951, IsCounting = false, ExpiresOn = new DateOnly(2027, 3, 14) },
        ],
    };

    private static IReadOnlyList<SeriesStanding> BuildSeriesStandings() =>
    [
        new()
        {
            Series = GastrikeserienId,
            Person = MeId,
            Class = "D21",
            Place = 4,
            TotalPoints = 268,
            Rounds =
            [
                new() { Competition = new("c-gastrikeserien-1-2026"), CompetitionName = "Deltävling 1, Valbo", Date = new DateOnly(2026, 4, 25), Place = 5, Points = 62, IsCounting = true },
                new() { Competition = new("c-gastrikeserien-2-2026"), CompetitionName = "Deltävling 2, Storvik", Date = new DateOnly(2026, 5, 16), Place = 3, Points = 74, IsCounting = true },
                new() { Competition = new("c-gastrikeserien-3-2026"), CompetitionName = "Deltävling 3, Ockelbo", Date = new DateOnly(2026, 6, 13), Place = 8, Points = 48, IsCounting = false },
                new() { Competition = HemlingbyloppetId, CompetitionName = "Deltävling 4, Hemlingby", Date = new DateOnly(2026, 8, 2), Place = 3, Points = 76, IsCounting = true },
                new() { Competition = SeriesRound5Id, CompetitionName = "Deltävling 5, Åmot", Date = new DateOnly(2026, 8, 22), Place = null, Points = 0, IsCounting = false },
                new() { Competition = SeriesRound6Id, CompetitionName = "Deltävling 6, Edske", Date = new DateOnly(2026, 9, 12), Place = null, Points = 0, IsCounting = false },
            ],
        },
    ];
}
