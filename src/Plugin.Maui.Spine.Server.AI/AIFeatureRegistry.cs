using System.Drawing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Plugin.Maui.Spine.Server.AI;

/// <summary>A feature as currently configured, with its shared pipeline when it is on.</summary>
internal sealed record AIFeature(
    string Name,
    AIFeatureOptions Options,
    AIOffReason? Off,
    string? OffDetail,
    IChatClient? Chat,
    IImageGenerator? Image,
    IAIRealtimeProvider? Realtime,
    AIPrice? Price)
{
    public IAISpeechProvider? Speech { get; init; }

    public IAITranscriptionProvider? Transcription { get; init; }

    public string Provider => Options.Provider ?? "";
    public string Model => Options.Model ?? "";
}

/// <summary>Everything built from one reading of the configuration.</summary>
internal sealed record AISnapshot(AIConfiguration Configuration, TimeZoneInfo Zone, IReadOnlyDictionary<string, AIFeature> Features)
{
    public AIPrice? PriceOf(string provider, string model) =>
        Configuration.Prices.TryGetValue(provider, out var models) && models.TryGetValue(model, out var price) ? price : null;
}

/// <summary>
/// Reads the <c>Ai</c> section and builds each feature's pipeline once: the provider's adapter under
/// OpenTelemetry, logging and the feature's default options. It is built again when the section changes.
/// </summary>
/// <remarks>
/// A replaced pipeline is not disposed: a call or a stream may still be running on it, and configuration
/// changes are rare enough to leave it to the collector.
/// </remarks>
internal sealed class AIFeatureRegistry
{
    internal const string TelemetrySource = "Plugin.Maui.Spine.Server.AI";

    private readonly IConfiguration _section;
    private readonly SpineAIOptions _options;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private AISnapshot? _current;

    public AIFeatureRegistry(IConfiguration section, SpineAIOptions options, IServiceProvider services, ILoggerFactory loggerFactory)
    {
        _section = section;
        _options = options;
        _services = services;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger("Plugin.Maui.Spine.Server.AI");

        ChangeToken.OnChange(section.GetReloadToken, Rebuild);
    }

    /// <summary>The features as configured now.</summary>
    public AISnapshot Current
    {
        get
        {
            if (_current is { } current) return current;

            lock (_gate)
            {
                return _current ??= Build();
            }
        }
    }

    /// <summary>Raised after the configuration changed and the features were built again.</summary>
    public event Action? Rebuilt;

    private void Rebuild()
    {
        lock (_gate)
        {
            _current = Build();
        }

        _logger.LogInformation("AI features were rebuilt after a configuration change.");
        Rebuilt?.Invoke();
    }

    private AISnapshot Build()
    {
        var configuration = new AIConfiguration();
        _section.Bind(configuration);

        var zone = TimeZoneOf(configuration.TimeZone);
        var features = new Dictionary<string, AIFeature>(StringComparer.OrdinalIgnoreCase);
        var snapshot = new AISnapshot(configuration, zone, features);

        foreach (var (name, options) in configuration.Features)
        {
            var feature = BuildFeature(snapshot, name, options);
            features[name] = feature;

            foreach (var function in options.Functions ?? [])
            {
                if (!_options.Functions.Knows(function))
                    _logger.LogWarning(
                        "AI feature '{Feature}' lists the function '{Function}', which is not registered; add ai.Functions.Add(...) or AddDevice(...) in AddSpineAI.",
                        name, function);
            }

            if (feature.Off is { } off)
                _logger.LogWarning("AI feature '{Feature}' is off ({Reason}): {Detail}", name, off, feature.OffDetail);
            else if (feature.Price is null)
                _logger.LogWarning(
                    "AI feature '{Feature}' has no price for {Provider}/{Model} under Ai:Prices, so its calls are recorded without a cost and budgets count them by number only.",
                    name, feature.Provider, feature.Model);
        }

        return snapshot;
    }

    private AIFeature BuildFeature(AISnapshot snapshot, string name, AIFeatureOptions options)
    {
        AIFeature Off(AIOffReason reason, string detail) => new(name, options, reason, detail, null, null, null, null);

        if (!options.Enabled) return Off(AIOffReason.Disabled, "Enabled is false.");
        if (string.IsNullOrWhiteSpace(options.Provider)) return Off(AIOffReason.NotConfigured, "it names no Provider.");
        if (string.IsNullOrWhiteSpace(options.Model)) return Off(AIOffReason.NotConfigured, "it names no Model.");

        var provider = options.Provider;
        var model = options.Model;
        var providerSection = _section.GetSection("Providers").GetSection(provider);

        // An ApiKey entry that is there but empty is the usual sign of a secret that never arrived: the
        // placeholder in appsettings.json without the value from user secrets or Key Vault.
        if (providerSection.GetSection("ApiKey") is { Value: "" })
            return Off(AIOffReason.NotConfigured, $"provider '{provider}' has an empty ApiKey under Ai:Providers:{provider}.");

        var price = snapshot.PriceOf(provider, model);

        try
        {
            switch (options.Kind)
            {
                case AIFeatureKind.Chat when _options.ChatProviders.TryGetValue(provider, out var factory):
                    return new(name, options, null, null, ChatPipeline(factory(providerSection, model), options), null, null, price);

                case AIFeatureKind.Image when _options.ImageProviders.TryGetValue(provider, out var factory):
                    return new(name, options, null, null, null, ImagePipeline(factory(providerSection, model), options), null, price);

                case AIFeatureKind.Realtime when _options.RealtimeProviders.TryGetValue(provider, out var factory):
                    var realtime = factory(providerSection);
                    if (options.Realtime is { Protocol: AIRealtimeProtocol.Live } live)
                    {
                        if (realtime is not IAILiveProvider)
                            return Off(AIOffReason.NotConfigured, $"Protocol is Live, and realtime provider '{provider}' does not speak GPT-Live.");
                        if (string.IsNullOrWhiteSpace(live.Backend?.Model))
                            return Off(AIOffReason.NotConfigured, "Protocol is Live and Realtime:Backend:Model names no backend model.");
                    }
                    return new(name, options, null, null, null, null, realtime, price);

                case AIFeatureKind.Speech when _options.SpeechProviders.TryGetValue(provider, out var factory):
                    return new(name, options, null, null, null, null, null, price) { Speech = factory(providerSection) };

                case AIFeatureKind.Transcription when _options.TranscriptionProviders.TryGetValue(provider, out var factory):
                    return new(name, options, null, null, null, null, null, price) { Transcription = factory(providerSection) };

                default:
                    var register = options.Kind.ToString();
                    return Off(AIOffReason.NotConfigured,
                        $"no {options.Kind.ToString().ToLowerInvariant()} provider '{provider}' is registered; add ai.{register}(\"{provider}\", ...) in AddSpineAI.");
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "AI feature '{Feature}': the factory for provider '{Provider}' threw.", name, provider);
            return Off(AIOffReason.NotConfigured, $"the factory for provider '{provider}' threw {e.GetType().Name}: {e.Message}");
        }
    }

    private IChatClient ChatPipeline(IChatClient vendor, AIFeatureOptions feature) =>
        new ChatClientBuilder(vendor)
            .ConfigureOptions(o =>
            {
                o.ModelId ??= feature.Model;
                o.MaxOutputTokens ??= feature.MaxOutputTokens;
                o.Temperature ??= feature.Temperature;
                if (feature.Reasoning is { } effort) o.Reasoning ??= new ReasoningOptions { Effort = effort };
            })
            .UseOpenTelemetry(_loggerFactory, TelemetrySource, c => c.EnableSensitiveData = feature.EnableSensitiveData)
            .UseLogging(_loggerFactory)
            .Build(_services);

    private IImageGenerator ImagePipeline(IImageGenerator vendor, AIFeatureOptions feature) =>
        new ImageGeneratorBuilder(vendor)
            .ConfigureOptions(o =>
            {
                o.ModelId ??= feature.Model;
                o.Count ??= feature.Image?.Count;
                o.ImageSize ??= SizeOf(feature.Image?.Size);
            })
            .UseOpenTelemetry(_loggerFactory, TelemetrySource, g => g.EnableSensitiveData = feature.EnableSensitiveData)
            .UseLogging(_loggerFactory)
            .Build(_services);

    internal static Size? SizeOf(string? text) =>
        text?.Split('x', 'X') is [var w, var h] && int.TryParse(w, out var width) && int.TryParse(h, out var height)
            ? new Size(width, height)
            : null;

    private TimeZoneInfo TimeZoneOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _logger.LogError("Ai:TimeZone '{TimeZone}' is not a time zone on this machine; budgets follow UTC days instead.", id);
            return TimeZoneInfo.Utc;
        }
    }
}
