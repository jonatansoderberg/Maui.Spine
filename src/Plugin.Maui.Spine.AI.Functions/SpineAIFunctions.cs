using System.Collections;
using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.AI;

/// <summary>The functions one side offers the model, by name.</summary>
/// <remarks>
/// In the app, <c>UseSpineAI</c> and <c>UseSpineVoice</c> share one set, so a function added in either reaches text chat,
/// on-device models and voice sessions alike.
/// </remarks>
public class SpineAIFunctions : IReadOnlyCollection<SpineAIFunction>
{
    private readonly Lock _gate = new();
    private SpineAIFunction[] _functions = [];

    /// <summary>The app's own set, which <c>UseSpineAI</c> and <c>UseSpineVoice</c> both hand out.</summary>
    internal static SpineAIFunctions App { get; } = new();

    /// <summary>The name of <see cref="EndConversation"/>, the one the model calls.</summary>
    public const string EndConversationName = "end_conversation";

    /// <summary>What the model is told <see cref="EndConversation"/> is for, in every session kind.</summary>
    public const string EndConversationDescription = "Ends the conversation. Call it when the user says goodbye in any words, thanks you and is done, or otherwise wants to end the conversation; then say only a goodbye of two or three words in their language, without saying that you are ending it. Do not call it for a pause or a thank-you in the middle of a conversation.";

    /// <summary>The answer to <see cref="EndConversation"/> when the farewell is still to be said.</summary>
    public const string SayGoodbye = "The conversation ends after your goodbye. Now say only a goodbye of two or three words in the user's language, such as \"Hej då!\"; do not say that you are ending it.";

    /// <summary>The answer to <see cref="EndConversation"/> when the model already said goodbye in the same answer.</summary>
    public const string GoodbyeSaid = "The conversation ends once your goodbye has been said. Say nothing more.";

    /// <summary>
    /// The answer to <see cref="EndConversation"/> when the session cannot tell whether the farewell was said: GPT-Live's
    /// voice talks while its backend works, so what it said before the call may be a goodbye or only "one moment".
    /// </summary>
    public const string GoodbyeUnlessSaid = "The conversation ends after your goodbye. If you have not said goodbye yet, say only a goodbye of two or three words in the user's language; if you have, say nothing more.";

    /// <summary>
    /// Spine's built-in function that ends a conversation on the user's intent: offered by a voice session while
    /// <c>IVoiceSession.EndsOnGoodbye</c> is on, and by typed chat while <c>ChatView.EndsOnIntent</c> is. Each session
    /// runs it itself, as the app's own function; a Spine.AI server offers it to a feature's model whenever the app does,
    /// without a <c>Functions</c> entry, since it only ends the app's own session.
    /// </summary>
    public static SpineAIFunction EndConversation { get; } = SpineAIFunction.Create(EndConversationName, EndConversationDescription, () => SayGoodbye);

    /// <inheritdoc />
    public int Count => _functions.Length;

    /// <summary>The function named <paramref name="name"/>, or <see langword="null"/>.</summary>
    /// <param name="name">The name the model calls it by.</param>
    public SpineAIFunction? this[string name] => _functions.FirstOrDefault(f => f.Name == name);

    /// <summary>Adds <paramref name="function"/>, replacing one with the same name.</summary>
    /// <param name="function">The function.</param>
    /// <returns>The same set, for chaining.</returns>
    public SpineAIFunctions Add(SpineAIFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);

        lock (_gate)
            _functions = [.. _functions.Where(f => f.Name != function.Name), function];

        return this;
    }

    /// <summary>Adds a function from a method or a lambda. See <see cref="SpineAIFunction.Create"/>.</summary>
    /// <param name="name">The name the model calls it by.</param>
    /// <param name="description">What it does and when to call it, for the model.</param>
    /// <param name="method">The implementation.</param>
    /// <param name="prefer">The side that runs it when both offer it.</param>
    /// <returns>The same set, for chaining.</returns>
    public SpineAIFunctions Add(string name, string description, Delegate method, SpineAIFunctionSide prefer = SpineAIFunctionSide.Device) =>
        Add(SpineAIFunction.Create(name, description, method, prefer));

    /// <summary>Adds an existing <see cref="AIFunction"/>. See <see cref="SpineAIFunction.From"/>.</summary>
    /// <param name="function">The function.</param>
    /// <param name="prefer">The side that runs it when both offer it.</param>
    /// <returns>The same set, for chaining.</returns>
    public SpineAIFunctions Add(AIFunction function, SpineAIFunctionSide prefer = SpineAIFunctionSide.Device) =>
        Add(SpineAIFunction.From(function, prefer));

    /// <summary>Removes the function named <paramref name="name"/>.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether there was one.</returns>
    public bool Remove(string name)
    {
        lock (_gate)
        {
            var before = _functions.Length;
            _functions = [.. _functions.Where(f => f.Name != name)];
            return _functions.Length != before;
        }
    }

    /// <inheritdoc />
    public IEnumerator<SpineAIFunction> GetEnumerator() => ((IEnumerable<SpineAIFunction>)_functions).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Which side runs a function that both the app and the server offer.</summary>
public static class SpineAIFunctionOrder
{
    /// <summary>The <see cref="AdditionalPropertiesDictionary"/> key <see cref="Prefer(ChatOptions, string, SpineAIFunctionSide)"/> writes to.</summary>
    internal const string PreferKey = "spine.functions.prefer";

    /// <summary>
    /// Runs <paramref name="function"/> on <paramref name="side"/> in this chat, when both sides offer it, instead of the side
    /// the function itself prefers. Set it on <c>ChatView.ChatOptions</c> for one conversation.
    /// </summary>
    /// <param name="options">The chat's options.</param>
    /// <param name="function">The function's name.</param>
    /// <param name="side">The side to run it on.</param>
    /// <returns>The same options, for chaining.</returns>
    public static ChatOptions Prefer(this ChatOptions options, string function, SpineAIFunctionSide side)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(function);

        options.AdditionalProperties ??= [];
        var sides = options.AdditionalProperties.TryGetValue(PreferKey, out var existing) && existing is Dictionary<string, SpineAIFunctionSide> found
            ? found
            : new Dictionary<string, SpineAIFunctionSide>(StringComparer.Ordinal);

        sides[function] = side;
        options.AdditionalProperties[PreferKey] = sides;
        return options;
    }

    /// <summary>The sides a chat's options choose, by function name.</summary>
    /// <param name="options">The chat's options.</param>
    internal static IReadOnlyDictionary<string, SpineAIFunctionSide>? PreferredSides(ChatOptions? options) =>
        options?.AdditionalProperties?.TryGetValue(PreferKey, out var value) == true ? value as IReadOnlyDictionary<string, SpineAIFunctionSide> : null;

    /// <summary>The side that runs <paramref name="function"/>: the instance's choice, else the function's own.</summary>
    /// <param name="function">The function.</param>
    /// <param name="preferred">The instance's choices, by name.</param>
    internal static SpineAIFunctionSide SideOf(SpineAIFunction function, IReadOnlyDictionary<string, SpineAIFunctionSide>? preferred) =>
        preferred is not null && preferred.TryGetValue(function.Name, out var side) ? side : function.Prefer;
}
