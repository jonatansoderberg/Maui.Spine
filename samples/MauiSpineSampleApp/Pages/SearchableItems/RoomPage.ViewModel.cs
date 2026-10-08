namespace MauiSpineSampleApp.Pages.SearchableItems;

public partial class RoomPageViewModel : SampleViewModel, IReceivesNavigationParameter<RoomId>
{
    [ObservableProperty]
    public partial string Icon { get; set; } = "house.svg";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial string OpenedAt { get; set; } = "";

    public Task OnNavigationParameterAsync(RoomId param)
    {
        var room = Room.Find(param);
        Title = room?.Name ?? $"No room \"{param.Value}\"";
        Icon = $"{room?.Icon ?? "house"}.svg";
        Summary = room?.Summary ?? "";
        OpenedAt = $"Opened with RoomId(\"{param.Value}\") at {DateTime.Now:HH:mm:ss}";
        return Task.CompletedTask;
    }
}
