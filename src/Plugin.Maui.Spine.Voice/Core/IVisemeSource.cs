namespace Plugin.Maui.Spine.Voice;

/// <summary>
/// Gives an avatar's mouth shape for the answer as it plays: the item and how far it has played. Register it as
/// <see cref="SpineVoiceOptions.VisemeSource"/>; <see cref="IVoiceSession.UpdateFrame"/> asks it on every tick and puts the
/// answer in <see cref="VoiceFrame.Viseme"/>.
/// </summary>
/// <remarks>
/// The realtime APIs give no visemes. A source can come from a text-to-speech service that does (Azure Speech sends viseme
/// ids with audio offsets), or estimate them from the audio.
/// </remarks>
public interface IVisemeSource
{
    /// <summary>The mouth shape at <paramref name="playing"/>, one of <see cref="Visemes"/>, or -1 when the source has none.</summary>
    int VisemeAt(PlaybackPosition playing);
}

/// <summary>Mouth-shape ids, the 22 of Azure Speech's viseme set (0 is silence), so a source from Azure needs no mapping.</summary>
public static class Visemes
{
    /// <summary>No viseme: avatars follow the output level.</summary>
    public const int None = -1;

    /// <summary>The mouth closed, at rest.</summary>
    public const int Silence = 0;

    /// <summary>æ, ə, ʌ: open, relaxed.</summary>
    public const int Ae = 1;

    /// <summary>ɑ: wide open.</summary>
    public const int Aa = 2;

    /// <summary>ɔ: open, rounded.</summary>
    public const int Ao = 3;

    /// <summary>ɛ, ʊ: half open.</summary>
    public const int Eh = 4;

    /// <summary>ɝ: half open, lips forward.</summary>
    public const int Er = 5;

    /// <summary>j, i, ɪ: spread, nearly closed.</summary>
    public const int Iy = 6;

    /// <summary>w, u: rounded, small.</summary>
    public const int Uw = 7;

    /// <summary>o: rounded.</summary>
    public const int Ow = 8;

    /// <summary>aʊ: open, then rounding.</summary>
    public const int Aw = 9;

    /// <summary>ɔɪ: rounded, then spread.</summary>
    public const int Oy = 10;

    /// <summary>aɪ: open, then spread.</summary>
    public const int Ay = 11;

    /// <summary>h: open, relaxed.</summary>
    public const int H = 12;

    /// <summary>ɹ: slightly rounded.</summary>
    public const int R = 13;

    /// <summary>l: half open, tongue up.</summary>
    public const int L = 14;

    /// <summary>s, z: teeth together.</summary>
    public const int S = 15;

    /// <summary>ʃ, tʃ, dʒ, ʒ: lips forward, teeth together.</summary>
    public const int Sh = 16;

    /// <summary>ð: tongue between the teeth.</summary>
    public const int Th = 17;

    /// <summary>f, v: lower lip to the teeth.</summary>
    public const int F = 18;

    /// <summary>d, t, n, θ: nearly closed.</summary>
    public const int D = 19;

    /// <summary>k, g, ŋ: half open, back.</summary>
    public const int K = 20;

    /// <summary>p, b, m: lips closed.</summary>
    public const int P = 21;

    /// <summary>How open the mouth is for <paramref name="viseme"/>, 0 (closed) to 1 (wide open), and how wide (0 narrow and round to 1 spread).</summary>
    public static (float Open, float Wide) Shape(int viseme) => viseme switch
    {
        Silence or P => (0f, 0.5f),
        Aa => (1f, 0.6f),
        Ae or H => (0.75f, 0.6f),
        Ao or Aw => (0.8f, 0.3f),
        Ow or Oy => (0.6f, 0.2f),
        Uw => (0.35f, 0.1f),
        Eh or Ay => (0.6f, 0.75f),
        Er or R => (0.45f, 0.35f),
        Iy => (0.25f, 0.95f),
        L or K => (0.45f, 0.6f),
        S or Sh or Th or D => (0.15f, 0.7f),
        F => (0.1f, 0.6f),
        _ => (0f, 0.5f),
    };
}
