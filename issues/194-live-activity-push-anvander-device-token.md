# Issue #194 — Spine.Server: Live Activity-push använder device-token, och misslyckandet avregistrerar enheten

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/194
**Branch:** issue/194-live-activity-push-anvander-device-token
**Status:** Completed

## Plan

Två defekter som förstärker varandra, båda i `Plugin.Maui.Spine.Server`. Fixas ihop eftersom den
första garanterar att den andra utlöses.

1. Sändaren adresserar Live Activity-kuvert med device-token. Rätt token finns i
   `PushInstallation.LiveActivities` men läses aldrig.
2. `DeviceTokenNotForTopic` klassas som `Invalid`, vilket får `RemoveInvalidAsync` att radera
   registreringen — trots att token lever.

## Changes

- `PushSender.DispatchAsync` tar en valfri `address`-funktion: vilken token kuvertet ska gå till när
  det inte är device-token. En installation den svarar `null` för skickas inte alls utan rapporteras
  som `Failed` med `NoLiveActivityToken`.
- `LiveActivityAsync` väljer token per plattform och händelse: `PushToStart` för `Start`,
  `Activities[kind]` för `Update` och `End`. Android behåller device-token — där renderas aktiviteten
  som en notis och någon separat token finns inte.
- `ApnsTransport` mappar inte längre `DeviceTokenNotForTopic` till `Invalid`. Bara `BadDeviceToken`
  och `Unregistered` betyder att en token är död.

## Tester

166 gröna. Två fanns som kodifierade det trasiga beteendet och skrevs om:

- `ApnsTransportTests`: `DeviceTokenNotForTopic` förväntas nu ge `Failed`.
- `PushSenderTests.A_live_activity_update_goes_out_on_both_platforms` gav Apple-installationen ingen
  aktivitetstoken och förväntade sig ändå leverans. Den får nu en.

Tre nya:

- uppdatering adresseras till aktivitetens egen token
- start adresseras till push-to-start-token
- en installation utan token skickas inte, utan rapporteras som `Failed` / `NoLiveActivityToken`

## Decisions

- **Ingen fallback från aktivitetstoken till push-to-start.** Klienten behandlar en uppdatering av en
  aktivitet som inte körs som en start, och det vore frestande att göra samma sak här. Men Apples
  push-to-start kräver `event: "start"` i payloaden, så en fallback hade behövt skriva om händelsen
  också — en beteendeändring, inte en buggfix. Utan token rapporteras det i stället, och den som vill
  starta anropar `StartLiveActivityAsync`.
- **`DeviceTokenNotForTopic` blir `Failed`, inte en egen status.** Det är ett avsändarfel bland andra;
  en ny `PushStatus` hade utökat det publika API:t för ett fall som nu inte ska kunna inträffa.
- **Adresseringen ligger i `DispatchAsync`, inte i transporterna.** Transporten ska skicka till den
  token den får. Att låta den välja token hade spridit kunskap om Live Activities till varje
  plattformsimplementation.

## Verifiering

Reproduktionen kom från en fysisk iPhone 16 Pro: `POST /send { "kind": "liveactivity" }` gav
`invalid 1, reason DeviceTokenNotForTopic`, och registret gick från 1 installation till 0.
Enhetsverifiering av den fixade vägen kräver en körande aktivitet på enheten och ligger i #195:s
spår, där samplet får en widget som går att observera.
