namespace MauiSpineSampleApp.Pages.Guards;

/// <summary>
/// The sample's own guard: asks the user to confirm before a page opens or a command runs. A real app
/// takes its guard from a package, such as an unlock from Plugin.Maui.Spine.Biometrics.
/// </summary>
public sealed class ConfirmAttribute(string question) : GuardAttribute
{
    public string Question { get; } = question;
}

public sealed class ConfirmGuard : IGuard<ConfirmAttribute>
{
    public async ValueTask<bool> CheckAsync(ConfirmAttribute attribute, GuardContext context, CancellationToken cancellationToken)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } page)
            return false;

        return await page.DisplayAlertAsync("Guard", attribute.Question, "Continue", "Cancel");
    }
}
