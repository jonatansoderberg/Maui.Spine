# Logo sources

The full-size originals behind `assets/icons/` (256×256 package icons) and `assets/spine-logo.png` (the project logo). Regenerate the small files from these; do not edit the small files by hand.

| File | Used for |
|---|---|
| `spine-logo.png` | The project logo (README, social preview), 1024×1024 |
| `spine.png` | `Plugin.Maui.Spine` |
| `svg.png` | `Plugin.Maui.Spine.Svg` |
| `svg-icons.png` | `Plugin.Maui.Spine.Svg.Icons` |
| `widgets.png` | `Plugin.Maui.Spine.Widgets` |
| `push-notifications.png` | `Plugin.Maui.Spine.PushNotifications` |
| `common.png` | `Plugin.Maui.Spine.Common` (`common-glow.png` is the alternative with a glow around the frame, unused) |
| `server.png` | `Plugin.Maui.Spine.Server` |
| `hero-collection-view.png` | `Plugin.Maui.Spine.Controls.HeroCollectionView` |
| `animated-label.png` | `Plugin.Maui.Spine.Controls.AnimatedLabel` |
| `calendar.png` | `Plugin.Maui.Spine.Controls.Calendar` (the HeroCollectionView original with its list badge painted over by a calendar glyph) |
| `rows.png` | `Plugin.Maui.Spine.Controls.Rows` (the HeroCollectionView original with its list bars shortened and a chevron after each) |
| `data-grid.png` | `Plugin.Maui.Spine.Controls.DataGrid` (the controls icon with a table glyph in the badge) |

Package icons are 1254×1254; nuget.org wants at most 1 MB per icon, so the packed copies are resized to 256×256:

```bash
python3 -c "from PIL import Image; import sys; Image.open(sys.argv[1]).convert('RGBA').resize((256,256), Image.LANCZOS).save(sys.argv[2], optimize=True)" assets/logo-src/widgets.png assets/icons/widgets.png
```
