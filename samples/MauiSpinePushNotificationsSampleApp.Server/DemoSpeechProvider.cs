using Plugin.Maui.Spine.Server.AI;

namespace MauiSpinePushNotificationsSampleApp.Server;

/// <summary>
/// A speech provider that needs no key: a hum shaped by each letter, with a viseme per letter in Azure Speech's alphabet,
/// so the app's avatar mouths TimedVisemes on a laptop with nothing set up. Not a voice; the timing is what it shows.
/// </summary>
internal sealed class DemoSpeechProvider : IAISpeechProvider
{
    private const int Rate = 24_000;
    private static readonly TimeSpan Letter = TimeSpan.FromMilliseconds(75);

    public Task<AISpeech> SynthesizeAsync(AISpeechRequest request, CancellationToken cancellationToken = default)
    {
        var visemes = new List<AISpeechViseme>();
        var samples = new List<short>();
        var at = TimeSpan.Zero;
        var previous = -1;

        foreach (var letter in request.Text.ToLowerInvariant())
        {
            var id = Viseme(letter);
            if (id != previous)
                visemes.Add(new AISpeechViseme(id, at));
            previous = id;

            // Open vowels hum louder than closed consonants; silence is silence.
            var loudness = id switch { 0 => 0f, 21 => 0.05f, 2 or 8 or 9 or 11 => 0.35f, 4 or 6 or 7 => 0.25f, _ => 0.12f };
            var count = (int)(Letter.TotalSeconds * Rate);
            for (var i = 0; i < count; i++)
            {
                var t = (samples.Count + i) / (double)Rate;
                var envelope = Math.Sin(Math.PI * i / count);
                var value = loudness * envelope * (Math.Sin(2 * Math.PI * 140 * t) + 0.5 * Math.Sin(2 * Math.PI * 280 * t) + 0.25 * Math.Sin(2 * Math.PI * 700 * t)) / 1.75;
                samples.Add((short)(value * short.MaxValue));
            }
            at += Letter;
        }

        visemes.Add(new AISpeechViseme(0, at));
        var pcm = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, pcm, 0, pcm.Length);
        return Task.FromResult(new AISpeech(pcm, Rate, visemes));
    }

    // Azure Speech's viseme ids: https://learn.microsoft.com/azure/ai-services/speech-service/how-to-speech-synthesis-viseme
    private static int Viseme(char letter) => letter switch
    {
        'a' or 'å' => 2,
        'ä' or 'e' => 4,
        'i' or 'y' => 6,
        'o' or 'ö' => 8,
        'u' => 7,
        'p' or 'b' or 'm' => 21,
        'f' or 'v' or 'w' => 18,
        's' or 'z' or 'c' => 15,
        'l' => 14,
        'r' => 13,
        't' or 'd' or 'n' => 19,
        'k' or 'g' or 'q' or 'x' => 20,
        'j' or 'h' => 12,
        _ when char.IsLetter(letter) => 19,
        _ => 0,
    };
}
