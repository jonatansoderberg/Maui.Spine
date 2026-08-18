# Issue #134 — Etapp C steg 2: anmälningsflödet

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/134
**Branch:** issue/131-competition-detail (samma gren som steg 1 — se Decisions)
**Status:** In Progress

## Plan

Mellanlandningen byggdes i steg 1 (#131). Kvar är insidan: det som möter användaren när hen
fortsätter till Eventor. Sidan är Eventors och skrivs inte om — formuläret, reglerna, betalningen
och bekräftelsen är deras, och det är hela skälet att anmälan sker där (D5). Det appen kan göra är
att låta formuläret vara det som syns.

## Changes

- **`Services/Eventor/EventorEntryChrome.cs`** — två skript som körs i webbvyn efter varje
  navigering:
  - `HideChrome` döljer sponsorraden, logotypbandet, den gula menyn, panoramabannern, annonsspalten
    och sidfoten, och gör innehållet läsbart på en telefonskärm.
  - `SelectClass` förväljer klassen appen skickade med, om Eventors formulär erbjuder den.
- **`EventorEntrySheet`** tar nu `EventorEntry` (tävling + klass) i stället för bara ett
  tävlings-id, och kör skripten på `Navigated`.
- **Sidhuvudets text** kommer ur `EventorReader.AccessAsync()` i stället för ur om en sessionsfil
  finns — fyra lägen i stället för två.

### Vad mätningen gav

Selektorerna är lästa av den riktiga sidan, inte gissade. `GET /Entry?eventId=…` utan inloggning
ger sidan med hela sitt skal: `#topMenuContainer` är sponsorraden, `#header` logotypbandet,
`#middleMenu` den gula menyn, `#adSideBar` de sju husannonserna, `#content > #main` innehållet.

**Att dölja dem räckte inte.** `#grid` är ett CSS-grid deklarerat
`width:1343px; grid-template-columns:1012px 331px` med explicita rader (`css_Core`). Ett dolt barn
lämnar sin rad kvar, så innehållet hamnade under en skärmhög vit yta — och den fasta bredden är
samma sak som gjorde formuläret bredare än telefonen, vilket är hur bricknumret hamnade utanför
högerkanten. Gridet görs därför om till ett vanligt block som tar den bredd det får.

**Annonsskriptet flyttar sina element efter att sidan laddat.** Första försöket dolde
`#leeads-panorama-outer-1` och annonsen kom ändå tillbaka — den sticky-banner som ligger i den
flyttas ut ur sin behållare. Matchas nu på det prefix alla dess id:n och klasser bär.

**Verifierat** på iPhone 17 Pro (iOS 26): sidan öppnar på sitt eget innehåll, utan annonsrad,
logotypband, meny, husannonser eller tomrum, och utan vågrät rullning. Sidhuvudet säger vad Eventor
faktiskt svarar.

**Inte verifierat:** att klassen förväljs. Formuläret ligger bakom inloggning, och den här
simulatorn har ingen giltig Eventor-session. Skriptet matchar på det alternativets text säger
snarare än på fältets namn, eftersom namnet inte går att mäta utifrån.

## Decisions

- **Ingen egen gren.** Steg 2 rör samma flöde som steg 1 och ligger på `issue/131-competition-detail`,
  som ändå väntar på #128 och #130. En gren till i kedjan hade kostat en rebase per merge utan att
  ge något.
- **Klassen matchas på synlig text.** Fältets namn är inte mätbart utifrån — formuläret kräver
  inloggning — och den synliga texten är ändå den användaren jämförde mot när testkörningen såg
  rutan stå på "Insk. 2,0". En miss lämnar formuläret precis som Eventor serverade det, och
  mellanlandningen har redan sagt vilken klass som skickades: ett löfte som inte infriades syns i
  stället för att gå tyst förbi.
- **Sidhuvudet frågar Eventor.** En sparad session som Eventor glömt ser likadan ut som en giltig
  härifrån. Appen har redan fyra lägen för det i `EventorAccess`; det här är samma fråga och ska
  ha samma svar.
- **Bekräftelsen stannar hos Eventor.** Testkörningens förslag att stänga webbvyn när anmälan
  bekräftats och visa kvittot i appens form kräver att appen kan avgöra *att* den bekräftats — en
  mätning av Eventors svarssida som ingen gjort. Att gissa på en URL eller en rubrik vore att
  stänga vyn mitt i en betalning som inte gick igenom. Lämnas till dess det är mätt.
