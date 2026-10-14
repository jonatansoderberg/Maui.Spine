using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MauiSpineSampleApp.Pages.Chat;

/// <summary>
/// A model that needs no server: it looks the race up with a tool call, then writes a markdown answer a few words at a
/// time, the way a real model streams. Asked for a table, it answers with the splits.
/// </summary>
internal sealed class DemoChatClient : IChatClient
{
    private const string Summary = """
        You ran **D21** in *31:02* and came **fifth** of 38.

        - Controls 1–3 went well, all within ten seconds of the best
        - Control 4 cost about **two minutes**: the straight line through the marsh was slower than the path
        - The finish was the fastest in the class

        > Next time, take the path to control 4.
        """;

    private const string Table = """
        Here are your splits against the winner:

        | Control | You | Best | Lost |
        |:--|--:|--:|--:|
        | 1 | 2:41 | 2:38 | 0:03 |
        | 2 | 4:02 | 3:55 | 0:07 |
        | 4 | **6:15** | 4:02 | **2:13** |
        | Finish | 0:22 | 0:22 | 0:00 |

        ```csharp
        var lost = you - best;   // 2:13 at control 4
        ```
        """;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The demo only streams.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var question = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
        var answer = question.Contains("table", StringComparison.OrdinalIgnoreCase) || question.Contains("split", StringComparison.OrdinalIgnoreCase)
            ? Table
            : Summary;

        // A moment of thinking, then a tool call and its result, as a model with tools would.
        await Task.Delay(700, cancellationToken);
        yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new FunctionCallContent("call-1", "get_results", new Dictionary<string, object?> { ["class"] = "D21" })] };
        await Task.Delay(900, cancellationToken);
        yield return new ChatResponseUpdate { Role = ChatRole.Tool, Contents = [new FunctionResultContent("call-1", "5th of 38, 31:02")] };

        foreach (var word in answer.Split(' '))
        {
            await Task.Delay(35, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }

        yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(new UsageDetails { InputTokenCount = question.Length / 4, OutputTokenCount = answer.Length / 4 })] };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
