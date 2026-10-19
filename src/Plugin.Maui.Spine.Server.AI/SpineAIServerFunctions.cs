using Microsoft.Extensions.AI;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>
/// The functions the server knows: those it runs itself with the caller's context, and those the app runs (device
/// functions), which the server declares to the model only when the app says it has them.
/// </summary>
public sealed class SpineAIServerFunctions
{
    private readonly SpineAIFunctions _server = new();
    private readonly SpineAIFunctions _device = new();

    /// <summary>The functions the server runs.</summary>
    public IReadOnlyCollection<SpineAIFunction> Server => _server;

    /// <summary>The functions the app runs, as the server declares them.</summary>
    public IReadOnlyCollection<SpineAIFunction> Device => _device;

    /// <summary>Runs <paramref name="function"/> on the server with the caller's context.</summary>
    /// <param name="function">The function, often from a project the app shares.</param>
    /// <returns>The same set, for chaining.</returns>
    public SpineAIServerFunctions Add(SpineAIFunction function)
    {
        _server.Add(function);
        return this;
    }

    /// <summary>Runs a method or a lambda on the server with the caller's context. See <see cref="SpineAIFunction.Create"/>.</summary>
    /// <param name="name">The name the model calls it by.</param>
    /// <param name="description">What it does and when to call it, for the model.</param>
    /// <param name="method">The implementation.</param>
    /// <param name="prefer">The side that runs it when the app offers it too.</param>
    /// <returns>The same set, for chaining.</returns>
    public SpineAIServerFunctions Add(string name, string description, Delegate method, SpineAIFunctionSide prefer = SpineAIFunctionSide.Server) =>
        Add(SpineAIFunction.Create(name, description, method, prefer));

    /// <summary>
    /// Declares a function the app runs. The server never calls <paramref name="function"/>'s implementation; it gives the
    /// model the declaration when the app says it has the function, and takes the app's result only for a call it made.
    /// </summary>
    /// <param name="function">The function, from a project the app shares.</param>
    /// <returns>The same set, for chaining.</returns>
    public SpineAIServerFunctions AddDevice(SpineAIFunction function)
    {
        _device.Add(function);
        return this;
    }

    /// <summary>Declares a function the app runs, by its name, description and parameters' JSON schema.</summary>
    /// <param name="name">The name the model calls it by.</param>
    /// <param name="description">What it does and when to call it, for the model.</param>
    /// <param name="jsonSchema">The parameters' schema; <c>{"type":"object","properties":{}}</c> for none.</param>
    /// <returns>The same set, for chaining.</returns>
    public SpineAIServerFunctions AddDevice(string name, string description, System.Text.Json.JsonElement jsonSchema) =>
        AddDevice(SpineAIFunction.From(new DeclaredOnly(AIFunctionFactory.CreateDeclaration(name, description, jsonSchema))));

    internal SpineAIFunction? ServerFunction(string name) => _server[name];

    internal SpineAIFunction? DeviceFunction(string name) => _device[name];

    internal bool Knows(string name) => _server[name] is not null || _device[name] is not null;

    /// <summary>A device function declared by schema: there is nothing to run here.</summary>
    private sealed class DeclaredOnly(AIFunctionDeclaration declaration) : AIFunction
    {
        public override string Name => declaration.Name;

        public override string Description => declaration.Description;

        public override System.Text.Json.JsonElement JsonSchema => declaration.JsonSchema;

        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"'{Name}' is a device function; the app runs it, not the server.");
    }
}
