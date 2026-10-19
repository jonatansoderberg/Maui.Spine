using System.Text;
using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.Controls;

/// <summary>A function the model called while it answered, shown as a card through <c>ToolCallTemplate</c>.</summary>
/// <param name="CallId">The call's id, which pairs it with its result.</param>
/// <param name="Name">The function's name.</param>
/// <param name="Arguments">What it was called with.</param>
/// <param name="Result">What it returned, once it has.</param>
/// <param name="IsDone">Whether its result has arrived.</param>
public sealed record ChatToolCall(string CallId, string Name, IDictionary<string, object?>? Arguments, object? Result, bool IsDone);

/// <summary>Where an answer is.</summary>
public enum ChatAnswerState
{
    /// <summary>Sent; nothing has arrived yet.</summary>
    Thinking,

    /// <summary>Text or tool calls are arriving.</summary>
    Streaming,

    /// <summary>The answer is complete and in the conversation.</summary>
    Done,

    /// <summary>The user stopped it; what had arrived stays.</summary>
    Stopped,

    /// <summary>It failed; the row offers to try again.</summary>
    Failed,
}

/// <summary>
/// One answer as it streams in: the updates folded into the text so far and the tool calls with their results. No MAUI
/// types, so it is tested on its own.
/// </summary>
internal sealed class ChatAnswer
{
    private readonly List<ChatResponseUpdate> _updates = [];
    private readonly StringBuilder _text = new();
    private readonly List<ChatToolCall> _calls = [];
    private string? _textMessage;
    private bool _callSinceText;

    /// <summary>The answer's text so far, as markdown.</summary>
    public string Text => _text.ToString();

    /// <summary>The tool calls so far, in the order they were made.</summary>
    public IReadOnlyList<ChatToolCall> ToolCalls => _calls;

    /// <summary>Whether anything the user can see has arrived.</summary>
    public bool HasContent => _text.Length > 0 || _calls.Count > 0;

    /// <summary>Folds one update in.</summary>
    /// <returns>What changed: the text, the tool calls, or both.</returns>
    public (bool Text, bool Calls) Add(ChatResponseUpdate update)
    {
        _updates.Add(update);
        bool text = false, calls = false;

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } piece:
                    // A new message, or text after a tool call, is a new paragraph: a voice session's goodbye and the
                    // words after a function ran ran into the text before them ("Vi hörs!Ha det bra").
                    if (_text.Length > 0 && (_callSinceText || update.MessageId != _textMessage))
                        _text.Append("\n\n");
                    _text.Append(piece.Text);
                    _textMessage = update.MessageId;
                    _callSinceText = false;
                    text = true;
                    break;

                case FunctionCallContent call:
                    var index = _calls.FindIndex(c => c.CallId == call.CallId);
                    var made = new ChatToolCall(call.CallId, call.Name, call.Arguments, null, false);
                    if (index < 0) _calls.Add(made);
                    else _calls[index] = made with { Result = _calls[index].Result, IsDone = _calls[index].IsDone };
                    _callSinceText = true;
                    calls = true;
                    break;

                case FunctionResultContent result:
                    var at = _calls.FindIndex(c => c.CallId == result.CallId);
                    if (at < 0) _calls.Add(new ChatToolCall(result.CallId, "", null, result.Result, true));
                    else _calls[at] = _calls[at] with { Result = result.Result, IsDone = true };
                    calls = true;
                    break;
            }
        }

        return (text, calls);
    }

    /// <summary>The whole answer as Microsoft.Extensions.AI folds it, messages and usage included.</summary>
    public ChatResponse ToResponse() => _updates.ToChatResponse();
}
