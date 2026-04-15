namespace MauiSpineSampleApp.Pages;

public partial class PersonDetailPageViewModel : ViewModelBase, IReceivesNavigationParameter<PersonData>
{
    [ObservableProperty] public partial string? Name { get; set; }
    [ObservableProperty] public partial string? Email { get; set; }
    [ObservableProperty] public partial int Age { get; set; }

    public Task OnNavigationParameterAsync(PersonData param)
    {
        Name = param.Name;
        Email = param.Email;
        Age = param.Age;
        return Task.CompletedTask;
    }
}
