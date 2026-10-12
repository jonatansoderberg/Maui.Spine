using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace MauiSpinePushNotificationsSampleApp.Server;

/// <summary>
/// A provider that needs no key: it answers with a canned paragraph, a word at a time, and reports made-up usage the
/// way a real adapter does. It lets the sample's AI features, budgets and report run on a laptop with nothing set up.
/// </summary>
internal sealed class DemoChatClient : IChatClient
{
    private const string Answer =
        "Det här svaret kommer från demo-leverantören. Byt Provider och Model för featuren i appsettings, " +
        "lägg nyckeln i user secrets, och samma anrop går till en riktig modell.";

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)) { Usage = UsageFor(messages), ModelId = options?.ModelId });

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var word in Answer.Split(' '))
        {
            await Task.Delay(60, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }

        // Real providers send usage last, in an update of its own.
        yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new UsageContent(UsageFor(messages))] };
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
