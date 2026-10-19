using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Extensions;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// The field a <see cref="ChatView"/> is written in: an editor that grows to a few lines, a send button that becomes Stop
/// while an answer streams, and suggestion chips above it. Put it in the page's footer, so the page's keyboard avoidance
/// lifts it, and point it at the view.
/// </summary>
/// <example>
/// <code>
/// &lt;SpinePage.Footer&gt;
///     &lt;ChatComposer Chat="{x:Reference chat}" Suggestions="{Binding Suggestions}" Placeholder="Ask about the race" /&gt;
/// &lt;/SpinePage.Footer&gt;
/// </code>
/// </example>
public class ChatComposer : ContentView
{
    public static readonly BindableProperty ChatProperty = BindableProperty.Create(
        nameof(Chat), typeof(ChatView), typeof(ChatComposer), null,
        propertyChanged: (b, o, n) => ((ChatComposer)b).OnChatChanged(o as ChatView, n as ChatView));

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(ChatComposer), null,
        propertyChanged: (b, _, n) => ((ChatComposer)b)._editor.Placeholder = n as string ?? ChatStrings.Get("Spine.Chat.Placeholder"));

    public static readonly BindableProperty SuggestionsProperty = BindableProperty.Create(
        nameof(Suggestions), typeof(IEnumerable<string>), typeof(ChatComposer), null,
        propertyChanged: (b, _, _) => ((ChatComposer)b).ShowSuggestions());

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(ChatStyleOptions), typeof(ChatComposer), null,
        propertyChanged: (b, _, _) => ((ChatComposer)b).ApplyStyle());

    public static readonly BindableProperty AutoFocusProperty = BindableProperty.Create(
        nameof(AutoFocus), typeof(bool), typeof(ChatComposer), true);

    public static readonly BindableProperty MicrophoneCommandProperty = BindableProperty.Create(
        nameof(MicrophoneCommand), typeof(ICommand), typeof(ChatComposer), null,
        propertyChanged: (b, _, _) => ((ChatComposer)b).UpdateSend());

    public static readonly BindableProperty IsMicrophoneActiveProperty = BindableProperty.Create(
        nameof(IsMicrophoneActive), typeof(bool), typeof(ChatComposer), false,
        propertyChanged: (b, _, _) => ((ChatComposer)b).OnMicrophoneActiveChanged());

    public static readonly BindableProperty MicrophoneTextCommandProperty = BindableProperty.Create(
        nameof(MicrophoneTextCommand), typeof(ICommand), typeof(ChatComposer), null,
        propertyChanged: (b, _, _) => ((ChatComposer)b).OnMicrophoneActiveChanged());

    // Long enough for a page's push to finish, so the keyboard does not rise during the slide.
    private static readonly TimeSpan FocusDelay = TimeSpan.FromMilliseconds(400);

    private const string SendGlyph = "M 10,16 L 10,4 M 4.5,9.5 L 10,4 L 15.5,9.5";
    private const string StopGlyph = "M 6,6 L 14,6 L 14,14 L 6,14 Z";
    private const string MicrophoneGlyph = "M 10,2.5 C 8.6,2.5 7.5,3.6 7.5,5 L 7.5,9.5 C 7.5,10.9 8.6,12 10,12 C 11.4,12 12.5,10.9 12.5,9.5 L 12.5,5 C 12.5,3.6 11.4,2.5 10,2.5 Z M 4.5,9 C 4.5,12 7,14.5 10,14.5 C 13,14.5 15.5,12 15.5,9 M 10,14.5 L 10,17.5";
    private const string HangUpGlyph = "M 6,6 L 14,14 M 14,6 L 6,14";

    private readonly Editor _editor;
    private readonly Border _field;
    private readonly Border _send;
    private readonly Path _glyph;
    private readonly HorizontalStackLayout _chips = new() { Spacing = 8 };
    private readonly ScrollView _chipsScroll;

    public ChatComposer()
    {
        ChatStrings.EnsureRegistered();

        _editor = new Editor
        {
            AutoSize = EditorAutoSizeOption.TextChanges,
            Placeholder = ChatStrings.Get("Spine.Chat.Placeholder"),
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
        };
        _editor.TextChanged += (_, _) => UpdateSend();
        _editor.HandlerChanged += (_, _) => FitPlatformEditor();

        _field = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(14, 0),
            // A grid centres the editor; a border would stretch it, and the text would sit high on one line.
            Content = new Grid { Children = { _editor } },
        };

        _glyph = new Path
        {
            Stroke = Colors.White,
            Fill = Colors.Transparent,
            StrokeThickness = 2.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 20,
            HeightRequest = 20,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        // As tall as the field on one line, and at its foot when it grows, the way Messages keeps its send button.
        _send = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Ellipse(),
            VerticalOptions = LayoutOptions.End,
            Content = _glyph,
        };
        Tap.SetCommand(_send, new Command(Act));

        _chipsScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _chips,
            IsVisible = false,
        };

        var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
        row.Add(_field, 0);
        row.Add(_send, 1);

        Content = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12, 8), Children = { _chipsScroll, row } };

        Core.SpineTheme.Track(this, ApplyStyle);
        ApplyStyle();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Puts the cursor in the field and shows the on-screen keyboard when the composer appears. On by default: on a chat
    /// page, writing is what comes next.
    /// </summary>
    public bool AutoFocus
    {
        get => (bool)GetValue(AutoFocusProperty);
        set => SetValue(AutoFocusProperty, value);
    }

    /// <summary>
    /// Shows a microphone in the send button's place while the field is empty; a tap runs the command, which starts a
    /// spoken conversation (and, while <see cref="IsMicrophoneActive"/>, ends it). Unset, there is no microphone.
    /// </summary>
    /// <example>
    /// <code>
    /// &lt;ChatComposer Chat="{x:Reference chat}" MicrophoneCommand="{Binding TalkCommand}"
    ///               IsMicrophoneActive="{Binding IsTalking}" MicrophoneTextCommand="{Binding SayCommand}" /&gt;
    /// </code>
    /// </example>
    public ICommand? MicrophoneCommand
    {
        get => (ICommand?)GetValue(MicrophoneCommandProperty);
        set => SetValue(MicrophoneCommandProperty, value);
    }

    /// <summary>A spoken conversation is on: the microphone becomes a button that ends it, and typed text goes to <see cref="MicrophoneTextCommand"/>.</summary>
    public bool IsMicrophoneActive
    {
        get => (bool)GetValue(IsMicrophoneActiveProperty);
        set => SetValue(IsMicrophoneActiveProperty, value);
    }

    /// <summary>
    /// Where text typed during a spoken conversation goes, with the text as the parameter, such as a voice session's
    /// <c>SendTextAsync</c>. Unset, the field rests while the conversation is on.
    /// </summary>
    public ICommand? MicrophoneTextCommand
    {
        get => (ICommand?)GetValue(MicrophoneTextCommandProperty);
        set => SetValue(MicrophoneTextCommandProperty, value);
    }

    /// <summary>The conversation this composer writes to.</summary>
    public ChatView? Chat
    {
        get => (ChatView?)GetValue(ChatProperty);
        set => SetValue(ChatProperty, value);
    }

    /// <summary>The empty field's text; unset, "Message".</summary>
    public string? Placeholder
    {
        get => (string?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Chips above the field, shown while no answer is on its way. A tap sends the chip's text.</summary>
    public IEnumerable<string>? Suggestions
    {
        get => (IEnumerable<string>?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>Fonts and colours; unset, the app's <c>DefaultChatStyleOptions</c> or the theme.</summary>
    public ChatStyleOptions? StyleOptions
    {
        get => (ChatStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    private ChatStyleOptions Effective => ChatStyleOptions.Resolve(StyleOptions ?? Chat?.StyleOptions);

    private bool IsBusy => Chat?.IsBusy == true;

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (!AutoFocus) return;

        Dispatcher.DispatchDelayed(FocusDelay, async () =>
        {
            if (!_editor.IsLoaded || !_editor.Focus()) return;

            // Focus alone leaves Android's keyboard down.
            await _editor.ShowSoftInputAsync(CancellationToken.None);
        });
    }

    private void OnChatChanged(ChatView? previous, ChatView? next)
    {
        if (previous is not null) previous.PropertyChanged -= OnChatPropertyChanged;
        if (next is not null) next.PropertyChanged += OnChatPropertyChanged;
        UpdateSend();
        ShowSuggestions();
    }

    private void OnChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatView.IsBusy)) return;
        UpdateSend();
        ShowSuggestions();
    }

    private void Act()
    {
        if (Chat is not { } chat) return;

        if (chat.IsBusy)
        {
            chat.Stop();
            return;
        }

        var text = _editor.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (MicrophoneCommand is { } microphone && microphone.CanExecute(null))
                microphone.Execute(null);
            return;
        }

        _editor.Text = "";
        Say(text);
    }

    /// <summary>Typed text goes into the spoken conversation while one is on, and to the chat otherwise.</summary>
    private void Say(string text)
    {
        if (IsMicrophoneActive)
        {
            if (MicrophoneTextCommand is { } spoken && spoken.CanExecute(text))
                spoken.Execute(text);
            return;
        }

        _ = Chat?.SendAsync(text);
    }

    private void OnMicrophoneActiveChanged()
    {
        // Without somewhere to send it, typing rests while the conversation is spoken.
        var resting = IsMicrophoneActive && MicrophoneTextCommand is null;
        _editor.IsEnabled = !resting;
        _editor.Placeholder = resting ? ChatStrings.Get("Spine.Chat.Talking")
            : IsMicrophoneActive ? ChatStrings.Get("Spine.Chat.TalkOrType")
            : Placeholder ?? ChatStrings.Get("Spine.Chat.Placeholder");
        if (resting) _editor.Unfocus();
        UpdateSend();
        ShowSuggestions();
    }

    /// <summary>
    /// Centres one line in the field and keeps the insets even as it grows. On iOS, UIKit's text view has 8 points above
    /// and below its text, so one line sat 4 points high in the 44-point field, and MAUI's Done bar above the keyboard
    /// goes (a chat sends with its own button). On Android, the EditText's underline goes.
    /// </summary>
    private void FitPlatformEditor()
    {
#if IOS || MACCATALYST
        if (_editor.Handler?.PlatformView is not UIKit.UITextView text || text.Font is not { } font) return;

        var inset = (nfloat)Math.Max(0, (Effective.ControlHeight - font.LineHeight) / 2);
        text.TextContainerInset = new UIKit.UIEdgeInsets(inset, 0, inset, 0);

        if (text.InputAccessoryView is not null)
        {
            text.InputAccessoryView = null;
            text.ReloadInputViews();
        }
#elif ANDROID
        // The field draws its own background; the EditText's Material underline would show inside it. Its padding goes
        // with it: 8 dp above and below keeps more lines off the edges, and the grid centres one line in the field
        // (padding worked out from LineHeight overshot, since the font's own padding adds to the line).
        if (_editor.Handler?.PlatformView is not Android.Widget.EditText text) return;

        text.Background = null;
        var inset = (int)(text.Context!.Resources!.DisplayMetrics!.Density * 8);
        text.SetPadding(0, inset, 0, inset);
#endif
    }

    private void UpdateSend()
    {
        var busy = IsBusy;
        var empty = string.IsNullOrWhiteSpace(_editor.Text);
        var (glyph, label) = busy ? (StopGlyph, "Spine.Chat.Stop")
            : empty && IsMicrophoneActive ? (HangUpGlyph, "Spine.Chat.HangUp")
            : empty && MicrophoneCommand is not null ? (MicrophoneGlyph, "Spine.Chat.Microphone")
            : (SendGlyph, "Spine.Chat.Send");

        _glyph.Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(glyph)!;
        _glyph.Fill = busy ? Colors.White : Colors.Transparent;
        _send.Opacity = busy || !empty || glyph != SendGlyph ? 1 : 0.4;
        _send.BackgroundColor = glyph == HangUpGlyph ? Effective.ErrorColor : Effective.SendButtonColor;
        SemanticProperties.SetDescription(_send, ChatStrings.Get(label));
    }

    private void ShowSuggestions()
    {
        _chips.Clear();
        var style = Effective;

        foreach (var suggestion in Suggestions ?? [])
        {
            var chip = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = style.ChipBackground,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Padding = new Thickness(14, 7),
                Content = new Label { Text = suggestion, FontSize = style.FontSize * 0.85, TextColor = style.ChipTextColor, FontFamily = style.FontFamily },
            };
            Tap.SetCommand(chip, new Command(() => Say(suggestion)));
            _chips.Add(chip);
        }

        _chipsScroll.IsVisible = _chips.Count > 0 && !IsBusy && !(IsMicrophoneActive && MicrophoneTextCommand is null);
    }

    private void ApplyStyle()
    {
        var style = Effective;
        _editor.FontSize = style.FontSize;
        _editor.FontFamily = style.FontFamily;
        _editor.TextColor = style.ComposerTextColor;
        _editor.MaximumHeightRequest = style.FontSize * 1.3 * style.ComposerMaxLines + 16;
        _field.BackgroundColor = style.ComposerFieldBackground;
        _field.MinimumHeightRequest = style.ControlHeight;
        _field.StrokeShape = new RoundRectangle { CornerRadius = style.ControlHeight / 2 };
        _send.WidthRequest = _send.HeightRequest = style.ControlHeight;
        FitPlatformEditor();
        UpdateSend();
        ShowSuggestions();
    }
}
