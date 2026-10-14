using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Plugin.Maui.Spine.AI;

/// <summary>What every feature shares: the options, one <see cref="HttpClient"/>, the installation id and the usage store.</summary>
internal sealed class SpineAIRuntime
{
    private readonly Func<CancellationToken, Task<string?>> _installationId;
    private readonly Lazy<HttpClient> _http;

    public SpineAIRuntime(
        SpineAIOptions options,
        Func<CancellationToken, Task<string?>> installationId,
        IAIDeviceUsageStore usage,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? time = null)
    {
        Options = options;
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

    /// <summary>The client for <paramref name="feature"/>, or <see langword="null"/> when the app declared no such feature.</summary>
    public IChatClient? Feature(string feature, IServiceProvider services) =>
        Options.Features.TryGetValue(feature, out var chain) ? new SpineFeatureChatClient(feature, chain, this, services) : null;
}
