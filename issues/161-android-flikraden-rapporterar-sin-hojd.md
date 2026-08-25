# Issue #161 — Android: flikraden rapporterar sin höjd som inset trots att den tar sin egen plats

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/161
**Branch:** issue/161-android-flikraden-rapporterar-sin-hojd
**Status:** In Progress

## Plan

Andra fyndet ur verifieringen av Hems skrollbeteende på Android, syskon till
[#159](159-android-sida-som-utesluter-toppen.md). Varje skrollande sida får ett tomt band ovanför
flikraden.

## Changes

### `Platforms/Android/SpineTabbedHostPage.Android.cs` ✅

`ApplyTabBarInset` rapporterar **överlappet** i stället för barens höjd: hur mycket av baren som
faktiskt täcker sidans innehåll, mätt ur bådas läge på skärmen.

## Decisions

- **Överlappet, inte höjden.** Om baren täcker innehåll eller står bredvid det är MAUI:s beslut och
  inte Spines. Att mäta överlappet klarar båda arrangemangen utan att koden behöver veta vilket som
  gäller: täcker baren ingenting blir insetet noll, ritar den över innehåll kommer hela höjden
  tillbaka — vilket är precis vad det gamla ovillkorliga värdet antog.

- **Antagandet stod utskrivet i koden och stämde inte längre.** Kommentaren påstod att flikssidan
  löper hela fönstrets höjd under baren. Uppmätt är den 872 dp av 952 och slutar där baren börjar.
  Kommentaren är omskriven med mätningen i sig, så nästa läsare ser vad som faktiskt gäller.

## Verifiering ✅

Uppmätt på Android 17 (Pixel-emulator, 3x):

| | Före | Efter |
|---|---|---|
| Tävlingar, sista raden mot flikraden | tomt band av listytan | slutar med sin luft |
| Hem, sista kortet mot flikraden | stort glapp | slutar med sin luft |
| iOS | — | orört, metoden är Androids egen partial |

Build grön för iOS, Mac Catalyst och Android. Testsviten grön (536).
