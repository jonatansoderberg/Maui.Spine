using SpineAvatarLab.Lab;

namespace SpineAvatarLab;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(new LabPage())) { Title = "Avatar Lab", Width = 1100, Height = 900 };
}
