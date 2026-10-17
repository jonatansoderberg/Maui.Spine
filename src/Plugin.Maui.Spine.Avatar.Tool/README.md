# Plugin.Maui.Spine.Avatar.Tool

The `spine-avatar` command for `.spineavatar` files, the avatars `AvatarView` draws (`Plugin.Maui.Spine.Controls.Avatar`). It checks a package, packs it for shipping with rendered posters, and draws sheets for review.

```bash
dotnet tool install -g Plugin.Maui.Spine.Avatar.Tool
```

```bash
spine-avatar inspect nova.spineavatar
spine-avatar validate nova.spineavatar --profile shipping
spine-avatar pack nova.spineavatar -o shipped/nova.spineavatar --strip
spine-avatar strip kairo.spineavatar -o shipped/kairo.spineavatar
spine-avatar render-sheet nova.spineavatar -o sheets
spine-avatar report shipped/*.spineavatar
```

- `pack` rewrites the manifest and bindings with the format's defaults, leaves the capabilities to be computed, renders the light and dark posters, drops `source/` with `--strip`, rewrites the SHA-256 table and validates the result.
- The shipping profile fails a placeholder poster (under 128 px) and anything under `source/`, and warns over 2 MB.
- Posters are drawn with the package's own Skia renderers on any OS. A glTF model with alpha-masked materials is drawn only by SceneKit: give `pack` pictures captured on Apple with `--poster-light` and `--poster-dark`.

Exit codes: 0 done, 1 a package failed, 2 a usage or file error.

Docs: https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/avatars.md#making-a-package
