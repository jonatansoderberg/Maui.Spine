using System.Collections.ObjectModel;
using Orientera.Services.Sources;

namespace Orientera.Features.Events;

public partial class ChooseClassSheetViewModel(
    INavigationService _navigation,
    IPeopleSource _people) : ViewModelBase
{
    public ObservableCollection<string> Classes { get; } = [];

    [ObservableProperty]
    public partial string MyClass { get; set; } = string.Empty;

    public override async Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        if (Classes.Count > 0)
            return;

        var me = await _people.GetMeAsync();
        MyClass = me.DefaultClass;

        foreach (var className in new[] { "D21", "D35", "D45", "D16", "D14", "Öppen 5", "Öppen 3" })
            Classes.Add(className);
    }

    [RelayCommand]
    private async Task Choose(string className) => await _navigation.ReturnAsync(className);
}
