# Widgets everywhere: examples

**Related:** [widgets-everywhere.md](widgets-everywhere.md), where the format and the decisions are.

Every widget and Live Activity in Glukos, Puckkoll and Almanacka (as of 2026-11-03) written as a layout file, with its model. They are a design, not compiled; step W12 made Glukos's real and proved it against the JSON of the day, and Almanacka followed on 2026-11-04 (notes under each).

## Glukos

*Proven in W12 (#755):* the files as they compile and write today's JSON are `tests/Plugin.Maui.Spine.Layouts.Tests/Layouts/Glucose.layout.xml` and `GlucoseNoValue.layout.xml` (and in Glukos, on its branch `spine/layout-files`). They differ from the draft below where the draft would have changed the JSON: `HorizontalTextAlignment="End"` and `MaxLines="1" ShrinkToFit="0.7"` for the age, a `Choose Shared="Unit"` for the unit, no `Part` (Glukos has none yet), `Parameters="double Size"`, `Expand`, `Prefix=' · '` quoted, and a model whose `Value` and `LastHour` are never null (`HasValue`, `HasGraph`).

### The reading (`GlucoseActivity.layout.xml`)

```xml
<!-- Glukos.Shared/Layouts/GlucoseActivity.layout.xml: one person's reading, in every place iOS and Android show it. -->
<LiveActivity xmlns="https://schemas.cosmomedia.se/spine/layout"
              Model="Glukos.GlucoseActivityModel"
              Background="#000000"
              Link="{Link}">

    <!-- Sent once in the payload and used in several regions: LiveActivityLayout.Shared, as today. -->
    <LiveActivity.Shared>
        <Label Shared="Title" Text="{Name}" FontSize="15" FontAttributes="Bold" TextColor="Primary" />
        <Elapsed Shared="Age" Since="{MeasuredAt}" Format="Ago" FontSize="Caption" TextColor="Secondary"
                 HorizontalOptions="End" MaxLines="1" ShrinkToFit="0.7" />
        <HStack Shared="Unit" Spacing="4">
            <Label Text="{Unit}" FontSize="Caption" FontAttributes="Bold" TextColor="Secondary" />
            <Label Text="· {Below}" If="{Below}" FontSize="Caption" FontAttributes="Bold" TextColor="{BelowColor}" />
        </HStack>
        <!-- The last hour, one value every five minutes: the same curve on the lock screen, in the island and in CarPlay. -->
        <Sparkline Shared="Graph" If="{HasGraph}" Values="{LastHour}" From="{GraphFrom}" To="{MeasuredAt}" Step="5m"
                   Color="{Static GlucoseColors.InRange}" Height="56" Area="True" Dot="True"
                   BandLow="{TargetLow}" BandHigh="{TargetHigh}"
                   AboveColor="{Static GlucoseColors.High}" BelowColor="{Static GlucoseColors.Low}" />
    </LiveActivity.Shared>

    <!-- Copied into each place it is used, at that place's size: reuse in the file, not in the payload. -->
    <Template Name="Reading" Size="double">
        <HStack Spacing="6">
            <Label Text="{Value}" Part="Value" FontSize="{Size}" FontDesign="Rounded" FontAttributes="Bold" TextColor="{Color}" />
            <Choose>
                <When If="{IsStale}">
                    <Icon Source="exclamationmark.circle" Tint="#FF9500" Size="{Size, Scale=0.6}" />
                </When>
                <When If="{Symbol}">
                    <Icon Source="{Symbol}" Part="Symbol" Tint="{Color}" Size="{Size, Scale=0.7}" />
                </When>
                <Otherwise>
                    <Label Text="{Arrow}" FontSize="{Size, Scale=0.7}" FontAttributes="Bold" TextColor="{Color}" />
                </Otherwise>
            </Choose>
        </HStack>
    </Template>

    <!-- The reading with the graph beside it, the graph always taking the right half; under them the unit. -->
    <Template Name="Row" Size="double" GraphHeight="double">
        <VStack Spacing="2">
            <HStack Spacing="12" If="{HasGraph}">
                <VStack Fill="True"><Reading Size="{Size}" /></VStack>
                <VStack Fill="True"><Use Shared="Graph" Height="{GraphHeight}" /></VStack>
            </HStack>
            <Reading Size="{Size}" IfNot="{HasGraph}" />
            <Use Shared="Unit" />
        </VStack>
    </Template>

    <!-- Lock Screen, and the Android notification drawn on Android 16.0. -->
    <VStack Spacing="10" Padding="16">
        <HStack Spacing="8">
            <Use Shared="Title" />
            <Spacer />
            <Use Shared="Age" />
        </HStack>
        <Row Size="40" GraphHeight="56" />
    </VStack>

    <!-- Inset from the island's rounded corners, which otherwise clip the name and the age. -->
    <LiveActivity.Island>
        <Island.Leading>
            <HStack Spacing="0" Padding="6"><Use Shared="Title" /></HStack>
        </Island.Leading>
        <Island.Trailing>
            <HStack Spacing="0" Padding="6"><Spacer /><Use Shared="Age" /></HStack>
        </Island.Trailing>
        <Island.Bottom>
            <HStack Spacing="0" Padding="10"><Row Size="40" GraphHeight="48" /></HStack>
        </Island.Bottom>
        <Island.CompactLeading>
            <Icon Source="drop" Tint="{Color}" />
        </Island.CompactLeading>
        <Island.CompactTrailing>
            <Label Text="{Value} {Arrow, Fallback=!}" FontSize="Headline" FontDesign="Rounded" FontAttributes="Bold" TextColor="{Color}" />
        </Island.CompactTrailing>
        <Island.Minimal>
            <Label Text="{Value}" FontSize="Headline" FontDesign="Rounded" FontAttributes="Bold" TextColor="{Color}" />
        </Island.Minimal>
    </LiveActivity.Island>

    <!-- CarPlay's Dashboard and the watch's Smart Stack: the lock screen in less room. -->
    <LiveActivity.Small>
        <Choose>
            <When If="{HasGraph}">
                <HStack Spacing="12">
                    <VStack Spacing="2" Fill="True">
                        <Use Shared="Title" />
                        <Reading Size="26" />
                        <Use Shared="Unit" />
                    </VStack>
                    <VStack Spacing="4" Fill="True">
                        <HStack Spacing="0"><Spacer /><Use Shared="Age" /></HStack>
                        <Use Shared="Graph" Height="36" />
                    </VStack>
                </HStack>
            </When>
            <Otherwise>
                <VStack Spacing="2">
                    <HStack Spacing="8"><Use Shared="Title" /><Spacer /><Use Shared="Age" /></HStack>
                    <Reading Size="26" />
                    <Use Shared="Unit" />
                </VStack>
            </Otherwise>
        </Choose>
    </LiveActivity.Small>

    <!-- Android's promoted Live Update: the unit and high or low, as under the value on iOS. -->
    <LiveActivity.Notification Title="{Name} {Value} {Arrow, Fallback=!}"
                               Text="{Unit}{Below, Prefix= · }"
                               Chip="{Value}{Arrow, Fallback=!}" />
</LiveActivity>
```

### No value (`GlucoseNoValue.layout.xml`)

```xml
<!-- Glukos.Shared/Layouts/GlucoseNoValue.layout.xml: no value to show, and why, as today's first branch of Layout(). -->
<LiveActivity xmlns="https://schemas.cosmomedia.se/spine/layout"
              Model="Glukos.GlucoseActivityModel"
              Background="#000000"
              Link="{Link}">
    <VStack Spacing="6" Padding="14">
        <Label Text="{Name}" FontSize="Caption" TextColor="Secondary" />
        <Label Text="{Message}" FontSize="Headline" FontAttributes="Bold" TextColor="Primary" />
    </VStack>

    <LiveActivity.Island>
        <Island.Leading><Icon Source="drop" Tint="{Color}" /></Island.Leading>
        <Island.Center><Label Text="{Message}" FontSize="Caption" TextColor="Primary" HorizontalOptions="Center" /></Island.Center>
        <Island.CompactLeading><Icon Source="drop" Tint="{Color}" /></Island.CompactLeading>
        <Island.CompactTrailing><Label Text="–" FontSize="Headline" FontAttributes="Bold" TextColor="Secondary" /></Island.CompactTrailing>
        <Island.Minimal><Icon Source="drop" Tint="{Color}" /></Island.Minimal>
    </LiveActivity.Island>

    <LiveActivity.Small>
        <VStack Spacing="2">
            <Label Text="{Name}" FontSize="Caption" TextColor="Secondary" />
            <Label Text="{Message}" FontSize="Headline" FontAttributes="Bold" TextColor="Primary" />
        </VStack>
    </LiveActivity.Small>

    <LiveActivity.Notification Title="{Name}" Text="{Message}" />
</LiveActivity>
```

### The model

```csharp
// Glukos.Shared: what the activity shows, worked out once. Look() and the text choices move here unchanged.
public sealed record GlucoseActivityModel(
    string Name, Uri Link,
    string? Value,                    // "9,3", null when there is no value (GlucoseNoValue.layout.xml)
    string? Arrow,                    // "↘", null when the value stopped coming
    string? Symbol,                   // "arrow.down.right", Spine's drawn arrow
    WidgetColor Color, bool IsStale,
    string Unit, string? Below, WidgetColor BelowColor,   // "mmol/L", "Högt" or why the value stopped coming
    DateTimeOffset MeasuredAt,
    IReadOnlyList<(DateTimeOffset At, double Value)>? LastHour, DateTimeOffset GraphFrom, double TargetLow, double TargetHigh,
    string? Message)
{
    public bool HasGraph => LastHour is { Count: > 1 };

    public static GlucoseActivityModel From(CurrentGlucose current, GlucoseText text, Uri? link, IReadOnlyList<HistoryPoint>? hour, DateTimeOffset now) => …;

    /// <summary>For a push that would not fit APNs' 4 KB even so: no graph anywhere, as Layout(graph: false) today.</summary>
    public GlucoseActivityModel WithoutGraph() => this with { LastHour = null };
}
```

```csharp
// GlucoseFeed in the app and LiveActivityPush in Glukos.Functions, as GlucoseActivity.Layout is called today:
var model = GlucoseActivityModel.From(current, GlucoseText.For(user.Language), link, hour, now);
LiveActivityLayout layout = model.Value is null ? GlucoseNoValueLayout.Build(model) : GlucoseActivityLayout.Build(model);

// Before switching: today's C# and the file must write the same JSON, for every recorded case in the tests.
Assert.Equal(GlucoseActivity.Layout(current, text, link, hour, now).ToJson(), GlucoseActivityLayout.Build(model).ToJson());
```

### What the generator writes (shortened)

```csharp
// obj/…/GlucoseActivityLayout.g.cs, written by the generator on every build (shortened). Not checked in.
public static partial class GlucoseActivityLayout
{
    public static LiveActivityLayout Build(GlucoseActivityModel m)
    {
        var shared = new Dictionary<string, WidgetNode>
        {
            ["title"] = W.Label(m.Name).FontSize(15).Bold().TextColor(WidgetColor.Primary),
            ["age"] = W.Elapsed(m.MeasuredAt, ElapsedFormat.Ago).Caption().TextColor(WidgetColor.Secondary).AlignEnd().MaxLines(1).ShrinkToFit(0.7),
            ["unit"] = W.HStack(
                W.Label(m.Unit).Caption().Bold().TextColor(WidgetColor.Secondary),
                m.Below is { } below ? W.Label($"· {below}").Caption().Bold().TextColor(m.BelowColor) : null).Spacing(4),
        };
        if (m.HasGraph)
            shared["graph"] = W.Sparkline(m.LastHour!, m.GraphFrom, m.MeasuredAt, TimeSpan.FromMinutes(5), WidgetColor.FromHex(GlucoseColors.InRange), 56)
                .Band(m.TargetLow, m.TargetHigh, WidgetColor.FromHex(GlucoseColors.High), WidgetColor.FromHex(GlucoseColors.Low)).Area().Dot();

        return new LiveActivityLayout
        {
            Shared = shared,
            Background = WidgetColor.FromHex("#000000"),
            Link = m.Link,
            LockScreen = W.VStack(W.HStack(W.Use("title"), W.Spacer(), W.Use("age")).Spacing(8), Row(m, 40, 56)).Spacing(10).Padding(16),
            ExpandedLeading = W.HStack(W.Use("title")).Spacing(0).Padding(6),
            ExpandedTrailing = W.HStack(W.Spacer(), W.Use("age")).Spacing(0).Padding(6),
            ExpandedBottom = W.HStack(Row(m, 40, 48)).Spacing(0).Padding(10),
            CompactLeading = W.Icon("drop").Tint(m.Color),
            // … CompactTrailing, Minimal, Small and Android as in the file
        };
    }

    static WidgetNode Row(GlucoseActivityModel m, double size, double graphHeight) => …;      // the Row template
    static WidgetNode Reading(GlucoseActivityModel m, double size) => …;                       // the Reading template
}
```

## Puckkoll

### The match, before face-off

```xml
<!-- Puckkoll.Core/Layouts/MatchBeforeFaceOff.layout.xml: the quarter of an hour before face-off. -->
<LiveActivity xmlns="https://schemas.cosmomedia.se/spine/layout"
              Model="Puckkoll.Core.MatchModel"
              Background="#0F131A">

    <Template Name="Side" Logo="string" Title="string">
        <VStack Spacing="4" Fill="True" CenterItems="True">
            <Image Source="{Logo}" Height="44" FullColor="True" />
            <Label Text="{Title}" FontSize="Caption" FontAttributes="Bold" TextColor="#FFFFFF" />
        </VStack>
    </Template>

    <VStack Spacing="8" Padding="14">
        <HStack Spacing="0">
            <Side Logo="{HomeLogo}" Title="{HomeName}" />
            <VStack Spacing="4" Fill="True" CenterItems="True">
                <Label Text="{Venue, Fallback={Loc FaceOff}}" FontSize="Caption" TextColor="#A6FFFFFF" />
                <Label Text="{StartTime}" Part="Value" FontSize="Title" FontAttributes="Bold" TextColor="#FFFFFF" />
            </VStack>
            <Side Logo="{AwayLogo}" Title="{AwayName}" />
        </HStack>
        <!-- A system-drawn countdown takes every point it is offered, so it gets a row of its own. -->
        <Countdown Until="{StartsAt}" Prefix="{Loc FaceOffIn}" Part="Time" FontSize="Caption" TextColor="#FFFFFF" HorizontalOptions="Center" />
    </VStack>

    <!-- Two sides do not fit one Subject and one Value, so the island is spelled out. -->
    <LiveActivity.Island>
        <Island.Leading><Image Source="{HomeLogo}" Height="48" FullColor="True" /></Island.Leading>
        <Island.Trailing><Image Source="{AwayLogo}" Height="48" FullColor="True" /></Island.Trailing>
        <Island.Center><Label Text="{StartTime}" FontSize="Title" FontAttributes="Bold" /></Island.Center>
        <Island.Bottom><Countdown Until="{StartsAt}" Prefix="{Loc FaceOffIn}" FontSize="Caption" HorizontalOptions="Center" /></Island.Bottom>
        <Island.CompactLeading>
            <HStack><Image Source="{HomeLogo}" Height="24" /><Image Source="{AwayLogo}" Height="24" /></HStack>
        </Island.CompactLeading>
        <Island.CompactTrailing><Label Text="{StartTime}" FontSize="Caption" FontAttributes="Bold" /></Island.CompactTrailing>
        <Island.Minimal><Image Source="{HomeLogo}" Height="24" /></Island.Minimal>
    </LiveActivity.Island>

    <LiveActivity.Notification Title="{HomeName}–{AwayName}"
                               Text="{Loc FaceOffAt, StartTime}{Venue, Prefix= · }"
                               Chip="{StartTime}"
                               Icon="{HomeBadge}" />
</LiveActivity>
```

### The match, live and final

```xml
<!-- Puckkoll.Core/Layouts/MatchLive.layout.xml: from face-off to the final score. -->
<LiveActivity xmlns="https://schemas.cosmomedia.se/spine/layout"
              Model="Puckkoll.Core.MatchModel"
              Background="#0F131A">

    <Template Name="Side" Logo="string" Title="string">
        <VStack Spacing="4" Fill="True" CenterItems="True">
            <Image Source="{Logo}" Height="44" FullColor="True" />
            <Label Text="{Title}" FontSize="Caption" FontAttributes="Bold" TextColor="#FFFFFF" />
        </VStack>
    </Template>

    <VStack Spacing="8" Padding="14">
        <HStack Spacing="0">
            <Side Logo="{HomeLogo}" Title="{HomeName}" />
            <VStack Spacing="4" Fill="True" CenterItems="True">
                <Label Text="{PhaseText}" FontSize="Caption" TextColor="#A6FFFFFF" />
                <Label Text="{HomeGoals}–{AwayGoals}" Part="Value" FontSize="Title" FontAttributes="Bold" TextColor="#FFFFFF" />
                <Label Text="{Clock}" If="{IsRunning}" FontSize="Headline" FontAttributes="Bold" TextColor="#FFFFFF" />
            </VStack>
            <Side Logo="{AwayLogo}" Title="{AwayName}" />
        </HStack>
        <VStack Spacing="2" CenterItems="True">
            <Label Text="{Loc Shots, HomeShots, AwayShots}" FontSize="Caption" TextColor="#A6FFFFFF" />
            <!-- Newest first; none once the game is over, when the result is the news. -->
            <ForEach Items="{LatestGoals}" Max="2">
                <Label Text="{.}" FontSize="Caption" TextColor="#FFFFFF" />
            </ForEach>
        </VStack>
    </VStack>

    <LiveActivity.Island>
        <Island.Leading>
            <HStack Spacing="8">
                <Image Source="{HomeLogo}" Height="48" FullColor="True" />
                <Label Text="{HomeGoals}" FontSize="Title" FontAttributes="Bold" />
            </HStack>
        </Island.Leading>
        <Island.Trailing>
            <HStack Spacing="8">
                <Label Text="{AwayGoals}" FontSize="Title" FontAttributes="Bold" />
                <Image Source="{AwayLogo}" Height="48" FullColor="True" />
            </HStack>
        </Island.Trailing>
        <Island.Center>
            <VStack Spacing="0" CenterItems="True">
                <Label Text="{PhaseShort} · {Clock}" If="{IsRunning}" FontSize="Caption" />
                <Label Text="{PhaseText}" IfNot="{IsRunning}" FontSize="Caption" />
                <Label Text="{Loc Shots, HomeShots, AwayShots}" FontSize="Caption" />
                <ForEach Items="{LatestGoals}" Max="1">
                    <Label Text="{.}" FontSize="Caption" />
                </ForEach>
            </VStack>
        </Island.Center>
        <Island.CompactLeading>
            <HStack><Image Source="{HomeLogo}" Height="24" /><Label Text="{HomeGoals}" FontSize="Headline" FontAttributes="Bold" /></HStack>
        </Island.CompactLeading>
        <Island.CompactTrailing>
            <HStack><Label Text="{AwayGoals}" FontSize="Headline" FontAttributes="Bold" /><Image Source="{AwayLogo}" Height="24" /></HStack>
        </Island.CompactTrailing>
        <Island.Minimal><Label Text="{HomeGoals}–{AwayGoals}" FontSize="Caption" FontAttributes="Bold" /></Island.Minimal>
    </LiveActivity.Island>

    <LiveActivity.Notification Title="{HomeName} {HomeGoals}–{AwayGoals} {AwayName}"
                               Text="{PhaseShort} · {Clock} · {Loc Shots, HomeShots, AwayShots}"
                               Chip="{HomeGoals}–{AwayGoals}"
                               Icon="{HomeBadge}" />
</LiveActivity>
```

### The match's model

```csharp
// Puckkoll.Core: one model for both match files. Texts stays where it is and fills the words that depend on the game.
public sealed record MatchModel(
    string HomeName, string AwayName,            // "Djurgården", "Björklöven"
    string HomeLogo, string AwayLogo,            // stored assets: "logo-dif"
    string HomeBadge,                            // Android's large icon
    int HomeGoals, int AwayGoals, int HomeShots, int AwayShots,
    string PhaseText, string PhaseShort,         // "Period 2", "P2"; "Paus efter P1", "Paus"
    string Clock, bool IsRunning,                // "17:19"
    IReadOnlyList<string> LatestGoals,           // "1–(2) Grahn 05:35", newest first, empty at the final score
    DateTimeOffset StartsAt, string StartTime,   // "19:00", in Stockholm's time as Texts does today
    string? Venue)
{
    public static MatchModel From(GameState game) => …;   // what MatchActivity.Layout works out today
}

// The app and MatchNotifier pick the file by phase:
LiveActivityLayout layout = game.Phase == GamePhase.Scheduled
    ? MatchBeforeFaceOffLayout.Build(MatchModel.From(game))
    : MatchLiveLayout.Build(MatchModel.From(game));
```

### The team widget

```xml
<!-- Puckkoll.Core/Layouts/Team.layout.xml: the followed team's widget, live or between games, in five sizes. -->
<Widget xmlns="https://schemas.cosmomedia.se/spine/layout"
        Model="Puckkoll.Core.TeamEntry"
        Background="{LinearGradient #1C222D, #0B0E13}">

    <!-- Logo, score, logo in three equal columns. -->
    <Template Name="Board" LogoSize="double">
        <HStack Spacing="0">
            <VStack Fill="True" CenterItems="True"><Image Source="{Live.HomeLogo}" Height="{LogoSize}" FullColor="True" /></VStack>
            <VStack Fill="True" CenterItems="True"><Label Text="{Live.HomeGoals}–{Live.AwayGoals}" FontSize="Title" FontAttributes="Bold" TextColor="#FFFFFF" /></VStack>
            <VStack Fill="True" CenterItems="True"><Image Source="{Live.AwayLogo}" Height="{LogoSize}" FullColor="True" /></VStack>
        </HStack>
    </Template>

    <Template Name="Header">
        <HStack Spacing="6">
            <Image Source="{TeamLogo}" Height="28" FullColor="True" />
            <Label Text="{TeamName}" FontSize="Headline" FontAttributes="Bold" TextColor="#FFFFFF" />
            <Spacer />
        </HStack>
    </Template>

    <!-- At the final score the button turns the widget to the next game. -->
    <Template Name="ShowNext">
        <Button Action="next" If="{Live.IsFinal}" HorizontalOptions="Center">
            <Label Text="{Loc NextGame}" FontSize="Caption" FontAttributes="Bold" TextColor="#F5C400" />
        </Button>
    </Template>

    <Widget.Small>
        <Choose>
            <When If="{IsLive}">
                <VStack Spacing="6" CenterItems="True">
                    <Spacer />
                    <Board LogoSize="38" />
                    <Label Text="{Live.PhaseLine}" FontSize="Caption" TextColor="#A6FFFFFF" />
                    <ForEach Items="{Live.LatestGoals}" Max="1">
                        <Label Text="{.}" FontSize="Caption" TextColor="#FFFFFF" />
                    </ForEach>
                    <ShowNext />
                    <Spacer />
                </VStack>
            </When>
            <When If="{HasNext}">
                <VStack Spacing="4">
                    <Header />
                    <Spacer />
                    <Label Text="{Loc Next} · {Next.HomeOrAway}" FontSize="Caption" TextColor="#A6FFFFFF" />
                    <HStack Spacing="6">
                        <Image Source="{Next.OpponentLogo}" Height="22" FullColor="True" />
                        <Label Text="{Next.OpponentName}" FontSize="Body" FontAttributes="Bold" TextColor="#FFFFFF" />
                    </HStack>
                    <Label Text="{Next.When}" FontSize="Caption" FontAttributes="Bold" TextColor="#F5C400" />
                </VStack>
            </When>
            <Otherwise>
                <VStack Spacing="4">
                    <Header />
                    <Spacer />
                    <Label Text="{Loc NoMoreGames}" FontSize="Caption" TextColor="#A6FFFFFF" />
                </VStack>
            </Otherwise>
        </Choose>
    </Widget.Small>

    <Widget.Medium>
        <Choose>
            <When If="{IsLive}">
                <VStack Spacing="4" CenterItems="True">
                    <HStack Spacing="0">
                        <VStack Spacing="4" Fill="True" CenterItems="True">
                            <Image Source="{Live.HomeLogo}" Height="40" FullColor="True" />
                            <Label Text="{Live.HomeName}" FontSize="Caption" FontAttributes="Bold" TextColor="#FFFFFF" />
                        </VStack>
                        <VStack Fill="True" CenterItems="True">
                            <Label Text="{Live.HomeGoals}–{Live.AwayGoals}" FontSize="Title" FontAttributes="Bold" TextColor="#FFFFFF" />
                        </VStack>
                        <VStack Spacing="4" Fill="True" CenterItems="True">
                            <Image Source="{Live.AwayLogo}" Height="40" FullColor="True" />
                            <Label Text="{Live.AwayName}" FontSize="Caption" FontAttributes="Bold" TextColor="#FFFFFF" />
                        </VStack>
                    </HStack>
                    <Label Text="{Live.PhaseLine}" FontSize="Caption" TextColor="#A6FFFFFF" />
                    <Label Text="{Loc Shots, Live.HomeShots, Live.AwayShots}" FontSize="Caption" TextColor="#A6FFFFFF" />
                    <ForEach Items="{Live.LatestGoals}" Max="3">
                        <Label Text="{.}" FontSize="Caption" TextColor="#FFFFFF" />
                    </ForEach>
                    <ShowNext />
                </VStack>
            </When>
            <Otherwise>
                <VStack Spacing="4">
                    <Header />
                    <Spacer />
                    <Label Text="{Loc Next} · {Next.HomeOrAway}" If="{HasNext}" FontSize="Caption" TextColor="#A6FFFFFF" />
                    <HStack Spacing="8" If="{HasNext}">
                        <Image Source="{Next.OpponentLogo}" Height="22" FullColor="True" />
                        <Label Text="{Next.OpponentName}" FontSize="Body" FontAttributes="Bold" TextColor="#FFFFFF" />
                        <Spacer />
                        <Label Text="{Next.When}" FontSize="Caption" FontAttributes="Bold" TextColor="#F5C400" />
                    </HStack>
                    <Label Text="{Loc NoMoreGames}" IfNot="{HasNext}" FontSize="Caption" TextColor="#A6FFFFFF" />
                    <Spacer />
                    <HStack Spacing="10">
                        <Label Text="{Loc Latest}" FontSize="Caption" TextColor="#A6FFFFFF" />
                        <Spacer />
                        <ForEach Items="{Recent}" Max="3">
                            <Label Text="{Text}" FontSize="Caption" FontAttributes="Bold" TextColor="{OnBool IsWin, True=#5BD27A, False=#FF6B6B}" />
                            <ForEach.Empty>
                                <Label Text="{Loc NoGamesYet}" FontSize="Caption" TextColor="#A6FFFFFF" />
                            </ForEach.Empty>
                        </ForEach>
                    </HStack>
                </VStack>
            </Otherwise>
        </Choose>
    </Widget.Medium>

    <!-- The lock screen tints its widgets itself, so these carry no colours. -->
    <Widget.LockRectangle>
        <VStack Spacing="2">
            <Choose>
                <When If="{IsLive}">
                    <Label Text="{Live.HomeShort} {Live.HomeGoals}–{Live.AwayGoals} {Live.AwayShort}" FontSize="Headline" FontAttributes="Bold" />
                    <Label Text="{Live.PhaseLine}" FontSize="Caption" />
                    <Label Text="{Live.LastGoal, Fallback={Loc Shots, Live.HomeShots, Live.AwayShots}}" FontSize="Caption" />
                </When>
                <Otherwise>
                    <Label Text="{TeamShort} {Loc Versus} {Next.OpponentShort}" If="{HasNext}" FontSize="Headline" FontAttributes="Bold" />
                    <Label Text="{Next.When} · {Next.HomeOrAway}" If="{HasNext}" FontSize="Caption" />
                    <Label Text="{TeamName}" IfNot="{HasNext}" FontSize="Headline" FontAttributes="Bold" />
                    <Label Text="{Loc NoMoreGames}" IfNot="{HasNext}" FontSize="Caption" />
                    <Label Text="{Loc Previous}: {Previous}" If="{Previous}" FontSize="Caption" />
                </Otherwise>
            </Choose>
        </VStack>
    </Widget.LockRectangle>

    <Widget.LockCircle>
        <VStack Spacing="0" CenterItems="True">
            <Label Text="{Live.HomeGoals}–{Live.AwayGoals}" If="{IsLive}" FontSize="Headline" FontAttributes="Bold" />
            <Label Text="{Live.PhaseShort}" If="{IsLive}" FontSize="Caption" />
            <Label Text="{Next.OpponentShort, Fallback={TeamShort}}" IfNot="{IsLive}" FontSize="Caption" FontAttributes="Bold" />
            <Label Text="{Next.StartTime}" If="{Next}" IfNot="{IsLive}" FontSize="Caption" />
        </VStack>
    </Widget.LockCircle>

    <Widget.LockLine>
        <Label Text="{Live.HomeShort} {Live.HomeGoals}–{Live.AwayGoals} {Live.AwayShort} · {Live.PhaseShort}" If="{IsLive}" />
        <Label Text="{TeamShort} {Loc Versus} {Next.OpponentShort} {Next.When}" IfNot="{IsLive}" />
    </Widget.LockLine>
</Widget>
```

```xml
<!-- Puckkoll.Core/Layouts/NoTeam.layout.xml: before a team is chosen. No model, one tree for every size. -->
<Widget xmlns="https://schemas.cosmomedia.se/spine/layout"
        Background="{LinearGradient #1C222D, #0B0E13}">
    <VStack Spacing="4">
        <Icon Source="puck" Tint="#FFFFFF" />
        <Label Text="Puckkoll" FontSize="Headline" FontAttributes="Bold" TextColor="#FFFFFF" />
        <Label Text="{Loc ChooseTeam}" FontSize="Caption" TextColor="#A6FFFFFF" />
    </VStack>
</Widget>
```

### The team widget's model

```csharp
// Puckkoll.Core: one entry of the team widget's timeline.
public sealed record TeamEntry(
    string TeamName, string TeamShort, string TeamLogo,
    LiveEntry? Live,                  // the game being played, or the final score for an hour after it
    NextEntry? Next,
    string? Previous,                 // "V 3–2 mot Luleå"
    IReadOnlyList<ResultEntry> Recent)
{
    public bool IsLive => Live is not null;
    public bool HasNext => Next is not null;
}

public sealed record LiveEntry(string HomeName, string AwayName, string HomeShort, string AwayShort, string HomeLogo, string AwayLogo,
    int HomeGoals, int AwayGoals, int HomeShots, int AwayShots, string PhaseLine, string PhaseShort, string? LastGoal,
    IReadOnlyList<string> LatestGoals, bool IsFinal);
public sealed record NextEntry(string OpponentName, string OpponentShort, string OpponentLogo, string HomeOrAway, string When, string StartTime);
public sealed record ResultEntry(string Text, bool IsWin);

// TeamWidget.Build keeps the timeline: now, and "Nedsläpp!" at the next face-off, which the platform turns to by itself.
var timeline = TeamLayout.Timeline([(now, TeamEntry.From(overview, now)), (next.StartsAt, TeamEntry.AtFaceOff(overview, next))])
    .Refresh(overview.Live is null ? TimeSpan.FromMinutes(60) : TimeSpan.FromMinutes(15));

// The button's handler gets a generated constant, so a renamed action is a build error on both sides.
if (action.ActionId == TeamLayout.Actions.Next) …
```

### Strings

```xml
<!-- Puckkoll/Resources/Strings/strings.sv.xml (and strings.xml in English): what {Loc …} reads. -->
<resources>
    <string name="FaceOff">Nedsläpp</string>
    <string name="FaceOffIn">Nedsläpp om </string>
    <string name="FaceOffAt">Nedsläpp {0}</string>
    <string name="Shots">Skott {0}–{1}</string>
    <string name="Next">Nästa</string>
    <string name="NextGame">Nästa match ›</string>
    <string name="Latest">Senaste</string>
    <string name="Previous">Förra</string>
    <string name="Versus">mot</string>
    <string name="NoMoreGames">Inga fler matcher i schemat</string>
    <string name="NoGamesYet">Inga spelade matcher än</string>
    <string name="ChooseTeam">Välj ditt lag i appen</string>
    <!-- A count picks .one or .other, as Spine's Get(key, n) does. -->
    <string name="Goals.one">{0} mål</string>
    <string name="Goals.other">{0} mål</string>
</resources>
```

## Almanacka

*Done in Almanacka (Almanacka #6 on Spine 0.1.39, #7 on 0.1.40):* the files that compile and write the JSON of the C# they replaced are `src/Almanacka.Widgets/Layouts/Today.layout.xml`, `Plain.layout.xml`, `Minimal.layout.xml` and `Week.layout.xml` in Almanacka, with `DayEntry`, `WeekEntry` and `WidgetLayoutTests`. They differ from the draft below where the draft would have changed the JSON or did not compile:

- The gradients say `Direction=Diagonal`, because a `WidgetGradient` is vertical unless it says otherwise, and `BackgroundImage` is the attribute the draft guessed: on 0.1.39 it did not exist, and the picture stayed in C# until #780 added it.
- `{Note, Fallback='v {Week}'}` wrote the braces literally on 0.1.39 (a `Choose` stood in) and reads the binding from 0.1.40.
- In the week, each day's two lines sit in a `<Template Name="Centered">` (an HStack of a spacer, `<Content />` and a spacer), as the C# helper did, instead of `Fill` and `CenterItems`. Today's box is `Background="{OnBool IsToday, True=Red}" CornerRadius="{OnBool IsToday, True=8}"`, since a corner radius on the other days would change their JSON.
- `DayEntry` also carries the `Theme` the app paints and names every stored picture (`home/<slot>.png`, `theme/<name>.png`), so the providers store under the model's names. It has no `Date`.
- Plain, minimal and week take the generated `Timeline` from 0.1.40, which puts their fixed background on the timeline once, as the C# did. On 0.1.39 it put the background on every entry, and Almanacka lifted it onto the timeline by hand.
- The models and files live in a plain `net10.0` project, `Almanacka.Widgets`, so the tests can build them; the providers in the app only draw and store the pictures.

### Today

```xml
<!-- Almanacka/Widgets/Today.layout.xml: today's leaf on the home and lock screens, on its day's theme. -->
<Widget xmlns="https://schemas.cosmomedia.se/spine/layout"
        Model="Almanacka.Widgets.DayEntry"
        Background="{LinearGradient #6D9BDD, #B7CDF0}"
        BackgroundImage="{ThemeImage}">

    <!-- The leaf is a picture the app draws for each day. -->
    <Widget.Small>
        <Image Source="{HomeLeaf}" FullColor="True" />
    </Widget.Small>

    <Widget.LockRectangle>
        <VStack Spacing="0">
            <Label Text="{Weekday} {Day} {MonthShort}" FontSize="Headline" />
            <Label Text="{Names}" />
            <Label Text="{Note, Fallback=v {Week}}" FontSize="Caption" TextColor="Secondary" />
        </VStack>
    </Widget.LockRectangle>

    <Widget.LockCircle>
        <Image Source="{LockLeaf}" />
    </Widget.LockCircle>

    <!-- After the date iOS already shows there; an en space and a thin space match its own gap. -->
    <Widget.LockLine>
        <Label Text="·&#x2002;&#x2009;v {Week}&#x2002;&#x2009;·&#x2002;&#x2009;{Names}" />
    </Widget.LockLine>
</Widget>
```

### This week

```xml
<!-- Almanacka/Widgets/Week.layout.xml: this week, its seven days, the red ones red, today marked. -->
<Widget xmlns="https://schemas.cosmomedia.se/spine/layout"
        Model="Almanacka.Widgets.WeekEntry"
        Background="Surface">
    <VStack Spacing="12">
        <HStack>
            <Label Text="v {Week}" FontSize="Headline" FontAttributes="Bold" />
            <Spacer />
            <Label Text="{Range}" FontSize="Headline" />
        </HStack>
        <HStack Spacing="0">
            <ForEach Items="{Days}">
                <VStack Spacing="4" Fill="True" CenterItems="True">
                    <Label Text="{WeekdayShort}" FontSize="Caption" TextColor="Secondary" />
                    <!-- Every date in a box of the same size, so today's filled one does not shift the row. -->
                    <HStack Padding="6" Background="{OnBool IsToday, True=Red}" CornerRadius="8">
                        <Label Text="{Day}" FontSize="Body" FontAttributes="Bold"
                               TextColor="{OnBool IsToday, True=OnAccent, False={OnBool IsRed, True=Red, False=Primary}}" />
                    </HStack>
                </VStack>
            </ForEach>
        </HStack>
    </VStack>
</Widget>
```

### Plain and minimal

```xml
<!-- Almanacka/Widgets/Plain.layout.xml: the leaf on the app icon's navy instead of the season's picture. -->
<Widget xmlns="https://schemas.cosmomedia.se/spine/layout"
        Model="Almanacka.Widgets.DayEntry"
        Background="{LinearGradient #44526B, #222C40, #121826}">
    <Image Source="{PlainLeaf}" FullColor="True" />
</Widget>

<!-- Almanacka/Widgets/Minimal.layout.xml: weekday, a large date and the names, drawn as a picture on a dark ground. -->
<Widget xmlns="https://schemas.cosmomedia.se/spine/layout"
        Model="Almanacka.Widgets.DayEntry"
        Background="#1C1C1E">
    <Image Source="{MinimalLeaf}" FullColor="True" />
</Widget>
```

### The models

```csharp
// Almanacka: one day, shared by Today, Plain and Minimal; the pictures are still drawn with SkiaSharp and stored first.
public sealed record DayEntry(string Weekday, int Day, string MonthShort, string Names, string? Note, int Week,
    string HomeLeaf, string LockLeaf, string PlainLeaf, string MinimalLeaf, string ThemeImage);

public sealed record WeekEntry(int Week, string Range, IReadOnlyList<WeekDay> Days);
public sealed record WeekDay(string WeekdayShort, int Day, bool IsRed, bool IsToday);

// Sixty days ahead, each entry from its midnight, as WidgetDays.Ahead gives them today.
var timeline = TodayLayout.Timeline(WidgetDays.Ahead(zone).Select(d => (d.From, DayEntry.From(Almanac.Leaf(d.Date, zone)))))
    .Refresh(TimeSpan.FromHours(1))
    .OpenUrl(_widgets.LinkFor(context.Kind));
```

## Not yet in these examples

They were written before decisions D1, D2 and D9–D11 were settled and still show `CenterItems`, `Fill`, one-number padding and a template's parameters as attributes (`Size="double"`). Read them as `Alignment="Center"`, `Expand="True"`, `Padding="…"` with `Thickness` and `Parameters="double Size"`. Puckkoll's colours and its `Side` and `Column` belong in a `Puckkoll.resources.xml` (D11).
