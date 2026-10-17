namespace MauiSpineSampleApp.Pages.Assistant;

[NavigableRegion(Title = "Assistant")]
public partial class AssistantPage
{
    public AssistantPage() => InitializeComponent();

    // A held button has no command for its release; push-to-talk needs both ends.
    private void OnTalkPressed(object? sender, EventArgs e) => (BindingContext as AssistantPageViewModel)?.BeginTurnCommand.Execute(null);

    private void OnTalkReleased(object? sender, EventArgs e) => (BindingContext as AssistantPageViewModel)?.EndTurnCommand.Execute(null);
}
