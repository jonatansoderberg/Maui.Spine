# Issue #626 — Showcase avatar gallery: the packages' posters on the cards

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/626
**Branch:** issue/626-gallery-posters
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), after C1

## Plan
B1.4 drew a live avatar on each gallery card until the packages had real posters. Since C1 they do, so the cards show the posters and the avatar comes alive on its own page.

## Changes
- `AvatarsPageViewModel` reads each bundled package once, off the UI thread (`AvatarArchive.Read`), and gives its entry the light and dark posters. A package that cannot be read keeps a live avatar on its card, which shows the package's own error.
- `AvatarEntry` gets `Poster`, `Live` and `LiveSource`. Kairo's card stays live: the sample server serves the lab's Kairo, whose posters are 1×1 until it is packed (#542).
- The cards show one image per theme, with the gallery's summary and code example to match.
- Plan: the C1 decision line and Last updated.

## Decisions
- **One image per theme** rather than `AppThemeBinding` around two `Binding`s, which left `Image.Source` unset on iOS (seen on the simulator: posters loaded, sources null).
- **The package is read in full** (hash-checked) for its posters, as an app should do with content it did not make; the four packages take one read each when the gallery opens.

## Verified
- iPhone 17 simulator: four cards with posters, the light ones in the light theme and the dark ones in the dark theme, no live avatar on the gallery; Nova's page still draws at 30 fps.

## Not verified (NotMeasured)
- Android and Windows, where the saving matters most (the live cards drew on the CPU).
