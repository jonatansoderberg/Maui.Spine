using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.Svg;

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
        propertyChanged: (b, _, _) => ((ChatComposer)b).ApplyPlaceholder());

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
        propertyChanged: (b, o, n) => ((ChatComposer)b).OnCommandChanged(o as ICommand, n as ICommand));

    public static readonly BindableProperty IsMicrophoneActiveProperty = BindableProperty.Create(
        nameof(IsMicrophoneActive), typeof(bool), typeof(ChatComposer), false,
        propertyChanged: (b, _, _) => ((ChatComposer)b).OnMicrophoneActiveChanged());

    public static readonly BindableProperty MicrophoneTextCommandProperty = BindableProperty.Create(
        nameof(MicrophoneTextCommand), typeof(ICommand), typeof(ChatComposer), null,
        propertyChanged: (b, o, n) =>
        {
            var composer = (ChatComposer)b;
            composer.OnCommandChanged(o as ICommand, n as ICommand);
            composer.OnMicrophoneActiveChanged();
        });

    // Long enough for a page's push to finish, so the keyboard does not rise during the slide.
    private static readonly TimeSpan FocusDelay = TimeSpan.FromMilliseconds(400);
    private bool _focusedOnce;

    // The composer's glyphs, in Spine's icon style, embedded in this package.
    private const string SendIcon = "spinechatsend.svg";
    private const string StopIcon = "spinechatstop.svg";
    private const string HangUpIcon = "spinechathangup.svg";
    private const string VoiceIcon = "spinechatvoice.svg";

    // The package's own SVGs, added to Spine.Svg's names the first time a composer draws one.
    private static readonly Lazy<ResourceNameCache> Icons = new(() =>
    {
        var cache = IPlatformApplication.Current?.Services.GetService<ResourceNameCache>() ?? new ResourceNameCache();
        cache.Initialize([typeof(ChatComposer).Assembly]);
        return cache;
    });

    private readonly Editor _editor;
    private readonly Border _field;
    private readonly ImageButton _send;
    private readonly ImageButton _voice;
    private readonly HorizontalStackLayout _chips = new() { Spacing = 8 };
    private (string Icon, Color Tint, double Size)? _sendShown, _voiceShown;
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

        // As tall as the field on one line, and at its foot when it grows, the way Messages keeps its send button. Liquid
        // Glass where the system has it (iOS and Mac Catalyst 26); a round button elsewhere.
        _send = new ImageButton { VerticalOptions = LayoutOptions.End };
        Glass.SetStyle(_send, GlassStyle.Prominent);
        _send.Clicked += (_, _) => SendOrStop();

        // The spoken conversation: the voice button starts one, and is the hang-up while it is on.
        _voice = new ImageButton { VerticalOptions = LayoutOptions.End, IsVisible = false };
        _voice.Clicked += (_, _) =>
        {
            if (MicrophoneCommand is { } microphone && microphone.CanExecute(null))
                microphone.Execute(null);
        };

        _chipsScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _chips,
            IsVisible = false,
        };

        var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)], ColumnSpacing = 8 };
        row.Add(_field, 0);
        row.Add(_send, 1);
        row.Add(_voice, 2);

        Content = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12, 8), Children = { _chipsScroll, row } };

        Core.SpineTheme.Track(this, ApplyStyle);
        ApplyStyle();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Puts the cursor in the field and shows the on-screen keyboard the first time the composer appears. On by default:
    /// on a chat page, writing is what comes next.
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
        // Once: the composer can be loaded again while the page stays (the keyboard came up on its own when a call ended).
        if (!AutoFocus || _focusedOnce) return;
        _focusedOnce = true;

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

    private void SendOrStop()
    {
        if (Chat is { IsBusy: true } chat)
        {
            chat.Stop();
            return;
        }

        // Cleared only once something took it: text that could not go anywhere stays to be sent again.
        if (_editor.Text is { } text && !string.IsNullOrWhiteSpace(text) && Say(text))
            _editor.Text = "";
    }

    /// <summary>Typed text goes into the spoken conversation while one is on, and to the chat otherwise.</summary>
    /// <returns>Whether the text was taken.</returns>
    private bool Say(string text)
    {
        if (IsMicrophoneActive)
        {
            if (MicrophoneTextCommand is not { } spoken || !spoken.CanExecute(text)) return false;
            spoken.Execute(text);
            return true;
        }

        if (Chat is not { } chat) return false;
        _ = chat.SendAsync(text);
        return true;
    }

    private void OnCommandChanged(ICommand? previous, ICommand? next)
    {
        if (previous is not null) previous.CanExecuteChanged -= OnCanExecuteChanged;
        if (next is not null) next.CanExecuteChanged += OnCanExecuteChanged;
        UpdateSend();
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e) => UpdateSend();

    private void OnMicrophoneActiveChanged()
    {
        // Without somewhere to send it, typing rests while the conversation is spoken.
        var resting = IsMicrophoneActive && MicrophoneTextCommand is null;
        _editor.IsEnabled = !resting;
        ApplyPlaceholder();
        if (resting) _editor.Unfocus();
        UpdateSend();
        ShowSuggestions();
    }

    private void ApplyPlaceholder() =>
        _editor.Placeholder = IsMicrophoneActive && MicrophoneTextCommand is null ? ChatStrings.Get("Spine.Chat.Talking")
            : IsMicrophoneActive ? ChatStrings.Get("Spine.Chat.TalkOrType")
            : Placeholder ?? ChatStrings.Get("Spine.Chat.Placeholder");

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
        var style = Effective;
        var busy = IsBusy;
        var empty = string.IsNullOrWhiteSpace(_editor.Text);

        // Send is always there, dimmed until there is something to send, and is Stop while an answer streams.
        var canSend = busy || !empty && (IsMicrophoneActive ? MicrophoneTextCommand?.CanExecute(_editor.Text) == true : Chat is not null);
        _send.BackgroundColor = style.SendButtonColor;
        _send.Opacity = canSend ? 1 : 0.4;
        Draw(_send, ref _sendShown, busy ? StopIcon : SendIcon, Colors.White);
        SemanticProperties.SetDescription(_send, ChatStrings.Get(busy ? "Spine.Chat.Stop" : "Spine.Chat.Send"));

        // The voice button is there whenever the app offers a spoken conversation; red while one is on.
        _voice.IsVisible = MicrophoneCommand is not null;
        var talking = IsMicrophoneActive;
        Glass.SetStyle(_voice, talking ? GlassStyle.Prominent : GlassStyle.Regular);
        _voice.BackgroundColor = talking ? style.ErrorColor : style.ComposerFieldBackground;
        _voice.Opacity = MicrophoneCommand?.CanExecute(null) == true ? 1 : 0.4;
        Draw(_voice, ref _voiceShown, talking ? HangUpIcon : VoiceIcon, talking ? Colors.White : style.ComposerTextColor ?? Colors.Black);
        SemanticProperties.SetDescription(_voice, ChatStrings.Get(talking ? "Spine.Chat.HangUp" : "Spine.Chat.Microphone"));
    }

    /// <summary>Renders <paramref name="icon"/> on <paramref name="button"/>, once per icon, tint and size.</summary>
    private void Draw(ImageButton button, ref (string Icon, Color Tint, double Size)? shown, string icon, Color tint)
    {
        var size = Effective.ControlHeight;
        if (shown is { } last && last.Icon == icon && last.Tint.Equals(tint) && last.Size == size) return;
        shown = (icon, tint, size);

        if (Icons.Value.Resolve(icon) is not { } resource) return;
        // The glyph is half the button, with line ends rounded and lines a little heavier, as the header bar's icons.
        button.Source = SvgBitmapLoader.LoadFromEmbedded(resource, size, size, tint, new Thickness(size / 4), lineWidthScale: 1.4f, roundLineEnds: true);
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
        _send.WidthRequest = _send.HeightRequest = _voice.WidthRequest = _voice.HeightRequest = style.ControlHeight;
        _send.CornerRadius = _voice.CornerRadius = (int)(style.ControlHeight / 2);
        _send.Padding = _voice.Padding = new Thickness(0);
        FitPlatformEditor();
        UpdateSend();
        ShowSuggestions();
    }
}
