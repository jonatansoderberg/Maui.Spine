using Microsoft.Identity.Client;
#if MACCATALYST
using Microsoft.Identity.Client.Extensibility;
#elif ANDROID
using System.Security.Cryptography;
using Android.Content.PM;
#elif WINDOWS
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
#endif

namespace Plugin.Maui.Spine.Authentication;

/// <summary>MSAL's public client for the app, set up for the platform: the broker, the redirect, the parent window and the cache.</summary>
#pragma warning disable CS9113 // The callback scheme and the browser are Mac Catalyst's alone.
internal sealed class MicrosoftClient(SpineMicrosoftOptions options, SpineAuthenticationOptions auth, IBrowserSignIn browser)
#pragma warning restore CS9113
{
    private static readonly string[] GraphRead = ["User.Read"];

    // With AddMicrosoft the ID token is what counts: the email scope puts the address (and, with the optional claim,
    // xms_edov) in it, which the server keeps when the domain is verified.
    private IEnumerable<string> SignInScopes => options.Scopes.Count > 0 ? options.Scopes : ["email"];
    private static readonly HttpClient Graph = new() { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _app;

    /// <summary>The remembered account without a prompt when MSAL can, otherwise MSAL's sign-in.</summary>
    /// <param name="freshIdToken">Refreshes even a valid token, so the ID token the server checks has not expired.</param>
    /// <param name="cancellationToken">Gives up on the sign-in.</param>
    /// <returns>MSAL's answer, or null when the user cancelled.</returns>
    /// <exception cref="InvalidOperationException">MSAL failed; the message says how.</exception>
    public async Task<AuthenticationResult?> SignInAsync(bool freshIdToken, CancellationToken cancellationToken)
    {
        var app = await AppAsync();
        try
        {
            if ((await app.GetAccountsAsync()).FirstOrDefault() is { } account)
            {
                try
                {
                    return await app.AcquireTokenSilent(SignInScopes, account).WithForceRefresh(freshIdToken).ExecuteAsync(cancellationToken);
                }
                catch (MsalUiRequiredException)
                {
                }
            }

            var interactive = app.AcquireTokenInteractive(SignInScopes);

            // Consent for the picture in the same prompt; Graph's token is asked for silently when the picture is.
            if (options.Picture && !options.Scopes.Contains(GraphRead[0], StringComparer.OrdinalIgnoreCase))
                interactive = interactive.WithExtraScopesToConsent(GraphRead);
#if MACCATALYST
            // MSAL has no Mac Catalyst build: its net8.0 build would start a loopback listener and a browser it cannot
            // open here. Spine's browser layer (WebAuthenticator) takes the sign-in to the callback scheme and back.
            interactive = interactive.WithCustomWebUi(new BrowserWebUi(browser));
#endif
            return await interactive.ExecuteAsync(cancellationToken);
        }
        catch (MsalClientException e) when (e.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            return null;
        }
        catch (MsalException e)
        {
            throw new InvalidOperationException($"Sign in with Microsoft failed: {e.ErrorCode}: {e.Message}", e);
        }
    }

    /// <summary>A new token for the account without a prompt.</summary>
    /// <returns>Null when MSAL no longer has the account.</returns>
    /// <exception cref="MsalUiRequiredException">The account must sign in again.</exception>
    public async Task<AuthenticationResult?> SilentAsync(string accountId, bool forceRefresh, CancellationToken cancellationToken)
    {
        var app = await AppAsync();
        return await app.GetAccountAsync(accountId) is { } account
            ? await app.AcquireTokenSilent(options.Scopes, account).WithForceRefresh(forceRefresh).ExecuteAsync(cancellationToken)
            : null;
    }

    /// <summary>The account's picture from Graph (<c>/me/photo/$value</c>).</summary>
    /// <param name="accountId">The account, or MSAL's only one.</param>
    /// <param name="cancellationToken">Gives up.</param>
    /// <returns>Null when the account has none, or when <see cref="SpineMicrosoftOptions.Picture"/> is off or <c>User.Read</c> was not consented to.</returns>
    public async Task<byte[]?> PictureAsync(string? accountId, CancellationToken cancellationToken)
    {
        if (!options.Picture) return null;

        var app = await AppAsync();
        var account = accountId is null ? (await app.GetAccountsAsync()).FirstOrDefault() : await app.GetAccountAsync(accountId);
        if (account is null) return null;

        AuthenticationResult token;
        try
        {
            token = await app.AcquireTokenSilent(GraphRead, account).ExecuteAsync(cancellationToken);
        }
        catch (MsalUiRequiredException)
        {
            // No consent for User.Read (an app that turned Picture on later, an admin who withheld it): no picture,
            // and no prompt the user did not ask for.
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "me/photo/$value");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var response = await Graph.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>Removes the account, or every account, from MSAL's cache.</summary>
    public async Task ForgetAsync(string? accountId)
    {
        var app = await AppAsync();
        foreach (var account in await app.GetAccountsAsync())
            if (accountId is null || account.HomeAccountId?.Identifier == accountId)
                await app.RemoveAsync(account);
    }

    private async Task<IPublicClientApplication> AppAsync()
    {
        if (_app is { } built) return built;

        await _gate.WaitAsync();
        try
        {
            if (_app is { } raced) return raced;

            var builder = PublicClientApplicationBuilder.Create(options.ClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, options.Tenant)
                .WithRedirectUri(options.RedirectUri ?? DefaultRedirectUri());
#if IOS
            // The app's own keychain group: MSAL's default (com.microsoft.adalcache) needs an entitlement, and a first
            // keychain-access-groups entry changes where the app's other SecureStorage items are looked for.
            builder = builder.WithIosKeychainSecurityGroup(AppInfo.Current.PackageName)
                .WithParentActivityOrWindow(() => Platform.GetCurrentUIViewController()!)
                .WithBroker(options.UseBroker);
#elif ANDROID
            builder = builder.WithParentActivityOrWindow(() => Platform.CurrentActivity!).WithBroker(options.UseBroker);
#elif WINDOWS
            builder = builder.WithParentActivityOrWindow(WindowHandle);
            if (options.UseBroker) builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));
#endif
            var app = builder.Build();
#if WINDOWS
            // MSAL keeps no cache of its own on Windows; the account would be forgotten on every start.
            var storage = new StorageCreationPropertiesBuilder("spine.msal.cache", FileSystem.AppDataDirectory).Build();
            (await MsalCacheHelper.CreateAsync(storage)).RegisterCache(app.UserTokenCache);
#elif MACCATALYST
            KeepCacheInKeychain(app.UserTokenCache);
#endif
            return _app = app;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string DefaultRedirectUri()
    {
#if IOS
        return $"msauth.{AppInfo.Current.PackageName}://auth";
#elif ANDROID
        return $"msauth://{AppInfo.Current.PackageName}/{Uri.EscapeDataString(SignatureHash())}";
#elif MACCATALYST
        return $"{auth.CallbackScheme ?? throw new InvalidOperationException("Sign in with Microsoft on Mac Catalyst needs a.CallbackScheme.")}://callback";
#elif WINDOWS
        return options.UseBroker ? $"ms-appx-web://microsoft.aad.brokerplugin/{options.ClientId}" : "http://localhost";
#else
        return "http://localhost";
#endif
    }

#if ANDROID
    // The redirect Entra knows the app by: the SHA-1 of the key that signed the APK, in base64.
    private static string SignatureHash()
    {
        var context = Platform.AppContext;
#pragma warning disable CA1422, CS0618 // GetPackageInfo(string, flags) and Signatures are the only way before Android 9 and 13.
        byte[] certificate = OperatingSystem.IsAndroidVersionAtLeast(28)
            ? context.PackageManager!.GetPackageInfo(context.PackageName!, PackageInfoFlags.SigningCertificates)!.SigningInfo!.GetApkContentsSigners()![0].ToByteArray()!
            : context.PackageManager!.GetPackageInfo(context.PackageName!, PackageInfoFlags.Signatures)!.Signatures![0].ToByteArray()!;
#pragma warning restore CA1422, CS0618
        return Convert.ToBase64String(SHA1.HashData(certificate));
    }
#elif WINDOWS
    private static IntPtr WindowHandle() =>
        Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window
            ? WinRT.Interop.WindowNative.GetWindowHandle(window)
            : IntPtr.Zero;
#elif MACCATALYST
    private const string CacheKey = "spine.msal.cache";

    private static void KeepCacheInKeychain(ITokenCache cache)
    {
        cache.SetBeforeAccessAsync(async args =>
        {
            if (await SecureStorage.Default.GetAsync(CacheKey) is { Length: > 0 } stored)
                args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(stored));
        });
        cache.SetAfterAccessAsync(async args =>
        {
            if (args.HasStateChanged)
                await SecureStorage.Default.SetAsync(CacheKey, Convert.ToBase64String(args.TokenCache.SerializeMsalV3()));
        });
    }

    /// <summary>MSAL's sign-in in Spine's browser layer, back to <c>{CallbackScheme}://callback</c>.</summary>
    private sealed class BrowserWebUi(IBrowserSignIn browser) : ICustomWebUi
    {
        public async Task<Uri> AcquireAuthorizationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
        {
            var answer = await browser.AuthenticateAsync(_ => authorizationUri, cancellationToken)
                ?? throw new MsalClientException(MsalError.AuthenticationCanceledError, "The browser was closed.");
            return new Uri($"{redirectUri.GetLeftPart(UriPartial.Path)}?{Pkce.Query(answer.Values)}");
        }
    }
#endif
}
