using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Authentication;

namespace MauiSpineSampleApp.Pages.SignIn;

/// <summary>One way the account signs in, as the Sign-in page lists it, with the logo the sign-in sheet uses.</summary>
public sealed record LoginRow(string Provider, string Details, string? Subject = null)
{
    public string Name => Provider switch { "apple" => "Apple", "google" => "Google", "microsoft" => "Microsoft", "password" => "Email and password", "passkey" => "Passkey", _ => Provider };

    public string Logo => $"signin-{(Provider is "apple" or "google" or "microsoft" or "passkey" ? Provider : "password")}.svg";

    // Google's and Microsoft's logos keep their own colours; Apple's and the password symbol take the text colour.
    public Color? Tint => Provider is "google" or "microsoft" ? Colors.Transparent : null;
}

/// <summary>A provider the account can add, with the button's text.</summary>
public sealed record LinkRow(string Provider)
{
    public string Text => Provider == "passkey" ? "Add a passkey" : $"Link {Provider switch { "apple" => "Apple", "google" => "Google", "microsoft" => "Microsoft", _ => Provider }}";
}

public partial class SignInPageViewModel : SampleViewModel
{
    private readonly ISpineAuth _auth;
    private readonly INavigationService _navigation;
    private readonly IHttpClientFactory _http;

    public SignInPageViewModel(ISpineAuth auth, INavigationService navigation, IHttpClientFactory http)
    {
        _auth = auth;
        _navigation = navigation;
        _http = http;
        _auth.UserChanged += (_, _) => Update();
        Update();
    }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string Log { get; set; } = "Nothing yet";

    public string Providers => string.Join(", ", _auth.Providers.Select(p => p.IsNative ? $"{p.Name} (native)" : p.Name));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicture))]
    public partial ImageSource? Picture { get; set; }

    public bool HasPicture => Picture is not null;

    [ObservableProperty]
    public partial string Details { get; set; } = "";

    /// <summary>The account's logins, from <see cref="SpineUser.Logins"/>.</summary>
    public ObservableCollection<LoginRow> Logins { get; } = [];

    /// <summary>The providers the app offers that the account does not sign in with yet.</summary>
    public ObservableCollection<LinkRow> Linkable { get; } = [];

    [ObservableProperty]
    public partial string LinkLog { get; set; } = "";

    private void Update() => MainThread.BeginInvokeOnMainThread(async () =>
    {
        Status = _auth.User is { } user ? $"Signed in as {user.DisplayName ?? user.Email} with {user.Provider}" : "Signed out";
        Details = _auth.User is { } u
            ? $"Given name: {u.GivenName ?? "–"}, family name: {u.FamilyName ?? "–"}\nEmail: {u.Email ?? "–"}\nOrganization: {u.Organization ?? "–"}\nSigns in with: {string.Join(", ", u.SignInMethods ?? [u.Provider])}"
            : "";

        Logins.Clear();
        Linkable.Clear();
        if (_auth.User is { } signedIn)
        {
            foreach (var login in signedIn.Logins ?? [])
                Logins.Add(new LoginRow(login.Provider, Details(login), login.Subject));
            // An account can have a passkey on each device, so "Add a passkey" stays.
            foreach (var provider in _auth.Providers.Where(p => p.Name != "password" && (p.Name == "passkey" || Logins.All(l => l.Provider != p.Name))))
                Linkable.Add(new LinkRow(provider.Name));
        }

        Picture = await _auth.GetPictureAsync() is { } picture ? ImageSource.FromStream(() => new MemoryStream(picture)) : null;
    });

    // A passkey says where it is kept, the device it was made on and when it was last used, so several can be told apart.
    private static string Details(SpineLoginInfo login) => string.Join(" · ", (login.Provider == "passkey"
        ? new[] { login.Authenticator, login.DeviceName, login.LastUsedAt is { } used ? $"last used {used.ToLocalTime():g}" : null }
        : new[] { login.Email ?? login.DisplayName, login.LinkedAt?.ToLocalTime().ToString("g") }).OfType<string>());

    [RelayCommand]
    private async Task Link(string provider)
    {
        var result = await _auth.LinkAsync(provider);
        LinkLog = result.Status == SignInStatus.SignedIn ? $"{provider} added" : $"{provider}: {result.Status} {result.Error}";
    }

    [RelayCommand]
    private async Task Unlink(LoginRow row)
    {
        // Removing a way in is not undone by the same tap, so it asks first.
        var provider = row.Provider;
        var name = row.Name;
        var unlink = new MenuAction($"Unlink {name}", "Unlink.svg") { IsDestructive = true };
        var message = provider == "passkey"
            ? "This passkey no longer signs in to the account. Delete it from Passwords on the device it is on; the account's other passkeys still work."
            : $"{name} no longer signs in to this account. Link it again to add it back.";
        var picked = await _navigation.ShowActionsAsync(new ActionSheet(null, message)
        {
            Actions = [unlink],
        });
        if (picked != unlink) return;

        // A passkey is removed on its own, so a lost phone's passkey goes without the others.
        var result = provider == "passkey" && row.Subject is { } subject ? await _auth.UnlinkAsync(provider, subject) : await _auth.UnlinkAsync(provider);
        LinkLog = result.Status == SignInStatus.SignedIn ? $"{provider} removed" : $"{provider}: {result.Error}";
    }

    [RelayCommand]
    private async Task SignIn()
    {
        var result = await _auth.SignInAsync();
        Log = result.Status == SignInStatus.Failed ? $"Failed: {result.Error}" : result.Status.ToString();
    }

    [RelayCommand]
    private Task OpenMembers() => _navigation.NavigateToAsync<MembersPage>();

    [RequiresSignIn]
    [RelayCommand]
    private async Task CallServer()
    {
        // The "SampleApi" client in MauiProgram carries the access token and refreshes it.
        try
        {
            var response = await _http.CreateClient("SampleApi").GetAsync("sample/me");
            Log = $"GET /sample/me: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}";
        }
        catch (HttpRequestException e)
        {
            Log = $"GET /sample/me: {e.Message}";
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        await _auth.SignOutAsync();
        Log = "Signed out";
    }

    [RelayCommand]
    private async Task DeleteAccount()
    {
        var result = await _auth.DeleteAccountAsync();
        Log = result.Status == SignInStatus.Deleted ? "Account deleted" : $"Failed: {result.Error}";
    }
}
