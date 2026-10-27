# Settings pages

How a Spine app's settings page is laid out, so the settings of every app built on Spine read the same: the same order of sections, the same icon colours, the same marks for what works and what needs the user, and a page that opens at once. The pieces are [`SpineSection` and `SpineRow`](rows.md); this page is the convention for putting them together.

## The page

```xml
<SpinePage x:Class="MyApp.Pages.SettingsPage"
           x:TypeArguments="SettingsPageViewModel"
           x:DataType="SettingsPageViewModel"
           BackgroundColor="{AppThemeBinding Light={x:Static SpineSection.PageBackgroundLight},
                                             Dark={x:Static SpineSection.PageBackgroundDark}}">
    <ScrollView>
        <VerticalStackLayout Padding="{x:Static HeaderBarConstants.PagePadding}" Margin="0,12,0,24"
                             Spacing="28" MaximumWidthRequest="640">
            <SpineSection Header="{String Settings.Display}" Footer="{String Settings.DisplayFooter}">
                ...
            </SpineSection>
        </VerticalStackLayout>
    </ScrollView>
</SpinePage>
```

- **The grouped background** (`SpineSection.PageBackgroundLight`/`Dark`) behind the sections. A Mac window with a translucent backdrop keeps its backdrop instead.
- **The page margin** from `HeaderBarConstants.PagePadding` on the stack inside the `ScrollView`, never a number of your own and never on the `ScrollView` (see the page guidelines in the repository's `CLAUDE.md`). `Margin="0,12,0,24"` on the stack leaves air above the first section and below the last; the page still scrolls under the bottom bar.
- **28 points between sections**, as `Spacing` on the stack, not a `Margin` on each section.
- **At most 640 points wide**, centred, so the rows don't stretch across a Mac window or an iPad in landscape.
- **The title "Settings"** from the app's strings (`Settings.Title`). An app with more than one language sets it again after the attribute's title has been applied (see [macOS options](macos-options.md#the-page-title-from-navigableregiontitle--overwrites-a-localized-one)).

## Open at once, load after

A tap on the settings button shows the page at once. Nothing slow happens before it is on screen:

- **The view model's constructor and the start of `OnAppearingAsync`** (everything before its first `await`) read only what is already in memory or in `Preferences`. The keychain, `SecureStorage`, the network, a process (`/usr/bin/security`, a CLI), HealthKit's authorisation and a sign-in status come after an `await`, off the main thread where they block.
- **Region pages** run `OnAppearingAsync` without holding the navigation, but its synchronous start still runs before the transition. **Sheets wait for it**: Spine presents a sheet after its `OnAppearingAsync` has completed, so a sheet starts its loads without awaiting them (`_ = RefreshAsync()`, or [`Load`](loading-states.md) with a `TaskState`).
- **Show the last known state first.** Keep what the last check found (in the service, or a static per page) and fill the rows from it in the constructor; update them when the new check is done. A row that waits for its first answer shows its place with the last known or a neutral value (`Value="…"`), not an empty gap that fills in and pushes the rows below it down.
- **No spinner over the page.** A row that is being checked keeps its place; a failure says so in the section's footer or in a line above the first section, not in an alert.

```csharp
public SettingsPageViewModel(AccountService account)
{
    _account = account;
    Email = account.LastKnownEmail;            // in memory: shown with the page
}

public override Task OnAppearingAsync(NavigationDirection direction)
{
    _ = RefreshAsync();                        // keychain and network after the page is up
    return base.OnAppearingAsync(direction);
}

private async Task RefreshAsync()
{
    var (email, hasKey) = await Task.Run(() => (_account.ReadEmail(), _keys.Has("api")), PageLifetime);
    Email = email;
    KeyStatus = hasKey ? SpineRowStatus.Ok : SpineRowStatus.NeedsAttention;
}
```

## Order of sections

From what the app is about to what is about the app, as in the platform's own Settings:

1. **Account**: signing in, the signed-in account and its sign-in methods, signing out (a grey row at the end of the section).
2. **What the app works with**: the sources, services, devices, people or the place the app's content comes from, one section each when there are several.
3. **Display**: theme, language, units, clock and the app's own display choices.
4. **The system**: notifications, launch at login, Apple Health, motion effects; only the ones the device has.
5. **More**: links to other apps and web pages.
6. **About**: About <app> (a sheet with the app's page), Send feedback and Version (see below).
7. **Delete**: deleting the account or all data, alone in the last section, with a footer that says what goes.

A section has a header when it has more than one row or when its row doesn't say what it is about on its own. A footer explains what a choice does or where something comes from, in one or two sentences; it is not a place for status that changes, except a section's own last-sync or error line.

## Rows

| A row that … | Use |
|---|---|
| opens a page or a sheet | `Command`; the chevron shows by itself |
| switches something on or off | a `Switch` as `Accessory`, no `Command`: a tap anywhere flips it |
| picks one of a few options (theme, unit, language) | `Menu` with a `MenuPicker`; the pick is the row's value |
| picks one of many options, or options with explanations | `Command` to a page of `IsSelected` rows |
| shows something that works (a saved key, a linked account) | `Status="Ok"`, `Command` to manage it, no chevron |
| shows something that needs the user (a missing key, an expired sign-in) | `Status="NeedsAttention"`, `Command` to fix it; `ShowChevron="True"` when the fix is a page |
| leaves the app (a web page, another app, mail) | `Command`, the `ExternalLink.svg` icon, or `Mail.svg` for mail |
| deletes, removes or signs out for good | `IsDestructive="True"`, the `Delete.svg` icon on red, a confirmation before it acts |
| states a fact (the version) | `Title` and `Value`, nothing else |

A long detail scrolls rather than truncating: set `DetailMarquee="True"` on every row with a `Detail`.

## Icons and their colours

Every row of a settings page has an icon on a badge (`IconBackground`). The badge's colour says what kind of setting it is, the same on every row and in every app, so the colour is never decoration:

| Colour | | For |
|---|---|---|
| Grey | `#8E8E93` | The app itself and the system: launch at login, sign out, version |
| Blue | `#007AFF` | What the app shows and how: theme, language, units, clock, display options, show or hide |
| Purple | `#AF52DE` | The things the app works with: a device, people, a place |
| Pink | `#FF2D55` | Access and sign-in: the account, keys, tokens, sign-in methods, permissions |
| Indigo | `#5856D6` | Leaving the app: web pages, other apps, feedback by mail |
| Teal | `#30B0C7` | Fetching and syncing: fetch now, reconnect, sync status |
| The service's own colour | | A named third-party service's own row (its logo or icon, its brand colour): Apple Health, a data source, an AI provider |

Green, orange and red are never a kind of setting:

- **Green** and **orange** are the [`Status`](rows.md#status-menus-and-destructive-rows) marks: green works, orange needs the user. A row that needs the user keeps its own icon colour and gets the orange mark.
- **Red** (`#FF3B30`) is the badge of a row with `IsDestructive`, and only of those.

Glyphs come from `Plugin.Maui.Spine.Svg.Icons`: `Delete.svg` for deleting and removing (never `Trashcan.svg`), `SignOut.svg` for signing out, `ExternalLink.svg` for a web page, `Mail.svg` for feedback, `Info.svg` for About, `Tag.svg` for the version, `Theme.svg` for the theme, `Globe.svg` for the language, `Key.svg` for a key, `User.svg` for an account, `Reload.svg` for fetching now (`Refresh.svg` is a sync symbol).

## About

The last section but the delete section, with the header "About". It always has the same three rows, in this order:

```xml
<SpineSection Header="{String Settings.About}">
    <SpineRow Icon="Info.svg" IconBackground="#8E8E93" Title="{String Settings.AboutApp}"
              Command="{Binding OpenAboutCommand}" />
    <SpineRow Icon="Mail.svg" IconBackground="#5856D6" Title="{String Settings.Feedback}"
              Command="{Binding SendFeedbackCommand}" />
    <SpineRow Icon="Tag.svg" IconBackground="#8E8E93" Title="{String Settings.Version}"
              Value="{Binding Version}" Detail="{Binding BuildTime}" />
</SpineSection>
```

- **About <app>** opens a sheet with the app's own page: what the app does, what is important to know (a medical or financial disclaimer, unofficial data sources), privacy, sources, licences and how to reach the developer. The text is a markdown file in the app (`Resources/Raw/about.md`, and `about.sv.md` beside it for each further language), drawn with [`MarkdownView`](markdown.md) in a scrolling sheet. The sheet loads the file after it is up (a sheet waits for `OnAppearingAsync`), and links in it open with `Launcher`. Licences and sources live there, not in footers.
- **Send feedback** opens a mail to the developer with the app's name and version as the subject, through `Email.Default.ComposeAsync`, or a `mailto:` link when Mail isn't set up.
- **Version** shows `AppInfo.Current.VersionString` as the value, `1.0.431494`, and the build time as the detail, `27 October 2026 16:47`, in the app's language. No brackets and no separate build string.

A footer under the section is only for what must be seen without opening the sheet, such as "not a medical device".

### Version and build number

Every build gets a new build number: the minutes since 1 January 2026 UTC. It is the app's `ApplicationVersion` (`CFBundleVersion`, Android's `versionCode`) and the third part of `ApplicationDisplayVersion`, so the version is always `major.minor.build` and always increases. Set it in the app's project, with the app's own major and minor version:

```xml
<PropertyGroup>
    <!-- Minutes since 2026-01-01 UTC: a new, increasing build number for every build. The app turns it back
         into the build time. 639028224000000000 is the ticks of that date. -->
    <AppBuildNumber Condition="'$(AppBuildNumber)' == ''">$([System.Math]::Floor($([MSBuild]::Divide($([MSBuild]::Subtract($([System.DateTime]::UtcNow.Ticks), 639028224000000000)), 600000000))))</AppBuildNumber>
    <ApplicationDisplayVersion>1.0.$(AppBuildNumber)</ApplicationDisplayVersion>
    <ApplicationVersion>$(AppBuildNumber)</ApplicationVersion>
</PropertyGroup>
```

The settings page turns it back into the build time, so no second value is stamped into the build:

```csharp
public string BuildTime => int.TryParse(AppInfo.Current.BuildString, out var minutes)
    ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes).ToLocalTime().ToString("d MMMM yyyy HH:mm", culture)
    : "";
```

A release build can pin the number with `-p:AppBuildNumber=<n>`. The widget extension's own version is still fixed at `1.0` / `1` in `Plugin.Maui.Spine.Widgets`.

## Sample

The Showcase's **Rows** page has a section of menu rows, status marks and a destructive row.
