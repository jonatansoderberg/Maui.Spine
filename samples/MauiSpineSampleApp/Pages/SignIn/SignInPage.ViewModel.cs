using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Authentication;

namespace MauiSpineSampleApp.Pages.SignIn;

/// <summary>One way the account signs in, as the Sign-in page lists it.</summary>
public sealed record LoginRow(string Provider, string Details);

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
    public ObservableCollection<string> Linkable { get; } = [];

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
                Logins.Add(new LoginRow(login.Provider, string.Join(" · ", new[] { login.Email ?? login.DisplayName, login.LinkedAt?.ToLocalTime().ToString("g") }.OfType<string>())));
            foreach (var provider in _auth.Providers.Where(p => p.Name != "password" && Logins.All(l => l.Provider != p.Name)))
                Linkable.Add(provider.Name);
        }

        Picture = await _auth.GetPictureAsync() is { } picture ? ImageSource.FromStream(() => new MemoryStream(picture)) : null;
    });

    [RelayCommand]
    private async Task Link(string provider)
    {
        var result = await _auth.LinkAsync(provider);
        LinkLog = result.Status == SignInStatus.SignedIn ? $"{provider} added" : $"{provider}: {result.Status} {result.Error}";
    }

    [RelayCommand]
    private async Task Unlink(string provider)
    {
        var result = await _auth.UnlinkAsync(provider);
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
