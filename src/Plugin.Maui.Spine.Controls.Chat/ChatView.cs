using System.Collections.Specialized;
using System.Windows.Input;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Controls.Shapes;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Plugin.Maui.Spine.Controls;

/// <summary>An answer that completed.</summary>
/// <param name="response">The answer as Microsoft.Extensions.AI folded it, usage included.</param>
public sealed class ChatAnsweredEventArgs(ChatResponse response) : EventArgs
{
    /// <summary>The answer as Microsoft.Extensions.AI folded it, usage included.</summary>
    public ChatResponse Response { get; } = response;
}

/// <summary>A piece of an answer as it streams in, for something that follows it (an avatar mouthing it, a caption).</summary>
/// <param name="delta">The text that arrived with this update; empty on the last one.</param>
/// <param name="text">The answer's text so far.</param>
/// <param name="isComplete">True once, at the end, however the answer ended: complete, stopped or failed.</param>
public sealed class ChatStreamedEventArgs(string delta, string text, bool isComplete) : EventArgs
{
    /// <summary>The text that arrived with this update; empty on the last one.</summary>
    public string Delta { get; } = delta;

    /// <summary>The answer's text so far.</summary>
    public string Text { get; } = text;

    /// <summary>True once, at the end, however the answer ended: complete, stopped or failed.</summary>
    public bool IsComplete { get; } = isComplete;
}

/// <summary>An answer that failed.</summary>
/// <param name="exception">What went wrong.</param>
public sealed class ChatFailedEventArgs(Exception exception) : EventArgs
{
    /// <summary>What went wrong.</summary>
    public Exception Exception { get; } = exception;
}

/// <summary>
/// A conversation with any <see cref="IChatClient"/>: the user's messages in bubbles, answers streamed in as markdown,
/// tool calls as cards, and the list following the end while the user is there. Pair it with a <see cref="ChatComposer"/>
/// in the page's footer.
/// </summary>
/// <remarks>
/// <see cref="Messages"/> is the conversation and belongs to the app. The view adds the user's message when it is sent and
/// the assistant's when the answer is complete (or stopped with text). A collection that raises change notifications is
/// watched, so the app can clear or load a conversation.
/// </remarks>
public class ChatView : ContentView
{
    public static readonly BindableProperty ClientProperty = BindableProperty.Create(
        nameof(Client), typeof(IChatClient), typeof(ChatView));

    public static readonly BindableProperty MessagesProperty = BindableProperty.Create(
        nameof(Messages), typeof(IList<ChatMessage>), typeof(ChatView), null,
        propertyChanged: (b, o, n) => ((ChatView)b).OnMessagesChanged(o as IList<ChatMessage>, n as IList<ChatMessage>));

    public static readonly BindableProperty ChatOptionsProperty = BindableProperty.Create(
        nameof(ChatOptions), typeof(ChatOptions), typeof(ChatView));

    public static readonly BindableProperty ThinkingTemplateProperty = BindableProperty.Create(
        nameof(ThinkingTemplate), typeof(DataTemplate), typeof(ChatView));

    public static readonly BindableProperty ToolCallTemplateProperty = BindableProperty.Create(
        nameof(ToolCallTemplate), typeof(DataTemplate), typeof(ChatView));

    public static readonly BindableProperty ShowToolCallsProperty = BindableProperty.Create(
        nameof(ShowToolCalls), typeof(bool), typeof(ChatView), false,
        propertyChanged: (b, _, _) => ((ChatView)b).Rebuild());

    public static readonly BindableProperty StyleOptionsProperty = BindableProperty.Create(
        nameof(StyleOptions), typeof(ChatStyleOptions), typeof(ChatView), null,
        propertyChanged: (b, _, _) => ((ChatView)b).Rebuild());

    private static readonly BindablePropertyKey IsBusyPropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsBusy), typeof(bool), typeof(ChatView), false);

    public static readonly BindableProperty IsBusyProperty = IsBusyPropertyKey.BindableProperty;

    private const double FollowSlack = 40;

    private readonly ScrollView _scroll;
    private readonly VerticalStackLayout _rows = new();
    private readonly Border _jump;
    private bool _follow = true;
    private double _lastScrollY;
    private bool _adding;
    // Where each drawn row's messages start in Messages; out of step with _rows once SendAsync draws rows itself.
    private readonly List<int> _rowStarts = [];
    private CancellationTokenSource? _running;

    public ChatView()
    {
        ChatStrings.EnsureRegistered();

        _scroll = new ScrollView { Content = _rows };
        _scroll.Scrolled += (_, e) => UpdateFollow(e.ScrollY);
        _rows.SizeChanged += (_, _) => { if (_follow) _ = ScrollToEndAsync(animated: false); };
        _scroll.SizeChanged += (_, _) => { if (_follow) _ = ScrollToEndAsync(animated: false); };

        _jump = JumpButton();

        var grid = new Grid();
        grid.Add(_scroll);
        grid.Add(_jump);
        Content = grid;

        SendCommand = new Command<string>(text => _ = SendAsync(text));
        StopCommand = new Command(Stop, () => IsBusy);
        SpineTheme.Track(this, Rebuild);
        Rebuild();
    }

    /// <summary>The model to talk to: a Spine.AI feature, any Microsoft.Extensions.AI client, or a fake.</summary>
    public IChatClient? Client
    {
        get => (IChatClient?)GetValue(ClientProperty);
        set => SetValue(ClientProperty, value);
    }

    /// <summary>The conversation, owned by the app. Unset, the view keeps its own.</summary>
    public IList<ChatMessage>? Messages
    {
        get => (IList<ChatMessage>?)GetValue(MessagesProperty);
        set => SetValue(MessagesProperty, value);
    }

    /// <summary>Options passed to every call.</summary>
    public ChatOptions? ChatOptions
    {
        get => (ChatOptions?)GetValue(ChatOptionsProperty);
        set => SetValue(ChatOptionsProperty, value);
    }

    /// <summary>What shows before the first word of an answer; unset, two lines under a shimmer.</summary>
    public DataTemplate? ThinkingTemplate
    {
        get => (DataTemplate?)GetValue(ThinkingTemplateProperty);
        set => SetValue(ThinkingTemplateProperty, value);
    }

    /// <summary>A tool call's card, with a <see cref="ChatToolCall"/> as its binding context; unset, a compact card.</summary>
    public DataTemplate? ToolCallTemplate
    {
        get => (DataTemplate?)GetValue(ToolCallTemplateProperty);
        set => SetValue(ToolCallTemplateProperty, value);
    }

    /// <summary>
    /// Shows a card for each function the model called, for debugging. Off by default: the model tells the user what a
    /// function did in its answer, and the calls go to the log.
    /// </summary>
    public bool ShowToolCalls
    {
        get => (bool)GetValue(ShowToolCallsProperty);
        set => SetValue(ShowToolCallsProperty, value);
    }

    /// <summary>Fonts, sizes and colours; unset, the app's <c>DefaultChatStyleOptions</c> or the theme.</summary>
    public ChatStyleOptions? StyleOptions
    {
        get => (ChatStyleOptions?)GetValue(StyleOptionsProperty);
        set => SetValue(StyleOptionsProperty, value);
    }

    /// <summary>Whether an answer is on its way.</summary>
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        private set
        {
            SetValue(IsBusyPropertyKey, value);
            ((Command)StopCommand).ChangeCanExecute();
        }
    }

    /// <summary>Sends its string parameter, as <see cref="SendAsync"/> does.</summary>
    public ICommand SendCommand { get; }

    /// <summary>Stops the answer on its way.</summary>
    public ICommand StopCommand { get; }

    /// <summary>Raised when the user's message has been added to the conversation.</summary>
    public event EventHandler<ChatMessage>? Sent;

    /// <summary>
    /// Raised as an answer's text streams in, piece by piece, and once more with <see cref="ChatStreamedEventArgs.IsComplete"/>
    /// when it ends. While <see cref="IsBusy"/> is true and no piece has come, the model is still thinking.
    /// </summary>
    public event EventHandler<ChatStreamedEventArgs>? Streamed;

    /// <summary>Raised when an answer is complete and in the conversation.</summary>
    public event EventHandler<ChatAnsweredEventArgs>? Answered;

    /// <summary>Raised when an answer failed; the row offers to try again.</summary>
    public event EventHandler<ChatFailedEventArgs>? Failed;

    private IList<ChatMessage> Conversation => Messages ??= [];

    private ChatStyleOptions Effective => ChatStyleOptions.Resolve(StyleOptions);

    /// <summary>Sends <paramref name="text"/> and streams the answer in. Does nothing while an answer is on its way.</summary>
    public async Task SendAsync(string? text, CancellationToken cancellationToken = default)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(text) || Client is null) return;

        var message = new ChatMessage(ChatRole.User, text.Trim());
        Add(message);
        _rows.Add(UserRow(message.Text));
        Haptics.Selection();
        Sent?.Invoke(this, message);

        await AnswerAsync(cancellationToken);
    }

    /// <summary>Stops the answer on its way. The text that had arrived stays.</summary>
    public void Stop() => _running?.Cancel();

    private async Task AnswerAsync(CancellationToken cancellationToken)
    {
        var client = Client;
        if (client is null) return;

        var row = new AnswerRow(this);
        _rows.Add(row);
        _follow = true;
        UpdateJump();
        IsBusy = true;

        using var running = _running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var answer = new ChatAnswer();

        try
        {
            var streamed = 0;
            await foreach (var update in client.GetStreamingResponseAsync([.. Conversation], ChatOptions, running.Token))
            {
                var (text, calls) = answer.Add(update);
                if (text || calls) row.Show(answer, ChatAnswerState.Streaming, calls);
                if (text && Streamed is { } handler)
                {
                    var all = answer.Text;
                    handler(this, new ChatStreamedEventArgs(all[streamed..], all, isComplete: false));
                    streamed = all.Length;
                }
            }

            var response = answer.ToResponse();
            foreach (var message in response.Messages) Add(message);
            row.Show(answer, ChatAnswerState.Done, calls: true);

            if (answer.Text.Length > 0)
                SemanticScreenReader.Announce(answer.Text);

            Answered?.Invoke(this, new ChatAnsweredEventArgs(response));
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            if (answer.Text.Length > 0) Add(new ChatMessage(ChatRole.Assistant, answer.Text));
            row.Show(answer, ChatAnswerState.Stopped, calls: true);
        }
        catch (Exception e)
        {
            row.ShowFailed(answer, e);
            Failed?.Invoke(this, new ChatFailedEventArgs(e));
        }
        finally
        {
            if (_running == running) _running = null;
            IsBusy = false;
            Streamed?.Invoke(this, new ChatStreamedEventArgs("", answer.Text, isComplete: true));
        }
    }

    private async Task RetryAsync(AnswerRow row)
    {
        if (IsBusy) return;
        _rows.Remove(row);
        await AnswerAsync(CancellationToken.None);
    }

    private void Add(ChatMessage message)
    {
        _adding = true;
        try
        {
            Conversation.Add(message);
        }
        finally
        {
            _adding = false;
        }
    }

    private void OnMessagesChanged(IList<ChatMessage>? previous, IList<ChatMessage>? next)
    {
        if (previous is INotifyCollectionChanged old) old.CollectionChanged -= OnConversationChanged;
        if (next is INotifyCollectionChanged watched) watched.CollectionChanged += OnConversationChanged;
        Rebuild();
    }

    private void OnConversationChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_adding || IsBusy) return;

        // A voice session replaces its answer's message ten times a second as the words come: only the row it is in, and
        // those after it, are drawn again. Drawing the whole conversation each time made the text flicker and the list jump.
        var at = e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace ? e.NewStartingIndex : -1;
        if (at < 0 || _rowStarts.Count != _rows.Count)
        {
            Rebuild();
            return;
        }

        var row = _rowStarts.Count - 1;
        while (row > 0 && _rowStarts[row] > at) row--;
        Draw(Math.Max(row, 0));
    }

    /// <summary>Draws the conversation again from <see cref="Messages"/>: on a new list, a change by the app, or a new theme.</summary>
    private void Rebuild()
    {
        if (IsBusy) return;

        var style = Effective;
        _rows.Clear();
        _rows.Spacing = style.MessageSpacing;
        _rows.Padding = style.Padding;
        _jump.BackgroundColor = style.JumpButtonBackground;
        _rowStarts.Clear();
        Draw(0);
    }

    /// <summary>Draws the rows from <paramref name="from"/> on, keeping a row whose view still fits its messages.</summary>
    private void Draw(int from)
    {
        var messages = Messages ?? [];
        var i = from < _rowStarts.Count ? _rowStarts[from] : 0;
        _rowStarts.RemoveRange(from, _rowStarts.Count - from);
        var row = from;

        while (i < messages.Count)
        {
            var message = messages[i];
            if (message.Role == ChatRole.User)
            {
                if (!(row < _rows.Count && _rows[row] is UserRowGrid shown && shown.Text == message.Text))
                    Place(row, UserRow(message.Text));
                _rowStarts.Add(i++);
                row++;
            }
            else if (message.Role == ChatRole.Assistant || message.Role == ChatRole.Tool)
            {
                // One answer up to the user's next message; a tool's results belong to the answer that called it.
                var start = i;
                var answer = new ChatAnswer();
                for (; i < messages.Count && messages[i].Role != ChatRole.User; i++)
                {
                    // Each message keeps its own id, so the answer starts a paragraph between two of them.
                    if (messages[i].Role == ChatRole.Assistant || messages[i].Role == ChatRole.Tool)
                        answer.Add(new ChatResponseUpdate
                        {
                            Role = messages[i].Role,
                            MessageId = messages[i].MessageId ?? i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Contents = [.. messages[i].Contents],
                        });
                }

                if (!(row < _rows.Count && _rows[row] is AnswerRow view))
                    Place(row, view = new AnswerRow(this));
                view.Show(answer, ChatAnswerState.Done, calls: true);
                _rowStarts.Add(start);
                row++;
            }
            else
            {
                i++;
            }
        }

        while (_rows.Count > row)
            _rows.RemoveAt(_rows.Count - 1);
    }

    private void Place(int row, View view)
    {
        if (row < _rows.Count)
            _rows.RemoveAt(row);
        _rows.Insert(row, view);
    }

    private View UserRow(string text)
    {
        var style = Effective;
        var bubble = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = style.UserBubbleColor,
            StrokeShape = new RoundRectangle { CornerRadius = style.BubbleCornerRadius },
            Padding = new Thickness(14, 9),
            HorizontalOptions = LayoutOptions.End,
            Content = new Label { Text = text, TextColor = style.UserTextColor, FontSize = style.FontSize, FontFamily = style.FontFamily },
        };

        SemanticProperties.SetDescription(bubble, ChatStrings.Get("Spine.Chat.You", text));
        ContextMenu.SetItems(bubble, CopyMenu(() => text));

        var row = new UserRowGrid(text) { ColumnDefinitions = [new ColumnDefinition(new GridLength(1 - style.BubbleMaxWidth, GridUnitType.Star)), new ColumnDefinition(new GridLength(style.BubbleMaxWidth, GridUnitType.Star))] };
        row.Add(bubble, 1);
        return row;
    }

    private static MenuItems CopyMenu(Func<string> text) =>
        [new MenuAction(ChatStrings.Get("Spine.Chat.Copy"), null, new Command(() => _ = Clipboard.Default.SetTextAsync(text())))];

    private Border JumpButton()
    {
        var chevron = new Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString("M 4,7 L 10,13 L 16,7")!,
            StrokeThickness = 2.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 20,
            HeightRequest = 20,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        chevron.SetAppThemeColor(Shape.StrokeProperty, Colors.Black, Colors.White);

        var jump = new Border
        {
            WidthRequest = 40,
            HeightRequest = 40,
            StrokeThickness = 0,
            StrokeShape = new Ellipse(),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 12),
            Shadow = new Shadow { Opacity = 0.18f, Radius = 8, Offset = new Point(0, 2) },
            Content = chevron,
            IsVisible = false,
        };

        SemanticProperties.SetDescription(jump, ChatStrings.Get("Spine.Chat.JumpToLatest"));
        Tap.SetCommand(jump, new Command(() =>
        {
            _follow = true;
            UpdateJump();
            _ = ScrollToEndAsync(animated: true);
        }));
        return jump;
    }

    private void UpdateFollow(double scrollY)
    {
        // The view's own scrolls to the end only ever go down, and they can report in after the content has grown again,
        // so being short of the bottom is not enough to stop following: only a move up, which is the user's, stops it.
        if (scrollY >= _scroll.ContentSize.Height - _scroll.Height - FollowSlack) _follow = true;
        else if (scrollY < _lastScrollY - 1) _follow = false;

        _lastScrollY = scrollY;
        UpdateJump();
    }

    private void UpdateJump() => _jump.IsVisible = !_follow && _rows.Count > 0;

    /// <summary>
    /// Scrolls to the end once the layout pass is over: when the rows or the view change size (the suggestions in the
    /// composer come and go), the content size is only final after it.
    /// </summary>
    private Task ScrollToEndAsync(bool animated)
    {
        var done = new TaskCompletionSource();
        Dispatcher.Dispatch(async () =>
        {
            if (_scroll.Height > 0)
                await _scroll.ScrollToAsync(0, Math.Max(0, _scroll.ContentSize.Height - _scroll.Height), animated);
            done.TrySetResult();
        });
        return done.Task;
    }

    private sealed class UserRowGrid(string text) : Grid
    {
        public string Text { get; } = text;
    }

    /// <summary>One answer: its tool cards, the thinking state, the markdown, and how it ended.</summary>
    private sealed class AnswerRow : VerticalStackLayout
    {
        private readonly ChatView _chat;
        private readonly VerticalStackLayout _tools = new() { Spacing = 6 };
        private readonly MarkdownView _text = new() { IsVisible = false };
        private readonly Label _status;
        private View? _thinking;
        private ChatAnswer _shown;

        public AnswerRow(ChatView chat)
        {
            _chat = chat;
            _shown = new ChatAnswer();
            var style = chat.Effective;
            Spacing = 8;

            _status = new Label { FontSize = style.FontSize * 0.8, TextColor = style.SecondaryTextColor, IsVisible = false };
            _thinking = Thinking(style);

            Add(_tools);
            Add(_thinking);
            Add(_text);
            Add(_status);

            // The answer last shown, streamed or drawn from the conversation.
            ContextMenu.SetItems(_text, CopyMenu(() => _shown.Text));
        }

        public void Show(ChatAnswer answer, ChatAnswerState state, bool calls)
        {
            _shown = answer;
            if ((answer.Text.Length > 0 || _chat.ShowToolCalls && answer.ToolCalls.Count > 0) && _thinking is not null)
            {
                Remove(_thinking);
                _thinking = null;
            }

            if (!_chat.ShowToolCalls) _tools.Clear();
            else if (calls) ShowTools(answer.ToolCalls);

            _text.IsVisible = answer.Text.Length > 0;
            _text.Text = answer.Text;
            SemanticProperties.SetDescription(this, state is ChatAnswerState.Done or ChatAnswerState.Stopped
                ? ChatStrings.Get("Spine.Chat.Assistant", answer.Text)
                : null);

            _status.IsVisible = state == ChatAnswerState.Stopped;
            _status.Text = ChatStrings.Get("Spine.Chat.Stopped");
        }

        public void ShowFailed(ChatAnswer answer, Exception exception)
        {
            Show(answer, ChatAnswerState.Failed, calls: true);
            if (_thinking is not null)
            {
                Remove(_thinking);
                _thinking = null;
            }

            var style = _chat.Effective;
            var message = exception switch
            {
                AIRateLimitedException limited => ChatStrings.Get("Spine.Chat.RateLimited", Math.Ceiling((limited.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds)),
                IEmptyResultException => ChatStrings.Get("Spine.Chat.Off"),
                _ => ChatStrings.Get("Spine.Chat.Failed"),
            };

            var retry = new Button
            {
                Text = ChatStrings.Get("Spine.Chat.TryAgain"),
                FontSize = style.FontSize * 0.85,
                HorizontalOptions = LayoutOptions.Start,
                Padding = new Thickness(14, 6),
                MinimumHeightRequest = 0,
            };
            retry.Clicked += (_, _) => _ = _chat.RetryAsync(this);

            Add(new Label { Text = message, TextColor = style.ErrorColor, FontSize = style.FontSize * 0.9 });
            Add(retry);
            SemanticScreenReader.Announce(message);
        }

        private void ShowTools(IReadOnlyList<ChatToolCall> calls)
        {
            _tools.Clear();
            var style = _chat.Effective;

            foreach (var call in calls)
            {
                if (_chat.ToolCallTemplate?.CreateContent() is View custom)
                {
                    custom.BindingContext = call;
                    _tools.Add(custom);
                    continue;
                }

                var card = new Border
                {
                    StrokeThickness = 0,
                    BackgroundColor = style.ToolCardBackground,
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Padding = new Thickness(12, 8),
                    HorizontalOptions = LayoutOptions.Start,
                    Content = new Label
                    {
                        FontSize = style.FontSize * 0.8,
                        TextColor = style.SecondaryTextColor,
                        Text = $"{(call.IsDone ? "✓" : "⋯")}  {(call.Name.Length > 0 ? call.Name : "tool")} · {ChatStrings.Get(call.IsDone ? "Spine.Chat.ToolDone" : "Spine.Chat.ToolRunning")}",
                    },
                };
                _tools.Add(card);
            }
        }

        private View Thinking(ChatStyleOptions style)
        {
            if (_chat.ThinkingTemplate?.CreateContent() is View custom)
                return custom;

            var lines = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Border { StrokeThickness = 0, HeightRequest = 14, WidthRequest = 220, HorizontalOptions = LayoutOptions.Start, StrokeShape = new RoundRectangle { CornerRadius = 7 } },
                    new Border { StrokeThickness = 0, HeightRequest = 14, WidthRequest = 150, HorizontalOptions = LayoutOptions.Start, StrokeShape = new RoundRectangle { CornerRadius = 7 } },
                },
            };

            var shimmer = new Shimmer { IsLoading = true, SkeletonContent = lines };
            SemanticProperties.SetDescription(shimmer, ChatStrings.Get("Spine.Chat.Thinking"));
            return shimmer;
        }
    }
}
