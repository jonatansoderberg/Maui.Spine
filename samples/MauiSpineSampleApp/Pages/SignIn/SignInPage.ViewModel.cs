using Plugin.Maui.Spine.Authentication;

namespace MauiSpineSampleApp.Pages.SignIn;

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

    private void Update() => MainThread.BeginInvokeOnMainThread(() =>
        Status = _auth.User is { } user ? $"Signed in as {user.DisplayName ?? user.Email} with {user.Provider}" : "Signed out");

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
