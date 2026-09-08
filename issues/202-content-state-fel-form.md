# Issue #202 — Spine.Server: Live Activity-push skickar content-state i fel form

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/202
**Branch:** issue/202-content-state-fel-form
**Status:** Completed

## Orsak

ActivityKit avkodar `content-state` till extensionens `ContentState`, som håller layouten som en
sträng — `SpineWidgetShared.swift:7`:

```swift
public struct ContentState: Codable, Hashable { public var json: String }
```

Servern skrev layout-objektet direkt på den nivån i stället för inuti `json`. Avkodningen misslyckas,
aktiviteten kan inte ritas, och iOS visar sin platshållare: en frusen progress-ring på var sida av
Dynamic Island.

Avsikten var rätt hela tiden — `LiveActivityLayout.cs:45` säger *"what a server puts in the
`content-state.json`"*. Det var nivån som blev fel.

## Changes

- `ApnsPayload` skriver `"content-state": { "json": "<layout>" }`.
- `PushPayloadsTests` läste `content-state.lockScreen.text` direkt och kodifierade därmed felet. Det
  läser nu strängen under `json` och parsar den.
- Storleksguardrailen går från halva till tre fjärdedelar av APNs-budgeten, med mätningen i en
  kommentar.

## Decisions

- **Guardrailen flyttades, inte kodningen.** Rätt form kostar utrymme: layouten som sträng betyder att
  varje citattecken escapas. Orienteras realistiska layout går från under halva budgeten till
  **2435 av 4096 byte**, 59 %. Testets "under hälften" skrevs mot den felaktiga kodningen och var
  inte längre en rimlig gräns. Tre fjärdedelar fångar fortfarande en layout som svämmar över, utan
  att argumentera med en kodning ActivityKit dikterar.

## Verifierat

Fysisk iPhone 16 Pro. Före fixen: `sent 1` följt av en frusen progress-ring på var sida av Dynamic
Island, medan `Uppdatera lokalt` i appen renderade normalt. Efter fixen: **ringarna är borta** och
aktiviteten ritas — symbol och tickande färskhetsräknare, som layouten föreskriver.

Att lokal uppdatering fungerade men serveruppdatering inte var hela ledtråden: appens egen väg bygger
`SpineActivityAttributes.ContentState(json:)` i processen och träffar rätt form.

### Innehållsbytet, bekräftat

Låsskärmen gick från `Spine push / Startad lokalt / 5 min` till
`Från servern 00:40:27 / Detta ersätter Startad lokalt`, och färskhetsräknaren nollställdes. Server-
drivna Live Activities fungerar alltså ände till ände.

Det tog två försök, och den första missen var lärorik: utskicket gick 26 sekunder **före** att appen
registrerat om sig, så det adresserades till den föregående aktivitetens token. APNs svarade `sent 1`
och tappade det tyst. Registret håller en gammal aktivitetstoken tills appen råkar registrera om sig
— eget issue, se #200:s uppföljning.
