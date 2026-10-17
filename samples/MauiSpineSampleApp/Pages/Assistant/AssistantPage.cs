namespace MauiSpineSampleApp.Pages.Assistant;

[NavigableRegion(Title = "Assistant")]
public partial class AssistantPage
{
    public AssistantPage()
    {
        InitializeComponent();
        // The chat's events drive the avatar in text mode.
        TextChat.Sent += (_, _) => (BindingContext as AssistantPageViewModel)?.OnSent();
        TextChat.Streamed += (_, e) => (BindingContext as AssistantPageViewModel)?.OnStreamed(e);
    }

    // A held button has no command for its release; push-to-talk needs both ends.
    private void OnTalkPressed(object? sender, EventArgs e) => (BindingContext as AssistantPageViewModel)?.BeginTurnCommand.Execute(null);

    private void OnTalkReleased(object? sender, EventArgs e) => (BindingContext as AssistantPageViewModel)?.EndTurnCommand.Execute(null);
}
