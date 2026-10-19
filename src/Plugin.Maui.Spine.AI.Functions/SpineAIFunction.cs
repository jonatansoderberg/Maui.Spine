using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.Spine.AI;

/// <summary>Where a function runs.</summary>
public enum SpineAIFunctionSide
{
    /// <summary>In the app, with the device's own state: the time, the location, the current page.</summary>
    Device,

    /// <summary>On the app's Spine.Server.AI backend, with the signed-in user's data.</summary>
    Server,
}

/// <summary>
/// A function the model may call, declared once: its name, what it does, its parameters, and the side it prefers when both
/// the app and the server offer it.
/// </summary>
/// <remarks>
/// <para>Parameters of type <see cref="SpineAIFunctionContext"/>, <see cref="CancellationToken"/> and
/// <see cref="IServiceProvider"/> are filled from the call and left out of the schema, so the model never sees them.</para>
/// <para>A function shared by the app and the server lives in a project both reference. The app registers it with
/// <c>UseSpineAI(o =&gt; o.Functions.Add(...))</c>, the server with <c>AddSpineAI(..., ai =&gt; ai.Functions.Add(...))</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// public static SpineAIFunction GetMyBookings { get; } = SpineAIFunction.Create(
///     "get_my_bookings", "Lists the signed-in user's bookings.", GetMyBookingsAsync, SpineAIFunctionSide.Server);
///
/// static async Task&lt;object&gt; GetMyBookingsAsync(SpineAIFunctionContext context, int? days)
/// {
///     if (await context.GetUserAsync() is not { } user) return SpineAIFunctionResult.SignInRequired();
///     …
/// }
/// </code>
/// </example>
public sealed class SpineAIFunction
{
    /// <summary>The key a call's <see cref="AIFunctionArguments.Context"/> carries the <see cref="SpineAIFunctionContext"/> under.</summary>
    internal static readonly Type ContextKey = typeof(SpineAIFunctionContext);

    // Microsoft.Extensions.AI's options indent, which the model pays for in tokens.
    private static readonly JsonSerializerOptions Compact = CreateCompact();

    /// <summary>The longest result, in characters of JSON, that reaches the model. A longer one is an error the model can explain.</summary>
    public const int DefaultMaxResultCharacters = 16_000;

    private SpineAIFunction(AIFunction function, SpineAIFunctionSide prefer)
    {
        Function = function;
        Prefer = prefer;
    }

    /// <summary>The name the model calls it by, such as <c>get_my_bookings</c>.</summary>
    public string Name => Function.Name;

    /// <summary>What it does, for the model.</summary>
    public string Description => Function.Description;

    /// <summary>The parameters' JSON schema, as the model sees it.</summary>
    public JsonElement JsonSchema => Function.JsonSchema;

    /// <summary>
    /// The side that runs it when the app and the server both offer it. A voice session or a chat can choose the other side
    /// with <c>Prefer(name, side)</c>.
    /// </summary>
    public SpineAIFunctionSide Prefer { get; }

    /// <summary>The Microsoft.Extensions.AI function underneath, with the context parameters bound.</summary>
    internal AIFunction Function { get; }

    /// <summary>Declares a function from a method or a lambda.</summary>
    /// <param name="name">The name the model calls it by: letters, digits and underscores.</param>
    /// <param name="description">What it does and when to call it, for the model.</param>
    /// <param name="method">
    /// The implementation. Its other parameters become the schema; a <see cref="SpineAIFunctionContext"/>,
    /// <see cref="CancellationToken"/> or <see cref="IServiceProvider"/> parameter is filled from the call.
    /// </param>
    /// <param name="prefer">The side that runs it when both offer it.</param>
    public static SpineAIFunction Create(string name, string description, Delegate method, SpineAIFunctionSide prefer = SpineAIFunctionSide.Device)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(method);

        return new(AIFunctionFactory.Create(method, new AIFunctionFactoryOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = Compact,
            ConfigureParameterBinding = BindContext,
        }), prefer);
    }

    /// <summary>Declares a function from an existing <see cref="AIFunction"/>, such as one made with <see cref="AIFunctionFactory"/>.</summary>
    /// <param name="function">The function. It runs with the call's services; it sees the context only through <see cref="AIFunctionArguments.Context"/>.</param>
    /// <param name="prefer">The side that runs it when both offer it.</param>
    public static SpineAIFunction From(AIFunction function, SpineAIFunctionSide prefer = SpineAIFunctionSide.Device)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new(function, prefer);
    }

    /// <summary>The function for one conversation: every call runs with <paramref name="context"/>, is logged, and answers the model even when it fails.</summary>
    /// <param name="context">Who the calls are for, where they run, and the services.</param>
    public AIFunction Bind(SpineAIFunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new BoundFunction(this, context);
    }

    /// <summary>The function's declaration only, for a model that should call it on the other side.</summary>
    public AIFunctionDeclaration Declaration() =>
        AIFunctionFactory.CreateDeclaration(Function.Name, Function.Description, Function.JsonSchema);

    /// <summary>Runs the function once, as <see cref="Bind"/> would: arguments checked against the schema, the call logged, a failure answered as an error.</summary>
    /// <param name="arguments">The model's arguments.</param>
    /// <param name="context">Who the call is for.</param>
    /// <returns>What the model reads: the function's result, or <c>{ "error": …, "message": … }</c>.</returns>
    public async Task<object?> InvokeAsync(IDictionary<string, object?>? arguments, SpineAIFunctionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await Bind(context).InvokeAsync(new AIFunctionArguments(arguments ?? new Dictionary<string, object?>()), context.CancellationToken);
    }

    /// <summary>The parameters the schema names, or <see langword="null"/> when it names none to check against; and which are required.</summary>
    internal (HashSet<string>? Known, List<string> Required) Parameters()
    {
        HashSet<string>? known = null;
        var required = new List<string>();

        if (JsonSchema.ValueKind != JsonValueKind.Object) return (known, required);

        if (JsonSchema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            known = new(StringComparer.Ordinal);
            foreach (var property in properties.EnumerateObject()) known.Add(property.Name);
        }

        if (JsonSchema.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array)
            foreach (var item in names.EnumerateArray())
                if (item.GetString() is { } named) required.Add(named);

        return (known, required);
    }

    private static AIFunctionFactoryOptions.ParameterBindingOptions BindContext(System.Reflection.ParameterInfo parameter) =>
        parameter.ParameterType == typeof(SpineAIFunctionContext)
            ? new()
            {
                ExcludeFromSchema = true,
                BindParameter = (_, arguments) =>
                    arguments.Context?.TryGetValue(ContextKey, out var context) == true && context is SpineAIFunctionContext found
                        ? found
                        : throw new InvalidOperationException(
                            $"The function '{parameter.Member.Name}' takes a SpineAIFunctionContext, but it was called without one. Call it through Bind or InvokeAsync."),
            }
            : default;

    private static JsonSerializerOptions CreateCompact()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions) { WriteIndented = false };
        options.MakeReadOnly();
        return options;
    }

    /// <inheritdoc />
    public override string ToString() => Name;

    private sealed class BoundFunction(SpineAIFunction declared, SpineAIFunctionContext context) : DelegatingAIFunction(declared.Function)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            var logger = context.Services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.AI.Functions") ?? NullLogger.Instance;

            if (Check(arguments) is { } invalid)
            {
                logger.LogWarning("AI function '{Function}' on the {Side} for {Caller}: {Problem}", Name, context.RunsOn, context.Caller.Key, invalid);
                return SpineAIFunctionResult.Error(invalid, "invalidArguments");
            }

            var call = new AIFunctionArguments(arguments) { Services = arguments.Services ?? context.Services };
            call.Context = arguments.Context is { } carried ? new Dictionary<object, object?>(carried) : [];
            call.Context[ContextKey] = context;

            var started = Stopwatch.GetTimestamp();
            object? result;

            try
            {
                result = await base.InvokeCoreAsync(call, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "AI function '{Function}' failed on the {Side} for {Caller} after {Elapsed:0} ms.",
                    Name, context.RunsOn, context.Caller.Key, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

                // The model hears that it failed, so it can say so, rather than waiting for an answer that never comes.
                return e is ArgumentException or JsonException or FormatException
                    ? SpineAIFunctionResult.Error($"The arguments to '{Name}' are not valid: {e.Message}", "invalidArguments")
                    : SpineAIFunctionResult.Error($"The function '{Name}' failed.");
            }

            var characters = Length(result);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (characters > context.MaxResultCharacters)
            {
                logger.LogWarning("AI function '{Function}' on the {Side} for {Caller} answered {Characters} characters, more than {Max}; the model is told it was too large.",
                    Name, context.RunsOn, context.Caller.Key, characters, context.MaxResultCharacters);
                return SpineAIFunctionResult.Error($"The result of '{Name}' was too large to read.", "tooLarge");
            }

            logger.LogInformation("AI function '{Function}' ran on the {Side} for {Caller} in {Elapsed:0} ms ({Characters} characters).",
                Name, context.RunsOn, context.Caller.Key, elapsed, characters);

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("AI function '{Function}' arguments {Arguments} answered {Result}.", Name, Text(arguments), Text(result));

            return result;
        }

        private string? Check(AIFunctionArguments arguments)
        {
            var (known, required) = declared.Parameters();

            if (known is not null && arguments.Keys.Where(k => !known.Contains(k)).ToList() is { Count: > 0 } unknown) return Unknown(unknown);

            var missing = required.Where(r => !arguments.ContainsKey(r)).ToList();
            return missing.Count > 0 ? $"'{Name}' needs {string.Join(", ", missing)}." : null;
        }

        private string Unknown(IEnumerable<string> names) => $"'{Name}' has no parameter {string.Join(", ", names)}.";

        private static int Length(object? result) => result switch
        {
            null => 0,
            string text => text.Length,
            JsonElement element => element.GetRawText().Length,
            JsonNode node => node.ToJsonString().Length,
            _ => Text(result).Length,
        };

        private static string Text(object? value)
        {
            try
            {
                return value switch
                {
                    null => "null",
                    string text => text,
                    _ => JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions.GetTypeInfo(value.GetType())),
                };
            }
            catch (Exception e) when (e is NotSupportedException or InvalidOperationException)
            {
                return value?.ToString() ?? "null";
            }
        }
    }
}

/// <summary>Answers a function gives the model when it cannot do what was asked.</summary>
public static class SpineAIFunctionResult
{
    /// <summary>The <c>error</c> value of <see cref="SignInRequired"/>.</summary>
    public const string SignInRequiredError = "signInRequired";

    /// <summary>The user has to sign in first. The model reads it and tells the user so.</summary>
    /// <param name="message">What the model is told; unset, a plain sentence that it needs sign-in.</param>
    public static JsonElement SignInRequired(string? message = null) =>
        Error(message ?? "The user is not signed in, so this cannot be looked up. Tell the user to sign in to the app first.", SignInRequiredError);

    /// <summary>The function could not do it.</summary>
    /// <param name="message">What the model is told.</param>
    /// <param name="error">A short code the model and the log see, such as <c>notFound</c>.</param>
    public static JsonElement Error(string message, string error = "failed")
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        using var json = JsonDocument.Parse(new JsonObject { ["error"] = error, ["message"] = message }.ToJsonString());
        return json.RootElement.Clone();
    }

    /// <summary>Whether <paramref name="result"/> is an error made here, and which.</summary>
    /// <param name="result">A function's result.</param>
    internal static string? ErrorOf(object? result) =>
        result is JsonElement { ValueKind: JsonValueKind.Object } element && element.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : null;
}
