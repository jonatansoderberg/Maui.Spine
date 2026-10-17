using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Controls.Avatar;

namespace MauiSpineSampleApp.Pages.AvatarGuide;

public partial class AvatarGuidePageViewModel(IHighlights highlights) : SampleViewModel
{
    private const string TourKey = "showcase.avatar.tour";
    private static readonly AvatarSource Nova = AvatarSource.FromMauiAsset("Avatars/nova.spineavatar");

    private AvatarPresenter? _presenter;
    private VisualElement? _scriptButton, _search, _newNote, _settings;
    private CancellationTokenSource? _playing;

    // Shown in the tour card and beside the hint: one presenter, so the avatar loads once.
    private readonly AvatarPresenter _guide = new() { Avatar = Nova, ShowsCaption = false, Speak = true, AvatarSize = 88 };

    public AvatarSource Avatar => Nova;

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial bool Speak { get; set; } = true;

    [ObservableProperty]
    public partial bool IsHintOn { get; set; }

    internal void Attach(AvatarPresenter presenter, VisualElement scriptButton, VisualElement search, VisualElement newNote, VisualElement settings, VisualElement streak)
    {
        _presenter = presenter;
        _scriptButton = scriptButton;
        _search = search;
        _newNote = newNote;
        _settings = settings;
        Plugin.Maui.Spine.Controls.Highlight.SetPresenter(streak, _guide);
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _playing?.Cancel();
        return Task.CompletedTask;
    }

    partial void OnSpeakChanged(bool value) => _guide.Speak = value;

    /// <summary>Five steps: a state, a spoken line, an expression with a nod, a glance at a button with a mouthed line, a tap.</summary>
    [RelayCommand]
    private async Task PlayScript()
    {
        if (_presenter is null || _scriptButton is null)
            return;
        _playing?.Cancel();
        _playing = new CancellationTokenSource();
        var script = AvatarScript.Create()
            .State(AvatarState.Connecting).Wait(TimeSpan.FromMilliseconds(600))
            .State(AvatarState.Idle)
            .Say("Hi, I am Nova. I can read a line aloud.", Speak)
            .Express(AvatarExpression.Happy).Gesture("nod")
            .LookAt(_scriptButton).Say("Or only mouth it, like this, while I look at the button.", speak: false)
            .LookBack().Say("Tap me to go on.", speak: false)
            .WaitForTap()
            .Express(AvatarExpression.Confident).Say("Thanks!", Speak);
        Status = "Playing the script…";
        try
        {
            await _presenter.PlayAsync(script, _playing.Token);
            Status = "The script has ended.";
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped.";
        }
    }

    /// <summary>Three steps over the page's own start screen, with Nova in the tour card looking at each control.</summary>
    [RelayCommand]
    private async Task StartTour()
    {
        if (_search is null || _newNote is null || _settings is null)
            return;
        highlights.Reset(TourKey);
        var result = await highlights.TourAsync(TourKey,
            new TourStep(_search, "Search finds any note by a word in it.") { Title = "Search", Presenter = _guide },
            new TourStep(_newNote, "Start a new note here.") { Title = "New note", Presenter = _guide, Gesture = "nod" },
            new TourStep(_settings, "Your settings, themes and sync live here.") { Title = "Settings", Presenter = _guide, Gesture = "lean" });
        Status = $"The tour ended: {result.Outcome}.";
    }

    [RelayCommand]
    private void ShowHint() => IsHintOn = !IsHintOn;
}
