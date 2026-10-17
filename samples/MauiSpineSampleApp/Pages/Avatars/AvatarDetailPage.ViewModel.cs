using Plugin.Maui.Spine.Controls;
using Plugin.Maui.Spine.Controls.Avatar;

namespace MauiSpineSampleApp.Pages.Avatars;

public partial class AvatarDetailPageViewModel : SampleViewModel, IReceivesNavigationParameter<AvatarEntry>
{
    private static readonly int[] Sizes = [64, 128, 320];
    private static readonly int[] FrameRates = [15, 30, 60];

    private readonly TextAvatarDriver _text = new();
    private AvatarView? _view;

    public IReadOnlyList<AvatarState> States { get; } = Enum.GetValues<AvatarState>();

    public IReadOnlyList<AvatarExpression> Expressions { get; } = Enum.GetValues<AvatarExpression>();

    [ObservableProperty]
    public partial AvatarEntry? Entry { get; set; }

    /// <summary>0 drives the avatar by hand, 1 mouths text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManual), nameof(IsText))]
    public partial int DriverIndex { get; set; }

    public bool IsManual => DriverIndex == 0;

    public bool IsText => DriverIndex == 1;

    [ObservableProperty]
    public partial AvatarDriver? Driver { get; set; }

    [ObservableProperty]
    public partial AvatarState State { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial AvatarExpression Expression { get; set; }

    [ObservableProperty]
    public partial AvatarExpression TimedExpression { get; set; } = AvatarExpression.Happy;

    [ObservableProperty]
    public partial double Intensity { get; set; } = 0.9;

    [ObservableProperty]
    public partial double DurationSeconds { get; set; } = 3;

    [ObservableProperty]
    public partial string Text { get; set; } = "Hello! I am an avatar, and this text is mouthed from its letters, with no audio.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Size))]
    public partial int SizeIndex { get; set; } = 2;

    public int Size => Sizes[SizeIndex];

    /// <summary>System, Reduced, Full.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotionMode))]
    public partial int MotionIndex { get; set; }

    public AvatarMotionMode MotionMode => (AvatarMotionMode)MotionIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameRate))]
    public partial int FrameRateIndex { get; set; } = 1;

    public int FrameRate => FrameRates[FrameRateIndex];

    public Task OnNavigationParameterAsync(AvatarEntry entry)
    {
        Entry = entry;
        return Task.CompletedTask;
    }

    public override Task OnDisappearingAsync(NavigationDirection navigationDirection)
    {
        _text.Clear();
        return Task.CompletedTask;
    }

    internal void Attach(AvatarView view) => _view = view;

    partial void OnDriverIndexChanged(int value)
    {
        _text.Clear();
        Driver = value == 1 ? _text : null;
    }

    [RelayCommand]
    private void ShowExpression() =>
        _view?.SetExpression(new(TimedExpression, (float)Intensity, TimeSpan.FromSeconds(DurationSeconds)));

    [RelayCommand]
    private Task Gesture(string name) => _view?.PlayGestureAsync(name) ?? Task.CompletedTask;

    [RelayCommand]
    private void Interrupt() => _view?.Interrupt();

    [RelayCommand]
    private void Mouth()
    {
        if (!string.IsNullOrWhiteSpace(Text))
            _text.Mouth(Text);
    }

    /// <summary>A reply streamed in pieces, as a chat answer arrives: Thinking first, then each piece as it comes.</summary>
    [RelayCommand]
    private async Task Stream(CancellationToken cancellationToken)
    {
        _text.Clear();
        _text.Begin();
        await Task.Delay(900, cancellationToken);
        foreach (var piece in Text.Split(' '))
        {
            _text.Append(piece + " ");
            await Task.Delay(Random.Shared.Next(60, 220), cancellationToken);
        }
        _text.Complete();
    }

    [RelayCommand]
    private void Stop() => _text.Clear();
}
