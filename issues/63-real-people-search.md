# Issue #63 — Följ någon: söker i fake-datat, inte i verkligheten

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/63
**Branch:** issue/63-real-people-search
**Status:** Completed

## Plan

`BackendSource.SearchAsync` skickade vidare till `FakeDataSource`, så **Följ någon** listade
seedade demopersoner även mot skarp backend. Sökte man på en riktig löpare fick man ingenting,
och på en påhittad fick man träff. Min grupp öppnade dessutom med tre personer användaren aldrig
valt.

**Vald väg (efter fråga):** sök i namn appen redan hämtat. Eventor har ingen publik
personsökning — uppslag kräver organisationsbehörighet, samma vägg som identiteten (M5) — men
resultatlistorna för tävlingarna i kalenderfönstret är riktiga namn som redan är hämtade.

## Changes

- `Orientera.Backend/Eventor/PeopleSearch.cs` — söker i resultatlistorna för de tävlingar som
  ligger närmast idag, med tak på antal tävlingar och antal träffar.
- `Orientera.Backend/Functions/PeopleFunctions.cs` — `GET /api/people?q=`.
- `Orientera/Services/Local/LocalGroupStore.cs` — Min grupp på disk, tom från början.
- `BackendSource` — söker mot backend; Min grupp och följ/avfölj mot den lokala store:n.
- `IPeopleSource.FollowAsync` tar nu `Person` i stället för `PersonId`.
- `FollowRunnerSheet` — bär hela personen, och säger vad sökningen söker i.

## Decisions

- **Bara resultatlistor.** Eventors startlistor bär person-id, klass och tid — men varken namn
  eller klubb. Det finns inget i dem att söka i. En löpare går alltså att hitta först när hen
  sprungit klart en tävling i fönstret, vilket är en verklig begränsning och därför skriven i
  klartext under sökrutan: *"Söker bland löpare i resultatlistorna för tävlingar runt idag."*
  Utan den raden läses en tom träfflista som "personen finns inte".
- **Taket är själva poängen.** Sökningen tittar i tolv tävlingar närmast idag, inte i hela
  fönstret. Utan tak hade en sökning blivit en svepning över förbundets säsong, vilket är precis
  vad repots principer förbjuder.
- **`FollowAsync` tar personen, inte ett id.** Mot skarp backend kommer personen ur en
  resultatlista, och det finns ingen katalog att slå upp id:t i efteråt — listan faller ur
  kalenderfönstret. Alternativet, att låta metoden tyst inte göra något, hade varit ett fel som
  bara syns för användaren.
- **Min grupp börjar tom.** De tre seedade hör till demot. Ett testfall som spikade motsatsen
  skrevs om: `Local_data_answers_even_when_the_backend_does_not` påstod att Min grupp är ifylld
  mot skarp backend, vilket är precis det ärendet kallar fel.
- **Hela personen sparas, inte en referens.** Se ovan: det finns inget att slå upp senare.

## Verifiering

`dotnet test`: 244 gröna (242 + 2 nya, ett omskrivet).

**Mot skarp Eventor via BFF-stubben:**

| Sökning | Utfall |
|---|---|
| `Sjödin` | 3 träffar — Cristoph, Isabel och Joshua Sjödin, Sundsvalls OK |
| `Alfred` | 1 träff — Alfred **Hansson**, inte den seedade Alfred Ek |
| `Alva` | 5 träffar, inklusive Alva Maripuu — inte Alva Lindqvist |
| `Ellen Roos` (seedad) | 0 träffar |

**iPhone 17 Pro-simulator (iOS 26.2), mot samma stubb:** Min grupp öppnar tom. Sökrutan visar
riktiga löpare med klubb och klass, raden under säger var den letar, och Adam Nyman (IFK Mora OK,
H21) läggs till i Min grupp och ligger kvar.

**Inte verifierat:** hur sökningen beter sig på en kall backend. Första sökningen hämtar upp till
tolv resultatlistor och kan ta tid; här var de redan cachade av tidigare arbete i samma process.
