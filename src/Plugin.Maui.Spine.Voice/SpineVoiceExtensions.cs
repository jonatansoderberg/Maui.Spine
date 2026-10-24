using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Spine.AI;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Voice;

/// <summary>Registers <c>Plugin.Maui.Spine.Voice</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineVoiceExtensions
{
    static readonly SpineVoiceOptions Options = new() { Functions = SpineAIFunctions.App };

    /// <summary>
    /// Registers <see cref="IVoiceSession"/> (a realtime feature, or a <see cref="VoicePipeline"/> by its name) and
    /// <see cref="IVoiceAudio"/>, a new one for each conversation, and
    /// <see cref="ServerSpeech"/>. <c>UseSpine()</c>
    /// calls it for an app that references this package; call it yourself with the session endpoint, before or after
    /// <c>UseSpine()</c>. A second call applies its options.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseSpineVoice(o =>
    /// {
    ///     o.SessionEndpoint = new Uri("https://api.example.com/ai");
    ///     o.AuthorizationHeader = ct => auth.GetHeaderAsync(ct);
    ///     o.Functions.Add("open_page", "Opens a page of the app.", OpenPage);
    /// });
    /// </code>
    /// </example>
    public static MauiAppBuilder UseSpineVoice(this MauiAppBuilder builder, Action<SpineVoiceOptions>? configure = null)
    {
        configure?.Invoke(Options);

        builder.Services.TryAddSingleton(Options);
        builder.Services.TryAddTransient<IVoiceAudio>(static services => CreateAudio(services));
        builder.Services.TryAddTransient<IVoiceSession>(static services =>
        {
            // One engine for both: only one of them talks at a time.
            var audio = CreateAudio(services);
            var time = services.GetService<TimeProvider>();
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Voice");
            return new RoutingVoiceSession(
                new VoiceSession(audio, Options, new VoiceTokenClient(Options, InstallationIdAsync), static () => new ClientVoiceSocket(),
                    static action => MainThread.BeginInvokeOnMainThread(action), time, logger, services),
                new PipelineVoiceSession(audio, Options, new PipelineServer(Options, InstallationIdAsync),
                    static action => MainThread.BeginInvokeOnMainThread(action), time, logger, services),
                Options);
        });
        builder.Services.TryAddTransient(static services => new ServerSpeech(
            CreateAudio(services),
            Options,
            InstallationIdAsync,
            static action => MainThread.BeginInvokeOnMainThread(action),
            services.GetService<TimeProvider>(),
            services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Voice")));
        return builder;
    }

    private static IVoiceAudio CreateAudio(IServiceProvider services)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Voice");
        var time = services.GetService<TimeProvider>();
#if IOS || MACCATALYST
        return new AppleVoiceAudio(logger, time);
#else
        return new UnsupportedVoiceAudio(logger, time);
#endif
    }

    /// <summary>
    /// The installation id the server knows the app by: the same <see cref="SecureStorage"/> key as push and the AI
    /// client, so the server sees one id per installation.
    /// </summary>
    static async Task<string?> InstallationIdAsync(CancellationToken cancellationToken)
    {
        if (await SecureStorage.Default.GetAsync(SpineInstallation.IdKey) is { Length: > 0 } existing)
            return existing;

        var created = Guid.NewGuid().ToString("N");
        await SecureStorage.Default.SetAsync(SpineInstallation.IdKey, created);
        return created;
    }

    /// <summary>Android (#562) and Windows (#563) until their engines come: it says so when started.</summary>
    private sealed class UnsupportedVoiceAudio(ILogger? logger, TimeProvider? time) : VoiceAudioEngine(logger, time)
    {
        public override bool IsSupported => false;

        protected override Task StartPlatformAsync(VoiceAudioOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task StopPlatformAsync() => Task.CompletedTask;
    }
}
