# Maui.Spine — Claude Instructions

## Repository
- **GitHub:** https://github.com/jonatansoderberg/Maui.Spine
- **Solution file:** `Spine.slnx`
- **Main source:** `src/`

## Code Standards
- All code must be modern C# / .NET (latest language features, nullable reference types enabled).
- Match the style and conventions of the existing codebase before introducing new patterns.
- No unnecessary abstractions — solve the problem at hand, not hypothetical future ones.
- Write no comments unless the *why* is non-obvious (hidden constraint, subtle invariant, specific bug workaround).

## Page and Sample Guidelines
- **Every page has the same side margin: `HeaderBarConstants.PageMargin`** (16, or 20 on wide iPhones and iPad), the line the header bar's outermost buttons sit on. Give it with `HeaderBarConstants.PagePadding` / `PageMargin`, never a hard-coded number, so every page lines up with the header bar and with every other page.
- **The margin goes inside the scroll source, never on it.** The `ScrollView` / `CollectionView` under the header bar reaches the page's sides: no side `Margin` on it and no side `Padding` on a parent between it and the page. UIKit draws the header's scroll edge effect inside the scroll view and no wider, so a narrower list leaves the bar's sides without it. This slipped into the Showcase twice (Icon set in #445; Transitions, Reorder and the push sample's Log in #304). Spine prints `[Spine] <Page>: the header bar's scroll source … leaves … of the page's sides uncovered` when it happens; check the app's console output after adding a page.
  - **`CollectionView`** (list or grid): `SafeArea.PageMargin="True"` lays its rows, header and footer out inside the page margin while the list reaches the sides. Items then have no side margin of their own; the gap between a grid's columns is `GridItemsLayout.HorizontalItemSpacing`.
  - **`ScrollView`:** `Padding="{x:Static HeaderBarConstants.PagePadding}"` on its content.

## Active Plans
Multi-issue efforts have a living plan under `docs/plans/`. When an issue belongs to one, read the plan first and update its step status, decisions and "Last updated" line in the same PR.
- `docs/plans/ai-and-security.md` — AI, voice, sign-in, biometrics and highlight (roadmap #505, issues #506–#522).

## GitHub Issue Workflow

### Starting an issue
Use the `/spine-issue <id>` skill to begin working on a GitHub issue. It will:
1. Fetch issue details from GitHub.
2. Create a branch named `issue/<id>-<slug>` (e.g. `issue/42-fix-crash-on-startup`).
3. Create `issues/<id>-<slug>.md` as the living changelog for that issue.
4. Write an initial plan section in that changelog — ask the user before proceeding if anything is ambiguous or if multiple approaches exist.

### Branch naming
```
issue/<id>-<kebab-case-title>
```
Example: `issue/17-android-ripple-overflow`

### Changelog file (`issues/<id>-<slug>.md`)
Each issue gets its own file under `issues/` at the solution root. Structure:

```markdown
# Issue #<id> — <Title>

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/<id>
**Branch:** issue/<id>-<slug>
**Status:** In Progress | Completed | Abandoned

## Plan
<!-- Written before any code. Document approach, key decisions, open questions. -->

## Changes
<!-- Updated as work progresses. One bullet per meaningful change. -->

## Decisions
<!-- Any non-obvious choices made during implementation and why. -->
```

### Rules
- Always reference the GitHub issue URL in the changelog.
- Keep the changelog updated as you work — add to **Changes** and **Decisions** as you go.
- If a decision requires user input, pause and ask before writing code.
- On PR creation, link to the issue and reference the changelog.
