using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Authentication;

/// <summary>The package's text (keys <c>Spine.Authentication.*</c>), registered once.</summary>
internal static class AuthStrings
{
    private const string Prefix = "Spine.Authentication.";
    private static int _registered;

    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            SpineStrings.Current.AddDefaults(new EmbeddedXmlStringProvider(typeof(AuthStrings).Assembly));
    }

    public static string Get(string key)
    {
        EnsureRegistered();
        return SpineStrings.Current[Prefix + key];
    }

    /// <summary>The text for a server error, or a general one for anything the user cannot act on.</summary>
    public static string Error(string? error)
    {
        EnsureRegistered();
        return error is not null && SpineStrings.Current.Has($"{Prefix}Error.{error}") ? SpineStrings.Current[$"{Prefix}Error.{error}"] : Get("Error.Other");
    }
}
