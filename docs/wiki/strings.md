# Strings

`ISpineStrings` is one store for the app's user-facing text and for the text Spine's own
controls show. The app and the framework read it the same way, and the app overrides any key a
control uses by defining that key itself.

## Documents

The default source is XML, one document per culture, embedded in the app:

```xml
<!-- Resources/Strings/strings.sv.xml -->
<strings culture="sv">
  <s key="Home.Greeting">Hej {0}!</s>
  <s key="Events.Count.one">{0} tävling</s>
  <s key="Events.Count.other">{0} tävlingar</s>
  <s key="Header.Back">Tillbaka</s>   <!-- overrides Spine's own text for this key -->
</strings>
```

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Strings\*.xml" WithCulture="false" />
</ItemGroup>
```

`WithCulture="false"` matters: without it MSBuild reads the `.sv.` in `strings.sv.xml` as a culture
and moves the file into a satellite assembly, where Spine does not look.

`strings.xml` is the neutral document, `strings.<culture>.xml` a culture's, anywhere in the
assembly's folders. Spine finds them in every assembly given to `options.AddAssembly`. Keys are
dotted: a Spine package owns a prefix (`Header.*`, and `Calendar.*`, `DataGrid.*` as the controls
arrive), the app owns the rest.

## Reading

In XAML, `{String}` resolves a key and re-resolves when the culture changes:

```xml
<Label Text="{String Home.Greeting, Args={Binding UserName}}" />
<Label Text="{String Events.Count, Count={Binding Total}}" />
<Button Text="{String Home.Refresh}" />
```

`Args`, `Arg1` and `Arg2` bind `{0}` to `{2}`; `Count` picks the plural form and is `{0}`.

In C#, inject `ISpineStrings`, or read `SpineStrings.Current` in a control that is built before
dependency injection is reachable:

```csharp
var title = _strings["Home.Title"];
var greeting = _strings.Get("Home.Greeting", user.Name);
var count = _strings.Get("Events.Count", events.Count);   // Events.Count.one / .zero / .other
```

The plural forms are `key.one` for one, `key.zero` for none when that key exists, otherwise
`key.other`.

## Culture

The store starts in `CultureInfo.CurrentUICulture`. Setting `Culture` switches at runtime: every
`{String}` re-evaluates, `Changed` is raised, and views tracked through `SpineTheme.Track` repaint,
so a code-drawn control repaints for a language switch through the same path as for a theme switch.
The choice is stored in `Preferences` and applied again at the next launch; turn that off with
`options.Strings.Persist = false`.

```csharp
_strings.Culture = CultureInfo.GetCultureInfo("sv");
```

## Resolution and fallback

Providers are asked in order: the ones added with `options.Strings.AddProvider`, then the embedded
documents of the registered assemblies, then each Spine package's defaults. For every provider the
culture chain is walked (`sv-SE`, then `sv`, then the neutral document) before the next provider is
asked, so an app's neutral text for a key still beats a package's translation of it. Resolved values
are cached per culture; a cached lookup allocates nothing.

A key no provider has resolves to the key itself, so it is visible on screen, and is reported once
through `Missing` and the debug output.

## Other sources

- `ResxStringProvider(MyResources.ResourceManager)` reads an existing `.resx`, so an app keeps
  what it has: `options.Strings.AddProvider(new ResxStringProvider(AppResources.ResourceManager))`.
- `XmlStringProvider` takes documents from anywhere: `.Add("sv", () => File.OpenRead(path))`.
- `IStringProvider` is one method, `Load(CultureInfo)`, returning the key/value pairs for exactly
  that culture or `null`. A database or a remote file fits; call `SpineStrings.Current.Reload()`
  when its content changed.

## Spine's own text

`Plugin.Maui.Spine` ships `Header.Back` and `Header.Close` (English and Swedish), read for the
back and close buttons' screen-reader descriptions. A control package ships its defaults the same
way and registers them with `SpineStrings.Current.AddDefaults(...)` from its `UseXxx()` call; the
app's own documents always win.
