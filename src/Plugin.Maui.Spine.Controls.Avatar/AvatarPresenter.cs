using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;

namespace Plugin.Maui.Spine.Controls;

/// <summary>Where a <see cref="AvatarPresenter"/>'s avatar sits beside its caption.</summary>
public enum AvatarPlacement
{
    /// <summary>The avatar left of the caption.</summary>
    Left,

    /// <summary>The avatar right of the caption.</summary>
    Right,

    /// <summary>The avatar above the caption.</summary>
    Top,
}

/// <summary>
/// An avatar that presents something: a guide in a tour card, a hint, an answer in a chat. Setting <see cref="Text"/>
/// makes it say the text (read aloud with <see cref="Speak"/> and Spine.Speech, mouthed otherwise) while a caption bubble
/// fills in with it; <see cref="PlayAsync"/> plays a whole <see cref="AvatarScript"/>. In a Highlight tour
/// (<c>TourStep.Presenter</c>) it says each step's tip, looks at the spotlighted element and plays the step's gesture.
/// </summary>
/// <remarks>A screen reader hears one element: the caption's text, or the avatar's name while it is empty.</remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <AvatarPresenter Avatar="{Binding Avatar}" Text="{Binding Caption}" Placement="Left" />
/// ]]></code>
/// </example>
public sealed class AvatarPresenter : ContentView, ITipPresenter
{
    /// <summary>The bindable property for <see cref="Avatar"/>.</summary>
    public static readonly BindableProperty AvatarProperty = BindableProperty.Create(
        nameof(Avatar), typeof(AvatarSource), typeof(AvatarPresenter),
        propertyChanged: static (b, _, n) => ((AvatarPresenter)b)._avatar.Source = (AvatarSource?)n);

    /// <summary>The bindable property for <see cref="Text"/>.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(AvatarPresenter),
        propertyChanged: static (b, _, n) => ((AvatarPresenter)b).OnTextChanged((string?)n));

    /// <summary>The bindable property for <see cref="Placement"/>.</summary>
    public static readonly BindableProperty PlacementProperty = BindableProperty.Create(
        nameof(Placement), typeof(AvatarPlacement), typeof(AvatarPresenter), AvatarPlacement.Left,
        propertyChanged: static (b, _, _) => ((AvatarPresenter)b).Arrange());

    /// <summary>The bindable property for <see cref="Speak"/>.</summary>
    public static readonly BindableProperty SpeakProperty = BindableProperty.Create(
        nameof(Speak), typeof(bool), typeof(AvatarPresenter), false);

    /// <summary>The bindable property for <see cref="ShowsCaption"/>.</summary>
    public static readonly BindableProperty ShowsCaptionProperty = BindableProperty.Create(
        nameof(ShowsCaption), typeof(bool), typeof(AvatarPresenter), true,
        propertyChanged: static (b, _, _) => ((AvatarPresenter)b).Arrange());

    /// <summary>The bindable property for <see cref="AvatarSize"/>.</summary>
    public static readonly BindableProperty AvatarSizeProperty = BindableProperty.Create(
        nameof(AvatarSize), typeof(double), typeof(AvatarPresenter), 96d,
        propertyChanged: static (b, _, _) => ((AvatarPresenter)b).Arrange());

    private readonly AvatarView _avatar = new() { MaxFramesPerSecond = 30 };
    private readonly Label _caption = new() { FontSize = 15, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Border _bubble;
    private readonly Grid _layout = new() { ColumnSpacing = 10, RowSpacing = 8 };
    private CancellationTokenSource? _playing;

    /// <summary>Creates a presenter; set <see cref="Avatar"/> to show one.</summary>
    public AvatarPresenter()
    {
        _bubble = new Border
        {
            Padding = new Thickness(12, 8),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            VerticalOptions = LayoutOptions.Center,
            Content = _caption,
        };
        _bubble.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F0F1F4"), Color.FromArgb("#2A2C31"));
        _caption.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#171A23"), Colors.White);
        AutomationProperties.SetIsInAccessibleTree(_avatar, false);
        Semantic.SetMerge(this, true);
        Content = _layout;
        Arrange();
    }

    /// <summary>The avatar.</summary>
    public AvatarSource? Avatar
    {
        get => (AvatarSource?)GetValue(AvatarProperty);
        set => SetValue(AvatarProperty, value);
    }

    /// <summary>What it says now; setting it says it, replacing what it was saying. The caption fills in as it is said.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Where the avatar sits beside the caption.</summary>
    public AvatarPlacement Placement
    {
        get => (AvatarPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Read the text aloud through Spine.Speech when the app has it; otherwise only mouth it. Default false.</summary>
    public bool Speak
    {
        get => (bool)GetValue(SpeakProperty);
        set => SetValue(SpeakProperty, value);
    }

    /// <summary>Whether the caption bubble shows; off in a tour card, which shows the tip itself. Default true.</summary>
    public bool ShowsCaption
    {
        get => (bool)GetValue(ShowsCaptionProperty);
        set => SetValue(ShowsCaptionProperty, value);
    }

    /// <summary>The avatar's width and height. Default 96.</summary>
    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    /// <summary>The avatar view inside, for its state, expression and gestures.</summary>
    public AvatarView AvatarView => _avatar;

    /// <summary>Plays <paramref name="script"/>, replacing what it was doing; the caption shows each line as it is said.</summary>
    public async Task PlayAsync(AvatarScript script, CancellationToken cancellationToken = default)
    {
        _playing?.Cancel();
        using var playing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _playing = playing;
        try
        {
            await _avatar.PlayAsync(script, playing.Token, ShowCaption);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Replaced by the next text or script.
        }
        finally
        {
            if (_playing == playing)
                _playing = null;
        }
    }

    void ITipPresenter.Present(TipPresentation tip)
    {
        var script = AvatarScript.Create().LookAt(tip.TargetFrame.Center);
        if (tip.Gesture is { } gesture)
            script.Gesture(gesture);
        _ = PlayAsync(script.Say(tip.Tip, Speak).Wait(TimeSpan.FromMilliseconds(400)).LookBack());
    }

    void ITipPresenter.Dismiss()
    {
        _playing?.Cancel();
        _avatar.LookAt(null);
    }

    private void OnTextChanged(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _playing?.Cancel();
            ShowCaption("");
            return;
        }
        _ = PlayAsync(AvatarScript.Create().Say(text, Speak));
    }

    private void ShowCaption(string text)
    {
        _caption.Text = text;
        _bubble.IsVisible = ShowsCaption && text.Length > 0;
        SemanticProperties.SetDescription(this, text.Length > 0 ? text : _avatar.Package?.Manifest.DisplayName);
    }

    private void Arrange()
    {
        _layout.Clear();
        _layout.RowDefinitions.Clear();
        _layout.ColumnDefinitions.Clear();
        _avatar.WidthRequest = _avatar.HeightRequest = AvatarSize;
        _bubble.IsVisible = ShowsCaption && !string.IsNullOrEmpty(_caption.Text);
        switch (Placement)
        {
            case AvatarPlacement.Top:
                _layout.RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)];
                _avatar.HorizontalOptions = LayoutOptions.Center;
                _layout.Add(_avatar, 0, 0);
                _layout.Add(_bubble, 0, 1);
                break;
            case AvatarPlacement.Right:
                _layout.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)];
                _layout.Add(_bubble, 0, 0);
                _layout.Add(_avatar, 1, 0);
                break;
            default:
                _layout.ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)];
                _layout.Add(_avatar, 0, 0);
                _layout.Add(_bubble, 1, 0);
                break;
        }
    }
}
