namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// The avatar's mouth vocabulary: the 15 canonical visemes of <c>oculus15-v1</c> (0 <c>sil</c> … 14
/// <c>U</c>) that packages map to their poses, and the maps from the alphabets drivers receive.
/// </summary>
public static class AvatarVisemes
{
    public const int Count = 15;

    public const int Silence = 0, PP = 1, FF = 2, TH = 3, DD = 4, KK = 5, CH = 6, SS = 7, NN = 8, RR = 9, AA = 10, E = 11, I = 12, O = 13, U = 14;

    /// <summary>The canonical names, by id.</summary>
    public static IReadOnlyList<string> Names => AvatarVocabulary.Visemes;

    // Azure Speech's 22 ids (Spine.Voice's Visemes) by index → canonical id.
    private static readonly int[] s_fromAzure =
    [
        Silence, // 0 silence
        AA,      // 1 æ ə ʌ
        AA,      // 2 ɑ
        O,       // 3 ɔ
        E,       // 4 ɛ ʊ
        E,       // 5 ɝ
        I,       // 6 j i ɪ
        U,       // 7 w u
        O,       // 8 o
        AA,      // 9 aʊ
        O,       // 10 ɔɪ
        I,       // 11 aɪ
        KK,      // 12 h
        RR,      // 13 ɹ
        NN,      // 14 l
        SS,      // 15 s z
        CH,      // 16 ʃ tʃ dʒ ʒ
        TH,      // 17 ð
        FF,      // 18 f v
        DD,      // 19 d t n θ
        KK,      // 20 k g ŋ
        PP,      // 21 p b m
    ];

    // Canonical id → the Azure id that stands for it, so a round trip keeps the canonical id.
    private static readonly int[] s_toAzure = [0, 21, 18, 17, 19, 20, 16, 15, 14, 13, 2, 4, 6, 8, 7];

    /// <summary>The canonical viseme for an Azure Speech viseme id (0–21); -1 (no viseme) stays -1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id is neither -1 nor 0–21.</exception>
    public static int FromAzure(int azureId) => azureId switch
    {
        -1 => -1,
        >= 0 and < 22 => s_fromAzure[azureId],
        _ => throw new ArgumentOutOfRangeException(nameof(azureId), azureId, "Azure viseme ids are 0–21"),
    };

    /// <summary>The Azure Speech viseme id that stands for a canonical one; <see cref="FromAzure"/> maps it back.</summary>
    public static int ToAzure(int canonicalId) => canonicalId is >= 0 and < Count
        ? s_toAzure[canonicalId]
        : throw new ArgumentOutOfRangeException(nameof(canonicalId), canonicalId, "canonical viseme ids are 0–14");

    /// <summary>Visemes guessed from letters, timed from 0 at <paramref name="charactersPerSecond"/>; quality EstimatedText.</summary>
    public static IReadOnlyList<AvatarTextVisemes.Estimate> FromText(string text, double charactersPerSecond = 14) =>
        AvatarTextVisemes.FromText(text, charactersPerSecond);
}
