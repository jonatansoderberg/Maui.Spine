# Agent skills

The repository ships a set of **skills** for AI coding agents (Claude Code and compatible tools) that describe how to install Spine from NuGet and use it the way the samples do. Each skill is a `SKILL.md` with the practices, the code shapes and the gotchas for one area, and links to the wiki page with the details.

| Skill | Use it when |
|---|---|
| `spine-setup` | Adding Spine to an app: which packages, `MauiProgram` registration order, `SpineApplication`, project-file entries, platform minimums, iOS App Group and push setup |
| `spine-page` | Creating or changing a page: the three-file pattern, regions, sheets, tabs, typed parameters and results, page actions, lifecycle hooks |
| `spine-widgets` | Building a widget or a Live Activity: the provider, the `W` tree, refresh, buttons, activity layouts |
| `spine-notifications` | Push and local notifications on the client and the `Plugin.Maui.Spine.Server` backend |
| `spine-controls` | `HeroCollectionView`, `AnimatedLabel`, SVG icons, Liquid Glass buttons |

They live in [`.claude/skills/`](https://github.com/jonatansoderberg/Maui.Spine/tree/master/.claude/skills) and are picked up automatically by agents working inside this repository.

## Using them in your own app

Copy the folders into your app's repository; Claude Code loads skills from `.claude/skills/<name>/SKILL.md` and offers them as `/spine-setup`, `/spine-page`, and so on:

```bash
curl -L https://github.com/jonatansoderberg/Maui.Spine/archive/refs/heads/master.tar.gz \
  | tar -xz --strip-components=3 -C .claude/skills Maui.Spine-master/.claude/skills/spine-setup \
      Maui.Spine-master/.claude/skills/spine-page Maui.Spine-master/.claude/skills/spine-widgets \
      Maui.Spine-master/.claude/skills/spine-notifications Maui.Spine-master/.claude/skills/spine-controls
```

or clone the repository and copy `.claude/skills/spine-*` by hand. The skills are self-contained: they reference the wiki by URL, not by relative path, so they work from any repository.

A typical first session in a new app:

```
/spine-setup        add the packages and the registration
/spine-page         create the first pages
/spine-widgets      add a widget, if the app has one
/spine-notifications
```

The skills track the current package version's API. When you upgrade Spine, copy the skills again so the two stay in step.

## What the skills do not replace

They condense the wiki; they do not repeat it. An agent that needs the full property table of `[NavigableSheet]`, the Live Activity budget rules or the server's tag-expression grammar follows the links in the skill to the wiki page. The sample apps under `samples/` remain the reference for complete, building code.
