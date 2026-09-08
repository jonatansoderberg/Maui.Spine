# Issue #200 — MauiSpinePushSampleApp: Live Activityn ser fryst ut

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/200
**Branch:** issue/200-live-activity-ser-fryst-ut
**Status:** Completed

## Plan

Båda layouterna byggde compact-vyn med `W.Text(DateTimeOffset.Now.ToString("HH:mm"))` — en sträng
stämplad när layouten byggdes. En Live Activity ritas av systemet medan appen inte kör, så allt som
ska förändras över tid måste vara en nod plattformen kan rita om. Byt till `W.Relative`.

## Changes

- `LiveActivityPage.ViewModel.cs` och `MauiSpinePushSampleApp.Server/Program.cs`: `CompactTrailing`
  blir `W.Relative(...)`, och låsskärmsvyn får en färskhetsrad. Båda filerna, så lokal start och
  serveruppdatering beter sig lika.
- `/installations` visar nu `liveActivities` med `pushToStart` och `activities`, förkortade.

## Decisions

- **`W.Relative`, inte `W.Timer`.** En serveruppdaterad aktivitet har ett intressant tillstånd: hur
  färsk den är. `Relative` nollställs synligt när en push landar och tickar sedan själv; `Timer`
  hade krävt ett slut att räkna ner till, som samplet inte har.
- **Registret visar tokens.** `NoLiveActivityToken` gick inte att felsöka — man såg att något
  saknades men inte vilket av de två. Samma princip som widgetens rad i `PushLog`: ett svar som
  inte går att felsöka är halvvägs till ett fel som inte går att hitta.

## Verifierat

Fysisk iPhone 16 Pro. `Uppdatera lokalt` och `Avsluta` + `Starta` ger båda en korrekt renderad
aktivitet, och **färskhetsraden nollställs** — vilket en stämplad sträng aldrig kunde göra.

### De frusna ringarna: förklarade, och inte det som fixades

Dynamic Island visade en progress-ring på var sida. De satt där `CompactLeading` och
`CompactTrailing` renderas, och fanns **före** layoutändringen — de orsakades alltså inte av den
stämplade tiden.

Orsaken var en **föräldralös aktivitet**: den hade startats före flera ominstallationer av appen och
överlevde dem. En Live Activity håller en referens till attributtypen från den binär som startade
den, och när extensionen byts ut under fötterna på den kan iOS inte längre avkoda den — den visar sin
platshållare i stället, för alltid. Uppdateringarna togs emot (`sent 1`) men kunde inte ritas.

Det förklarade tre observationer som annars inte hängde ihop: att ringarna fanns med den gamla
layouten, att "först hände ingenting alls", och att `sent 1` inte motsvarades av något synligt.

En nystartad aktivitet renderar normalt. Att en aktivitet kan överleva en ominstallation i ett
tillstånd där den varken går att rendera eller bli av med är värt ett eget fynd — se nedan.

## Uppföljning

- Samplet bör avsluta kvarvarande aktiviteter vid start, så en ominstallation inte lämnar en
  orenderbar aktivitet efter sig.
- `RemoveInvalidAsync` raderar hela installationen när en leverans är `Invalid`, även när kuvertet
  adresserades till en **aktivitetstoken**. En död aktivitet skulle då avregistrera enheten från all
  push. Testat mot en gammal token: APNs svarade `sent 1` och tappade den tyst, så det gick inte att
  framkalla — men kodvägen finns. Misstanke, inte fynd.
