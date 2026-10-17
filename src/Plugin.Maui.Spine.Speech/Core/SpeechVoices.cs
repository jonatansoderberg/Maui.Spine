namespace Plugin.Maui.Spine.Speech;

/// <summary>Choosing among the installed voices.</summary>
internal static class SpeechVoices
{
    /// <summary>Voices for <paramref name="language"/> (exact first, then its language part), best first; all of them, best first, without one.</summary>
    public static IReadOnlyList<SpeechVoice> For(IEnumerable<SpeechVoice> voices, string? language)
    {
        var list = voices.ToList();
        if (language is null)
            return [.. list.OrderByDescending(v => v.Quality).ThenBy(v => v.Language, StringComparer.Ordinal).ThenBy(v => v.Name, StringComparer.Ordinal)];

        var prefix = language.Split('-')[0];
        return [.. list
            .Where(v => v.Language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && (v.Language.Length == prefix.Length || v.Language[prefix.Length] is '-' or '_'))
            .OrderByDescending(v => string.Equals(v.Language.Replace('_', '-'), language, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(v => v.Quality)
            .ThenBy(v => v.Name, StringComparer.Ordinal)];
    }

    /// <summary>The voice for <paramref name="options"/>: the one asked for, else the best for its language, else null (the platform's default).</summary>
    public static SpeechVoice? Pick(IEnumerable<SpeechVoice> voices, SpeakOptions options)
    {
        var list = voices as IReadOnlyCollection<SpeechVoice> ?? [.. voices];
        if (options.VoiceId is { } id && list.FirstOrDefault(v => v.Id == id) is { } chosen)
            return chosen;
        return options.Language is { } language ? For(list, language).FirstOrDefault() : null;
    }
}
