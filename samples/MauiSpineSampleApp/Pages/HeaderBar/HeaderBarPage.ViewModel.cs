using System.ComponentModel;
using System.Text;

namespace MauiSpineSampleApp.Pages.HeaderBar;

public partial class HeaderBarPageViewModel : ViewModelBase
{
    static readonly string[] Palette = ["#E4572E", "#F3A712", "#29335C", "#669BBC", "#A8C686", "#7B2D26"];

    // Colourful cards with text on them, so what passes under the bar is easy to see through it.
    public IReadOnlyList<Swatch> Rows { get; } = Enumerable.Range(1, 30)
        .Select(i => new Swatch(
            $"Row {i}",
            i % 2 == 0 ? "Scroll me under the bar: its background decides how much of me shows." : "Colours show how much of the row the bar lets through.",
            Color.FromArgb(Palette[i % Palette.Length])))
        .ToList();

    public IReadOnlyList<ChoiceGroup> Groups { get; }

    readonly ChoiceGroup _layout, _largeTitle, _background, _foreground, _statusBar;

    public HeaderBarPageViewModel()
    {
        _layout = new("Layout",
        [
            new("Normal", "The content starts below the bar, and a list scrolls under it when the background lets it show. Most pages.", () => HeaderBarMode = HeaderBarMode.Normal),
            new("Overlay", "The content starts at the top of the screen, behind the status bar and the bar. A page that opens on a photo, a map or a hero.", () => HeaderBarMode = HeaderBarMode.Overlay),
        ]);

        _largeTitle = new("Large title",
        [
            new("Off", "The title sits in the bar.", () => LargeTitle = false),
            new("On", "The page opens on a large title that collapses into the bar as it scrolls. Top-level pages, as in Mail and Settings.", () => LargeTitle = true),
        ]);

        _background = new("Background",
        [
            new("Auto", "What the platform's own bar does: on iOS the navigation bar's default when a list fills the page (Soft on iOS 26, Hard from iOS 27), Transparent under Overlay, Solid otherwise. The default; pick another value only to get a look on purpose.", () => HeaderBarBackground = HeaderBarBackground.Auto),
            new("Solid", "The page's colour: content under the bar is hidden. A classic bar, or a photo that gives way to a plain bar once it scrolls.", () => HeaderBarBackground = HeaderBarBackground.Solid),
            new("Transparent", "Nothing: content shows through the bar, and the title floats over it. A photo or a map under an Overlay bar.", () => HeaderBarBackground = HeaderBarBackground.Transparent),
            new("Soft", "SoftEdge: content fades and blurs into the whole header, the navigation bar's default on iOS 26. A fading band on Android and Windows.", () => HeaderBarBackground = HeaderBarBackground.SoftEdge),
            new("Status bar", "SoftStatusBar: the same soft fade behind the status bar only; rows stay sharp behind the title and the actions. For a bar that should stay light.", () => HeaderBarBackground = HeaderBarBackground.SoftStatusBar),
            new("Hard", "HardEdge: a frosted, nearly opaque band with a clear bottom edge, the navigation bar's default from iOS 27. A nearly opaque band with a hairline on Android and Windows.", () => HeaderBarBackground = HeaderBarBackground.HardEdge),
        ]);

        _foreground = new("Foreground",
        [
            new("Theme", "The title and the icons follow light and dark mode.", () => HeaderBarForeground = null),
            new("White", "A fixed white title and icons, for a dark photo under a Transparent bar. Over a Solid or scroll edge bar in light mode it disappears.", () => HeaderBarForeground = Colors.White),
        ]);

        _statusBar = new("Status bar",
        [
            new("Default", "The clock and icons follow the theme.", () => StatusBarStyle = StatusBarStyle.Default),
            new("Light", "White clock and icons, for a dark photo at the top.", () => StatusBarStyle = StatusBarStyle.LightContent),
            new("Dark", "Black clock and icons, for a light photo at the top.", () => StatusBarStyle = StatusBarStyle.DarkContent),
        ]);

        Groups = [_layout, _largeTitle, _background, _foreground, _statusBar];
    }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Sync();
        return Task.CompletedTask;
    }

    // The attribute sets these properties before the page appears; the footer sets them later.
    // Either way the chips, the explanation and the code follow.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(HeaderBarMode) or nameof(LargeTitle) or nameof(HeaderBarBackground)
            or nameof(EffectiveHeaderBarBackground) or nameof(HeaderBarForeground) or nameof(StatusBarStyle))
            Sync();
    }

    void Sync()
    {
        // Spine's base class sets properties before this class's constructor has built the groups.
        if (Groups is null)
            return;

        _layout.Select(HeaderBarMode == HeaderBarMode.Overlay ? 1 : 0);
        _largeTitle.Select(LargeTitle ? 1 : 0);
        _background.Select((int)HeaderBarBackground);
        _foreground.Select(HeaderBarForeground is null ? 0 : 1);
        _statusBar.Select((int)StatusBarStyle);

        OnPropertyChanged(nameof(Resolved));
        OnPropertyChanged(nameof(Code));
    }

    /// <summary>What Spine made of the background on this page and platform.</summary>
    public string Resolved => HeaderBarBackground == HeaderBarBackground.Auto
        ? $"Auto is {EffectiveHeaderBarBackground} on this page."
        : EffectiveHeaderBarBackground == HeaderBarBackground
            ? $"{HeaderBarBackground} is drawn as asked."
            : $"{HeaderBarBackground} is drawn as {EffectiveHeaderBarBackground} here (before iOS 26, or with Reduce Transparency on).";

    /// <summary>The attribute (and the XAML it needs) for the combination on screen.</summary>
    public string Code
    {
        get
        {
            var settings = new List<string>();

            if (HeaderBarMode == HeaderBarMode.Overlay)
                settings.Add("HeaderBar = HeaderBarMode.Overlay");
            if (LargeTitle)
                settings.Add("LargeTitle = true");
            if (HeaderBarBackground != HeaderBarBackground.Auto)
                settings.Add($"HeaderBarBackground =\n    HeaderBarBackground.{HeaderBarBackground}");
            if (HeaderBarForeground is not null)
                settings.Add("HeaderBarForeground = \"#FFFFFF\"");
            if (StatusBarStyle != StatusBarStyle.Default)
                settings.Add($"StatusBarStyle =\n    StatusBarStyle.{StatusBarStyle}");

            var code = new StringBuilder("[NavigableRegion(Title = \"Inbox\"");
            foreach (var setting in settings)
                code.Append(",\n  ").Append(setting);
            code.Append(")]");

            if (LargeTitle)
                code.Append("\n\n<CollectionView>\n  <CollectionView.Header>\n    <HeaderBarLargeTitle />\n  </CollectionView.Header>");

            if (HeaderBarMode == HeaderBarMode.Overlay)
                code.Append("\n\n// The page keeps clear what it wants:\n// SafeArea.ScrollInset=\"Top\" on a list, or\n// SafeAreaInsets.Top from the view model.");

            code.Append("\n\n// Live, from the view model:\nHeaderBarBackground =\n  HeaderBarBackground.")
                .Append(HeaderBarBackground).Append(';');

            return code.ToString();
        }
    }
}

public sealed record Swatch(string Name, string Text, Color Colour);

public sealed partial class Choice(string label, string description, Action choose) : ObservableObject
{
    public string Label { get; } = label;

    public string Description { get; } = description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Choose() => choose();
}

public sealed partial class ChoiceGroup(string name, IReadOnlyList<Choice> choices) : ObservableObject
{
    public string Name { get; } = name;

    public IReadOnlyList<Choice> Choices { get; } = choices;

    [ObservableProperty]
    public partial Choice? Selected { get; private set; }

    public void Select(int index)
    {
        for (var i = 0; i < Choices.Count; i++)
            Choices[i].IsSelected = i == index;

        Selected = Choices[index];
    }
}
