using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Spine.Authentication;
using Plugin.Maui.Spine.Server.AI;

namespace Plugin.Maui.Spine.AI;

/// <summary>What every feature shares: the options, one <see cref="HttpClient"/>, the installation id and the usage store.</summary>
internal sealed class SpineAIRuntime
{
    private readonly Func<CancellationToken, Task<string?>> _installationId;
    private readonly Lazy<HttpClient> _http;
    private readonly Func<CancellationToken, Task>? _whenActive;

    public SpineAIRuntime(
        SpineAIOptions options,
        Func<CancellationToken, Task<string?>> installationId,
        IAIDeviceUsageStore usage,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? time = null,
        Func<CancellationToken, Task>? whenActive = null)
    {
        Options = options;
        _whenActive = whenActive;
        _installationId = installationId;
        Usage = usage;
        Logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("Plugin.Maui.Spine.AI");
        Time = time ?? TimeProvider.System;

        // Created at the first call, so a handler set after registration (a second UseSpineAI) is the one used. A
        // model can take minutes to answer; the default 100 seconds would cut long answers off.
        _http = new(() => new HttpClient(options.HttpHandler ?? new HttpClientHandler(), disposeHandler: options.HttpHandler is null)
        {
            Timeout = TimeSpan.FromMinutes(5),
        });
    }

    public SpineAIOptions Options { get; }
    public IAIDeviceUsageStore Usage { get; }
    public ILogger Logger { get; }
    public TimeProvider Time { get; }
    public HttpClient Http => _http.Value;

    public Task<string?> InstallationIdAsync(CancellationToken cancellationToken) => _installationId(cancellationToken);

    /// <summary>Completes when the app is in front; at once where nothing tracks that, as in tests.</summary>
    public Task WhenActiveAsync(CancellationToken cancellationToken) => _whenActive?.Invoke(cancellationToken) ?? Task.CompletedTask;

    /// <summary>The <c>Authorization</c> and installation headers every call to the backend carries.</summary>
    public async Task AddHeadersAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Options.AuthorizationHeader is { } authorization && await authorization(cancellationToken) is { Length: > 0 } header)
            request.Headers.TryAddWithoutValidation("Authorization", header);

        if (await InstallationIdAsync(cancellationToken) is { Length: > 0 } installation)
            request.Headers.TryAddWithoutValidation(Common.SpineInstallation.Header, installation);
    }

    /// <summary>
    /// The context the app's functions run with: the signed-in user's id when Spine.Authentication shares it
    /// (<see cref="ISignedInSession"/>), and otherwise the installation.
    /// </summary>
    public async Task<SpineAIFunctionContext> DeviceContextAsync(string feature, string? conversationId, IServiceProvider services, CancellationToken cancellationToken)
    {
        var caller = services.GetService(typeof(ISignedInSession)) is ISignedInSession { UserId: { Length: > 0 } user }
            ? AICaller.SignedIn(user)
            : await InstallationIdAsync(cancellationToken) is { Length: > 0 } installation ? AICaller.Installation(installation) : AICaller.Anonymous(null);

        return new SpineAIFunctionContext(caller, SpineAIFunctionSide.Device)
        {
            Feature = feature,
            ConversationId = conversationId,
            Services = services,
            CancellationToken = cancellationToken,
        };
    }

    /// <summary>The client for <paramref name="feature"/>, or <see langword="null"/> when the app declared no such feature.</summary>
    public IChatClient? Feature(string feature, IServiceProvider services) =>
        Options.Features.TryGetValue(feature, out var chain) ? new SpineFeatureChatClient(feature, chain, this, services) : null;
}
