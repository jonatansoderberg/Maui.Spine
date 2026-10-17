# Issue #598 — Avatar presenter, scripts and Highlight tours

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/598
**Branch:** issue/598-avatar-presenter-scripts-and-highlight-tours
**Status:** Completed
**Plan:** [docs/plans/spine-avatar.md](../docs/plans/spine-avatar.md), steps B4.1–B4.2.

## Plan

- **B4.1** In `Plugin.Maui.Spine.Controls.Avatar`: `AvatarScript` (State, Say, Express, Gesture, LookAt a view or a window point, LookBack, Wait, WaitForTap), `ScriptAvatarDriver` (plays it, saying lines through the Speech or Text driver, with a caption event), `AvatarView.PlayAsync(script, ct, caption)`, and `AvatarPresenter` (avatar + caption bubble, placement, `Text`, `Speak`, `ShowsCaption`, `PlayAsync`, `Semantic.Merge`).
- **B4.2** A contract in the core that Highlight and the avatar both know, so neither depends on the other: `ITipPresenter` (`Present(TipPresentation)`, `Dismiss()`). Highlight gets `TourStep.Presenter`, `TourStep.Gesture` and `Highlight.Presenter`; the tour card shows the presenter beside the tip and tells it each step. `AvatarPresenter` implements it: it looks at the target, plays the gesture and says the tip. The Showcase's **Avatar guide** page is the onboarding demo.

## Changes

- Core (`Plugin.Maui.Spine`): `ITipPresenter`, `TipPresentation`.
- Highlight: `TourStep.Presenter`, `TourStep.Gesture`, `Highlight.Presenter`; `SpotlightView` lays the presenter left of the tip (the card grows by its width), keeps the same presenter across steps, and calls `Present` as a step shows and `Dismiss` when the tour fades out; a Spotlight hint passes `Highlight.Presenter`.
- Avatar Core: `AvatarDriver.Tick`/`Interrupted` (internal, for a driver that composes others); `TextAvatarDriver.Length`.
- Avatar: `AvatarScript`, `ScriptAvatarDriver`, `AvatarPresenter`, `AvatarPlacement`, `AvatarView.PlayAsync`.
- Showcase: `Pages/AvatarGuide/` (a presenter playing a five-step script, a three-step tour with Nova in the card, a one-step hint); the Avatar page attaches its view to the view model at once (it missed it before: gestures and timed expressions did nothing).
- Docs: `avatars.md`, `highlight.md`, the `spine-controls` skill.

## Decisions

- **`ITipPresenter` lives in the core**, the one package both Highlight and the avatar reference; Highlight stays free of avatars and the avatar free of Highlight. It is general: any view can present a tip.
- **The presenter sits left of the tip in the card**, 96 points wide, and the card grows by that width (up to the window's margin); the same presenter across steps stays in place so its avatar is not reloaded.
- **A script's `Say` waits until the line is said**: the Speech driver's utterance, or the Text driver going idle. The caption follows the words the voice has reached, or the Text driver's even pace.
- **`ScriptAvatarDriver` composes the Text and Speech drivers** through internal `Tick`/`Interrupted` hooks on `AvatarDriver`, attaching only the one saying the current line (each sets the playback clock).
- **`AvatarPresenter.Text` plays a one-line script**, so setting a text, playing a script and presenting a tour step share one path; a new one cancels the old.
- **Presenting a tour step**: look at the target's centre, play the gesture, say the tip, then look back.
- **The onboarding demo runs over the page's own small start screen** (search, settings, new note) rather than the Showcase's start page: the tour's targets must be on screen with the page that starts it, and a self-contained page needs no reach into the main page.

## Verification (iPhone 17 Pro simulator, Debug, the Showcase with the scratch harness)

| Check | Result |
| --- | --- |
| Script | The five steps play: Connecting, the spoken line (Speaking, EstimatedText) with the caption filling in, Happy with a nod, a mouthed line looking at the button, "Tap me to go on." and the wait; a tap on the avatar goes on to "Thanks!" and the end |
| Tour | The card shows Nova left of "Search finds any note by a word in it." with Step 1 of 3, Skip and Next; the guide says the tip |
| Hint | The Spotlight card shows Nova beside "Your streak lives here…", Speaking |
| Page margin | No warning in the console |

NotMeasured: moving through all three tour steps (Next is inside the overlay, which the harness does not reach: the gesture on steps 2 and 3 and the look towards each target were not seen), Escape on Windows, VoiceOver and TalkBack reading the card, Android.
