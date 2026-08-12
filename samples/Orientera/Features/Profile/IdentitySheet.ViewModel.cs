using Orientera.Presentation;
using Orientera.Services.Eventor;
using Orientera.Services.Local;
using Orientera.Services.Sources;

namespace Orientera.Features.Profile;

/// <summary>
/// Name, club and class — what the live and result lists identify a runner by, and therefore
/// everything the app needs to find the user in them.
/// </summary>
/// <remarks>
/// Not a sign-up. Nothing leaves the phone, and the app works without it: until it is filled
/// in, the seeded demo runner stands in.
///
/// Once the user has logged in to Eventor, the name and the club are Eventor's answer and are shown
/// rather than asked for (#123). The class is still asked for — Eventor has a preferred class, but
/// people enter others, and the app's job is to find them in a start list, not to correct them.
/// </remarks>
public partial class IdentitySheetViewModel(
    INavigationService _navigation,
    IPeopleSource _people,
    EventorReader _eventor,
    LocalIdentityStore _identity) : OrienteraViewModel
{
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Club { get; set; } = string.Empty;
    [ObservableProperty] public partial string DefaultClass { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CanSave { get; set; }

    /// <summary>Whether Eventor owns the name and the club, and this sheet only asks for the class.</summary>
    [ObservableProperty] public partial bool IsEventorOwned { get; set; }

    [ObservableProperty] public partial bool CanEditIdentity { get; set; } = true;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection) =>
        LoadAsync(async () =>
        {
            var me = _identity.Current is { } current
                ? new LocalIdentity { Name = current.Name, Club = current.Club, DefaultClass = current.DefaultClass }
                : Of(await _people.GetMeAsync());

            Name = me.Name;
            Club = me.Club;
            DefaultClass = me.DefaultClass;

            IsEventorOwned = await _eventor.AccessAsync()
                is EventorAccess.Available or EventorAccess.NoSubscription or EventorAccess.Unreachable;
            CanEditIdentity = !IsEventorOwned;

            UpdateCanSave();
        });

    partial void OnNameChanged(string value) => UpdateCanSave();

    partial void OnClubChanged(string value) => UpdateCanSave();

    [RelayCommand]
    private async Task Save()
    {
        if (!CanSave)
            return;

        _identity.Save(new LocalIdentity
        {
            Name = Name.Trim(),
            Club = Club.Trim(),
            DefaultClass = DefaultClass.Trim(),
        });

        await _navigation.BackAsync();
    }

    private static LocalIdentity Of(Domain.Person person) => new()
    {
        Name = person.Name,
        Club = person.Club,
        DefaultClass = person.DefaultClass,
    };

    /// <summary>A name without a club matches too many people to be worth storing.</summary>
    private void UpdateCanSave() =>
        CanSave = !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Club);
}
