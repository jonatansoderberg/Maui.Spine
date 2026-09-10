# Glass buttons (Liquid Glass)

`Glass.Style` renders a `Button` or `ImageButton` as Liquid Glass on iOS 26 and Mac Catalyst 26. It is an attached property on the ordinary controls: no new control type, no handler registration, and the same markup renders the platform's normal button everywhere else.

---

## Platforms

| Platform | Result |
|---|---|
| iOS 26+ / iPadOS 26+ | ✅ Liquid Glass via `UIButtonConfiguration` |
| iOS 15–18 | The attribute is ignored; the button renders as today |
| macOS Catalyst 26 | 🚧 Same code path as iOS, not yet verified |
| Android | No change (`MaterialButton` / `ShapeableImageView`, Material 3 when `UseMaterial3` is on) |
| Windows (WinUI 3) | No change (Fluent) |

---

## Registration

None. `UseSpine()` registers the handler mapping. Add the namespace to the app's `GlobalXmlns.cs` so `Glass.Style` works without a prefix, like `SvgImageSource.Svg` does:

```csharp
[assembly: XmlnsDefinition(
    "http://schemas.microsoft.com/dotnet/maui/global",
    "Plugin.Maui.Spine.Extensions", AssemblyName = "Plugin.Maui.Spine")]
```

---

## XAML usage

```xml
<!-- a text button -->
<Button Text="Save" Glass.Style="Prominent" Command="{Binding SaveCommand}" />

<!-- an icon button with one of Spine's SVGs -->
<ImageButton Style="{StaticResource SvgImageButtonStyle}"
             SvgImageSource.Svg="settings.svg" SvgImageSource.Padding="10"
             Glass.Style="Regular" WidthRequest="44" HeightRequest="44"
             Command="{Binding OpenSettingsCommand}" />

<!-- every button that uses a style -->
<Style x:Key="FloatingAction" TargetType="Button">
    <Setter Property="Glass.Style" Value="Regular" />
</Style>
```

From C#: `Glass.SetStyle(button, GlassStyle.Regular)`.

---

## `GlassStyle`

| Value | Surface | Foreground |
|---|---|---|
| `None` | The platform's normal button | — |
| `Regular` | Frosted glass | The button's `TextColor` |
| `Prominent` | Glass tinted with the button's `BackgroundColor` | The button's `TextColor` (white in the default styles) |
| `Clear` | Nearly transparent glass, for buttons over photos or maps | The button's `TextColor` |
| `ProminentClear` | Tinted, nearly transparent | The button's `TextColor` |
| `Transient` | Nothing at rest; regular glass materialises while the button is pressed. For an icon floating on rich content that should not read as a control until touched | The button's `TextColor` |

Apple's guidance: glass is for the controls that float over content (navigation, a floating action), not for buttons inside the content, and never glass on top of glass. Use `Clear` only over visually rich backgrounds.

---

## What the glass takes over

| Property | On glass |
|---|---|
| `BackgroundColor` / `Background` | Not painted. For `Prominent` and `ProminentClear` it becomes the tint of the glass, so a prominent button needs one |
| `CornerRadius`, `BorderWidth`, `BorderColor` | Not drawn; the glass is a capsule with its own rim |
| `Text`, `FontFamily`, `FontSize`, `FontAttributes`, `CharacterSpacing`, `TextColor` | Honoured. The default app style's white `TextColor` is meant for filled buttons; give glass buttons a label-like colour, for example `{AppThemeBinding Light=Black, Dark=White}` |
| `Padding` | Becomes the glass content insets when set; when `0` the system's own capsule insets apply |
| `ContentLayout` (a `Button` with an image) | Image placement and spacing |
| `ImageButton` image | Shown at the size it was rendered, centred, with no inset from the capsule. `SvgImageSource.Padding="10"` gives a 24-point glyph in a 44-point circle, the size a navigation bar uses |
| `IsEnabled` | The system dims a disabled glass button itself; `Opacity` setters in the app's visual states still apply |
| Visual states that set `BackgroundColor` | Neutralised; hover and press feedback is the glass's own |

Setting `Glass.Style` back to `None` at runtime restores the normal button.

---

## The header bar

On iOS 26 and Mac Catalyst 26, Spine's header bar renders its back button and page actions as glass, the way a `UINavigationBar` shows its items. This is on by default and switched off in `UseSpine`:

```csharp
builder.UseSpine(options =>
{
    options.Apple.GlassHeaderActions = false;
});
```

---

## Do not

- Do **not** rely on `CornerRadius` or a gradient `Background` for a glass button; the glass draws its own capsule.
- Do **not** stack a glass button on another glass surface.
- Do **not** expect the platform look to change on Android or Windows: the attribute is a no-op there by design, so the same page can carry it for every platform.
