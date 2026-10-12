using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.AI.Tests;

/// <summary>A model that answers from a script and remembers what it was asked.</summary>
internal sealed class FakeChatClient(string answer = "Hej då.") : IChatClient
{
    public UsageDetails? Usage { get; set; } = new() { InputTokenCount = 1000, OutputTokenCount = 200 };

    /// <summary>The stream's pieces: the answer split before each space.</summary>
    public string[] Pieces { get; set; } = [.. System.Text.RegularExpressions.Regex.Split(answer, "(?= )")];

    /// <summary>Thrown at once, before any update.</summary>
    public Exception? ThrowAtOnce { get; set; }

    /// <summary>Thrown after the first update.</summary>
    public Exception? ThrowAfterFirst { get; set; }

    /// <summary>When set, the stream waits on it after its first update.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public int Calls { get; private set; }
    public List<ChatMessage> LastMessages { get; private set; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastMessages = messages.ToList();
        if (ThrowAtOnce is { } e) throw e;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)) { Usage = Usage });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        LastMessages = messages.ToList();
        if (ThrowAtOnce is { } e) throw e;

        for (var i = 0; i < Pieces.Length; i++)
        {
            if (i == 1 && ThrowAfterFirst is { } after) throw after;
            yield return new ChatResponseUpdate(ChatRole.Assistant, Pieces[i]);
            if (i == 0 && Hold is { } hold) await hold.Task.WaitAsync(cancellationToken);
        }

        if (Usage is { } usage)
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(usage)] };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
