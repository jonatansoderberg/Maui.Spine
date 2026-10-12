using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Controls;

/// <summary>
/// Registers the package's default text (keys <c>Spine.Chat.*</c>) the first time a <see cref="ChatView"/> or
/// <see cref="ChatComposer"/> is used, so the package needs no builder call.
/// </summary>
internal static class ChatStrings
{
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(ChatStrings).Assembly));
    }

    public static string Get(string key) => SpineStrings.Current[key];

    public static string Get(string key, params object?[] args) => SpineStrings.Current.Get(key, args);
}
