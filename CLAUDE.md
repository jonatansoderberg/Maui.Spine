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
