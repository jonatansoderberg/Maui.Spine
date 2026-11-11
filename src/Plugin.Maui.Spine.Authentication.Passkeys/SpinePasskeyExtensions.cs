namespace Plugin.Maui.Spine.Authentication;

/// <summary>Adds passkeys to Spine sign-in.</summary>
public static class SpinePasskeyExtensions
{
    /// <summary>
    /// Passkeys through the platform's own sheet: "Sign in with a passkey" and "Create account with a passkey" in the
    /// sign-in sheet, passkeys offered by AutoFill in its email field (iOS), and <c>LinkAsync("passkey")</c> to add one
    /// to the signed-in account. On iOS and Mac Catalyst 16 or later and on Windows 10 1903 or later (Windows Hello, a
    /// phone or a security key); elsewhere the sheet leaves them out (Android #601). The server configures
    /// <c>Passkeys(p =&gt; p.RelyingPartyId = …)</c>, and an iOS or Mac project sets <c>&lt;SpinePasskeyDomain&gt;</c> to
    /// the same domain; Windows needs no association.
    /// </summary>
    /// <param name="options">The sign-in options.</param>
    /// <param name="configure">Changes what the sheet offers.</param>
    /// <returns>The same options, for chaining.</returns>
    /// <example>
    /// <code>
    /// o.AddAuthentication(a =>
    /// {
    ///     a.Server = new Uri("https://api.example.com/auth");
    ///     a.AddApple();
    ///     a.AddPasskeys();
    /// });
    /// </code>
    /// </example>
    public static SpineAuthenticationOptions AddPasskeys(this SpineAuthenticationOptions options, Action<SpinePasskeySignInOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var passkeys = new SpinePasskeySignInOptions();
        configure?.Invoke(passkeys);

#if WINDOWS
        return options.AddPasskeyCeremony(new PasskeyProvider(
            WindowsPasskeys.IsSupported, SupportsAutoFill: false, passkeys.AllowSignUp, _ => new WindowsPasskeys()));
#else
        return options.AddPasskeyCeremony(new PasskeyProvider(
            ApplePasskeys.IsSupported, ApplePasskeys.SupportsAutoFill, passkeys.AllowSignUp, _ => new ApplePasskeys()));
#endif
    }
}

/// <summary>What the sign-in sheet offers for passkeys. The server decides what it allows.</summary>
public sealed class SpinePasskeySignInOptions
{
    /// <summary>The sheet offers "Create account with a passkey". On by default; turn it off when the server's <c>AllowSignUp</c> is off.</summary>
    public bool AllowSignUp { get; set; } = true;
}
