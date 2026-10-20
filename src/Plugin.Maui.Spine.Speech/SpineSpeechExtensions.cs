using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Plugin.Maui.Spine.Speech;

/// <summary>Registers <c>Plugin.Maui.Spine.Speech</c> with a <see cref="MauiAppBuilder"/>.</summary>
public static class SpineSpeechExtensions
{
    /// <summary>
    /// Registers <see cref="ISpineSpeech"/>, <see cref="ISpineSpeechRecognizer"/> and <see cref="ISpineSpeechRenderer"/>, one each for the app. <c>UseSpine()</c> calls it for an app that references this
    /// package; an app without Spine calls it itself.
    /// </summary>
    public static MauiAppBuilder UseSpineSpeech(this MauiAppBuilder builder)
    {
        builder.Services.TryAddSingleton<ISpineSpeech>(static _ => Create());
        builder.Services.TryAddSingleton<ISpineSpeechRecognizer>(static _ => CreateRecognizer());
        builder.Services.TryAddSingleton<ISpineSpeechRenderer>(static _ => CreateRenderer());
        return builder;
    }

    private static ISpineSpeech Create()
    {
#if IOS || MACCATALYST
        return new AppleSpineSpeech();
#elif ANDROID
        return new AndroidSpineSpeech();
#elif WINDOWS
        return new WindowsSpineSpeech();
#else
        return new UnsupportedSpeech(MainThread.BeginInvokeOnMainThread);
#endif
    }

    private static ISpineSpeechRecognizer CreateRecognizer()
    {
#if IOS || MACCATALYST
        return new AppleSpeechRecognizer();
#else
        return new UnsupportedSpeechRecognizer();
#endif
    }

    private static ISpineSpeechRenderer CreateRenderer()
    {
#if IOS || MACCATALYST
        return new AppleSpeechRenderer();
#else
        return new UnsupportedSpeechRenderer();
#endif
    }
}
