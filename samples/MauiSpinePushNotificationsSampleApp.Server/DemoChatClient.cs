using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace MauiSpinePushNotificationsSampleApp.Server;

/// <summary>
/// A provider that needs no key: it answers with a canned paragraph, a word at a time, and reports made-up usage the
/// way a real adapter does. It lets the sample's AI features, budgets and report run on a laptop with nothing set up.
/// </summary>
/// <remarks>
/// It calls the functions it is given as a real model would, when the user's last message asks for one ("bokning",
/// "klockan"), and then answers with what the function returned.
/// </remarks>
internal sealed class DemoChatClient : IChatClient
{
    private const string Answer =
        "Det här svaret kommer från demo-leverantören. Byt Provider och Model för featuren i appsettings, " +
        "lägg nyckeln i user secrets, och samma anrop går till en riktig modell.";

    private static readonly (string Function, string[] Words)[] Asks =
    [
        ("get_my_bookings", ["bokning", "bokat", "booking", "reservation"]),
        ("show_time", ["klocka", "klockan", "tid är", "time"]),
    ];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var contents = Next(list, options) is { } call ? [call] : (List<AIContent>)[new TextContent(TextFor(list))];
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)) { Usage = UsageFor(list), ModelId = options?.ModelId });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();

        if (Next(list, options) is { } call)
        {
            await Task.Delay(200, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [call]);
        }
        else
        {
            foreach (var word in TextFor(list).Split(' '))
            {
                await Task.Delay(60, cancellationToken);
                yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
            }
        }

        // Real providers send usage last, in an update of its own.
        yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(UsageFor(list))] };
    }

    /// <summary>A call to a function the user asked for, when it is offered and the user spoke last.</summary>
    private static FunctionCallContent? Next(List<ChatMessage> messages, ChatOptions? options)
    {
        if (messages.Count == 0 || messages[^1].Role != ChatRole.User || options?.Tools is not { Count: > 0 } tools) return null;

        var text = messages[^1].Text.ToLowerInvariant();
        foreach (var (function, words) in Asks)
        {
            if (words.Any(text.Contains) && tools.Any(t => t.Name == function))
                return new FunctionCallContent($"demo_{Guid.NewGuid():N}", function, new Dictionary<string, object?>());
        }

        return null;
    }

    /// <summary>What a function returned, read back as a model would; otherwise the canned paragraph.</summary>
    private static string TextFor(List<ChatMessage> messages)
    {
        var lastUser = messages.FindLastIndex(m => m.Role == ChatRole.User);
        var results = messages.Skip(lastUser + 1).SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();
        if (results.Count == 0) return Answer;

        var read = results.Select(r => r.Result switch
        {
            JsonElement element => element.GetRawText(),
            null => "inget",
            var other => other.ToString(),
        });
        return "Demo-leverantören fick svaret från funktionen: " + string.Join(" · ", read);
    }

    private static UsageDetails UsageFor(IEnumerable<ChatMessage> messages)
    {
        var input = messages.Sum(m => m.Text.Length) / 4 + 10;
        var output = Answer.Length / 4;
        return new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
