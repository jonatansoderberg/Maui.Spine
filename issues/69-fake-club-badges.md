# Issue #69 — Fake-datat saknar klubbmärken

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/69
**Branch:** issue/69-fake-club-badges
**Status:** Completed

## Plan

Klubbmärken kommer ur Eventors organisationsregister och sätts bara av backend. Fake-källan satte
aldrig `ClubLogo`, så demoraderna saknade märke: annan radhöjd, klubbnamnet på annat ställe, och
`ClubBadge`-ramen aldrig ritad. Allt designarbete mot demodatat missade det.

## Changes

- `Resources/Images/club_badge_1..6.svg` — sex märken, platt geometri.
- `Services/FakeData/FakeClubBadges.cs` — tilldelar ett märke per klubbnamn.
- `FakeDataSource` sätter `ClubLogo` på både resultat- och liverader.
- `FakeClubBadgeTests` — fyra tester.

## Decisions

- **Det här bryter inte mot "ointegrerat svarar tomt".** Den regeln skyddar den *skarpa* vägen,
  där appen inte får låna från fake-datat. Fake-datat är motsatsen: en komplett, designad fixtur,
  och en klubb utan märke är i det sammanhanget bara ofullständig.
- **Egen stabil hash, inte `string.GetHashCode()`.** Den senare är slumpad per process i .NET
  Core och hade gett en klubb olika märke vid varje start — det enda en fixtur inte får göra.
  Testet spikar ett bokstavligt värde, eftersom ett test som bara jämför funktionen med sig själv
  hade godkänt en tyst ändring.
- **Uppenbart påhittade.** Platta former i färger ingen svensk klubb använder som sitt märke, inte
  imitationer av någons riktiga.

## Verifiering

`dotnet test`: 250 gröna (246 + 4 nya).

**iPhone 17 Pro-simulator (iOS 26.2):** Hemlingbyloppets resultatlista visar märke på varje rad,
samma klubb samma märke, och raden har nu samma form som mot skarp Eventor-data.

**Känd begränsning:** sex märken på ett tiotal klubbar ger kollisioner — Falu OK och Gävle OK får
samma. Det syns i en lista där båda förekommer. Fler märken löser det när det stör; för radens
form, som var problemet, spelar det ingen roll.
