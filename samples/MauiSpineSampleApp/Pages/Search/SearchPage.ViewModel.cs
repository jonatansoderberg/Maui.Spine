namespace MauiSpineSampleApp.Pages.Search;

public partial class SearchPageViewModel : SampleViewModel
{
    private readonly ISearchIndex _index;
    private readonly INavigationService _navigation;

    public SearchPageViewModel(ISearchIndex index, INavigationService navigation)
    {
        _index = index;
        _navigation = navigation;
    }

    public override Task OnAppearingAsync(NavigationDirection navigationDirection) => RefreshAsync();

    public string Where => DeviceInfo.Platform == DevicePlatform.Android ? "Android lists the rooms as shortcuts of the app; whether the launcher's search shows them depends on the launcher."
        : DeviceInfo.Platform == DevicePlatform.iOS ? "Swipe down on the Home Screen and type “Kitchen”."
        : DeviceInfo.Platform == DevicePlatform.MacCatalyst ? "Press ⌘ Space and type “Kitchen”."
        : "This platform has no system search: the calls do nothing.";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [RelayCommand]
    private async Task Index()
    {
        await _index.UpsertAsync(Room.All.Select(r => r.ToSearchable()));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RemoveAll()
    {
        await _index.RemoveAllAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private Task Open(string id) => _navigation.NavigateToAsync<RoomPage, RoomId>(new RoomId(id));

    private async Task RefreshAsync()
    {
        if (!_index.IsSupported)
        {
            Status = "Not supported on this platform.";
            return;
        }

        var ids = await _index.GetIdsAsync();
        var rooms = ids.Count(id => id.StartsWith("room-", StringComparison.Ordinal));
        Status = $"In the index: {rooms} of {Room.All.Count} rooms, and {ids.Count - rooms} [Searchable] pages.";
    }
}
