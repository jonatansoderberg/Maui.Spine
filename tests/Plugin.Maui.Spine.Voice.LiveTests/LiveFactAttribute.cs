namespace Plugin.Maui.Spine.Voice.LiveTests;

/// <summary>A test against the real models: skipped unless every named environment variable holds a key.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public const string OpenAI = "OPENAI_API_KEY";
    public const string Anthropic = "ANTHROPIC_API_KEY";

    public LiveFactAttribute(params string[] keys)
    {
        var missing = keys.Where(k => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k))).ToList();
        if (missing.Count > 0)
            Skip = $"Live: set {string.Join(" and ", missing)} to run it against the real models (a few cents a run).";
    }
}
