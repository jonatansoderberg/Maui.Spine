namespace MauiSpineSampleApp.Pages.Avatars;

[NavigableRegion(Title = "Avatar")]
public partial class AvatarDetailPage : INavigableWithParameter<AvatarEntry>
{
    public AvatarDetailPage()
    {
        InitializeComponent();
        // Gestures, timed expressions and Interrupt are the view's methods, so the view model gets the view.
        Attach();
        BindingContextChanged += (_, _) => Attach();
    }

    // SpinePage sets the view model before InitializeComponent, so it is there already; a later one is attached too.
    private void Attach() => (BindingContext as AvatarDetailPageViewModel)?.Attach(Avatar);
}
