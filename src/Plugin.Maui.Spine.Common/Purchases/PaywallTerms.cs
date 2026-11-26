namespace Plugin.Maui.Spine.Purchases;

/// <summary>Terms of use a paywall can link to.</summary>
public static class PaywallTerms
{
    /// <summary>
    /// Apple's standard licensed application end user licence agreement: the terms of an app on the App Store that has
    /// none of its own, as App Store Connect's License Agreement is by default. It does not fit a Google Play build.
    /// </summary>
    public static Uri AppleStandardEula { get; } = new("https://www.apple.com/legal/internet-services/itunes/dev/stdeula/");
}

/// <summary>A link per store, with one for every store under them.</summary>
internal sealed class StoreLinks
{
    public Uri? Everywhere { get; set; }
    public Uri? Apple { get; set; }
    public Uri? Google { get; set; }
    public Uri? Microsoft { get; set; }

    /// <summary>The store's own link, or the one for every store.</summary>
    public Uri? For(StorePlatform store) => store switch
    {
        StorePlatform.Apple => Apple,
        StorePlatform.Google => Google,
        StorePlatform.Microsoft => Microsoft,
        _ => null,
    } ?? Everywhere;

    /// <summary>The store this build sells through: Google Play on Android, the Microsoft Store on Windows, else the App Store.</summary>
    public static StorePlatform Current =>
        OperatingSystem.IsAndroid() ? StorePlatform.Google
        : OperatingSystem.IsWindows() ? StorePlatform.Microsoft
        : StorePlatform.Apple;
}
