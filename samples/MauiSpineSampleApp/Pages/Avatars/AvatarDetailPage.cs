namespace MauiSpineSampleApp.Pages.Avatars;

[NavigableRegion(Title = "Avatar")]
public partial class AvatarDetailPage : INavigableWithParameter<AvatarEntry>
{
    public AvatarDetailPage()
    {
        InitializeComponent();
        // Gestures, timed expressions and Interrupt are the view's methods, so the view model gets the view.
        BindingContextChanged += (_, _) => (BindingContext as AvatarDetailPageViewModel)?.Attach(Avatar);
    }
}
