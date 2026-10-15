using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Plugin.Maui.Spine.Voice;

/// <summary>Registers <c>Plugin.Maui.Spine.Voice</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineVoiceExtensions
{
    /// <summary>
    /// Registers <see cref="IVoiceAudio"/>, a new engine for each conversation. <c>UseSpine()</c> calls it for an app that
    /// references this package; calling it more than once is harmless.
    /// </summary>
    public static MauiAppBuilder UseSpineVoice(this MauiAppBuilder builder)
    {
        builder.Services.TryAddTransient<IVoiceAudio>(static services => Create(
            services.GetService<ILoggerFactory>()?.CreateLogger("Plugin.Maui.Spine.Voice"),
            services.GetService<TimeProvider>()));
        return builder;
    }

    private static IVoiceAudio Create(ILogger? logger, TimeProvider? time) =>
#if IOS || MACCATALYST
        new AppleVoiceAudio(logger, time);
#else
        new UnsupportedVoiceAudio(logger, time);
#endif

    /// <summary>Android (#562) and Windows (#563) until their engines come: it says so when started.</summary>
    private sealed class UnsupportedVoiceAudio(ILogger? logger, TimeProvider? time) : VoiceAudioEngine(logger, time)
    {
        public override bool IsSupported => false;

        protected override Task StartPlatformAsync(VoiceAudioOptions options, CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task StopPlatformAsync() => Task.CompletedTask;
    }
}
