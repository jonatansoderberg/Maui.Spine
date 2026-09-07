---
name: spine-issue
description: Start working on a Maui.Spine GitHub issue. Fetches issue details, creates a branch, and sets up the issues changelog file with a documented plan. Invoke as /spine-issue <issue-id>.
---

You are starting work on a GitHub issue in the Maui.Spine repository (https://github.com/jonatansoderberg/Maui.Spine).

The user invoked `/spine-issue` with an issue number. Extract it from `$ARGUMENTS` — it is the first (and only) token, e.g. `123`.

Follow these steps **in order**. Do not skip any step. Do not write code yet.

---

## Step 1 — Fetch the issue

Run:
```
gh issue view <id> --repo jonatansoderberg/Maui.Spine --json number,title,body,labels,assignees,state
```

Parse the JSON output to extract:
- `number` — issue ID
- `title` — full title
- `body` — description / acceptance criteria
- `labels` — label names
- `state` — open/closed

If the issue does not exist or the command fails, tell the user and stop.

---

## Step 2 — Derive branch name and slug

Convert the title to a kebab-case slug:
- Lowercase everything
- Replace spaces and underscores with `-`
- Strip all characters that are not alphanumeric or `-`
- Collapse multiple consecutive `-` into one
- Trim leading/trailing `-`
- Truncate to 50 characters maximum

Branch name: `issue/<number>-<slug>`
Changelog filename: `issues/<number>-<slug>.md`

---

## Step 3 — Create the branch

Check if the branch already exists:
```
git branch --list issue/<number>-<slug>
```

If it does NOT exist, create it from the current HEAD:
```
git checkout -b issue/<number>-<slug>
```

If it already exists, check it out:
```
git checkout issue/<number>-<slug>
```

Tell the user which branch is now active.

---

## Step 4 — Analyse the codebase

Before writing the plan, read relevant parts of the codebase to understand the existing structure:
- Look at `src/` to understand affected areas based on the issue title/body.
- Read any files directly relevant to the issue.
- Check recent git log for context: `git log --oneline -10`

Use this to inform a concrete, realistic plan.

---

## Step 5 — Write the initial plan

Think carefully about the issue. Consider:
- What is the root cause or feature gap?
- What files / layers need to change?
- Are there multiple valid approaches? If so, list them with trade-offs.
- Are there any open questions that require user input before proceeding?

**If anything is ambiguous or if you see two or more equally valid approaches, STOP and ask the user for guidance before writing the changelog.**

Once you are confident (or have received guidance), write the plan section. Be concrete — name files, types, methods where you know them. Do not be vague.

---

## Step 6 — Create the changelog file

Create `issues/<number>-<slug>.md` in the solution root with the following content (fill in all placeholders):

```markdown
# Issue #<number> — <title>

**GitHub:** https://github.com/jonatansoderberg/Maui.Spine/issues/<number>
**Branch:** issue/<number>-<slug>
**Status:** In Progress

## Plan

<your plan here — concrete steps, affected files, key decisions already made>

## Open Questions

<any questions that still need user input — leave empty if none>

## Changes

<!-- Updated as work progresses -->

## Decisions

<!-- Non-obvious choices and why -->
```

Do NOT commit this file yet. Just create it on disk.

---

## Step 7 — Report to the user

Tell the user:
- The branch that was created/checked out
- The changelog file path
- A concise summary of the plan
- Any open questions you need answered before writing code

Then wait for the user to confirm the plan or answer open questions before touching any source files.

---

## Important rules

- **Do not write any C# or project code** until the user has approved the plan.
- **Always keep the changelog updated** as work progresses — add to `Changes` and `Decisions` sections.
- All C# must be modern (.NET latest, nullable reference types, file-scoped namespaces, primary constructors where appropriate).
- Match existing code style and conventions — read the surrounding files before introducing new patterns.
- If a decision point arises during implementation, update `Decisions` in the changelog and (if significant) pause to inform the user.
