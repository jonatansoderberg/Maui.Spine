# Issue #549 — Showcase: Generated images uses a Shimmer as the pending view

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/549
**Branch:** issue/549-showcase-generated-images-uses-a-shimmer-as-the-pe
**Status:** Completed

## Plan
#547 (PR #548) made `Shimmer` draw when its content is a single empty block and taught it per-corner radii. Put the intended placeholder back on the Showcase's Generated images page: a `Shimmer` (`IsLoading="True"`) around an empty `Border` (`RoundRectangle 12,12,0,0`, height 200) as `GeneratedImage.PendingView`, in place of the `ActivityIndicator`, and the same in the page's code sample. Close out step 2.3c (#543, merged as PR #545) in the plan.

## Open Questions

## Changes
- `GeneratedImagesPage.View.xaml`: the pending view is a `Shimmer` over an empty `Border` shaped like the card's top; the code sample shows it and the `PendingView` line that had gone missing; the summary says "a shimmering placeholder"; `Plugin.Maui.Spine.Controls.Shimmer` is listed among the page's packages.
- `docs/plans/ai-and-security.md`: step 2.3c and the Spine.AI package row set to Done (#545), Last updated, Plan history, a Decision for the Shimmer, and the verification backlog row notes what was seen.

## Decisions
- `IsLoading="True"` is set on the Shimmer itself: `GeneratedImage` only toggles the pending view's `IsVisible`, and `IsLoading` defaults to false.
- Verified on the iPhone 17 Pro simulator (iOS 26.4, light theme) against the messaging sample's API with an invalid OpenAI key and Azurite in Docker: pending showed the block with a moving wave, rounded top and square bottom; after about 90 seconds the card turned to failed with the shimmer hidden. Not seen in dark theme, on Android, Windows or Mac Catalyst. CI is paused.
