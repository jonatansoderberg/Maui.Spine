using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.AI;

namespace MauiSpineSampleApp.Pages.AIClient;

public partial class AIClientPageViewModel : SampleViewModel
{
    private readonly IChatClient _assistant;
    private readonly IChatClient _serverAssistant;
    private readonly IAIUsage _usage;
    private CancellationTokenSource? _running;

    public AIClientPageViewModel()
    {
        var services = IPlatformApplication.Current!.Services;

        // Declared in MauiProgram with UseSpineAI: the device first, then the push sample server's "Assistant".
        _assistant = services.GetRequiredKeyedService<IChatClient>("Assistant");
        _serverAssistant = services.GetRequiredKeyedService<IChatClient>("ServerAssistant");
        _usage = services.GetRequiredService<IAIUsage>();

        // "Story" runs on Anthropic on the sample server, which is off until it has a key: the off answer is
        // IEmptyResultException, so the state shows its empty view with the reason instead of an error and a retry.
        var story = services.GetRequiredKeyedService<IChatClient>("Story");
        Story = new TaskState<string>(async ct => (await story.GetResponseAsync("Skriv två meningar om ett orienteringslopp.", cancellationToken: ct)).Text);

        RefreshUsage();
    }

    public TaskState<string> Story { get; }

    [ObservableProperty]
    public partial string Prompt { get; set; } = "Skriv en haiku om orientering i höstskog.";

    [ObservableProperty]
    public partial bool DeviceFirst { get; set; } = true;

    [ObservableProperty]
    public partial string Answer { get; set; } = "";

    [ObservableProperty]
    public partial string? AnsweredBy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string DeviceUsage { get; set; } = "";

    [RelayCommand]
    private async Task Send()
    {
        _running?.Cancel();
        using var running = _running = new CancellationTokenSource();

        Answer = "";
        AnsweredBy = null;
        Problem = null;
        IsRunning = true;

        try
        {
            var client = DeviceFirst ? _assistant : _serverAssistant;
            await foreach (var update in client.GetStreamingResponseAsync(Prompt, cancellationToken: running.Token))
            {
                AnsweredBy ??= update.AnsweredBy() switch
                {
                    AIAnsweredBy.Device => "Answered on the device",
                    AIAnsweredBy.Server => "Answered by the server",
                    _ => null,
                };
                Answer += update.Text;
            }
        }
        catch (OperationCanceledException) when (running.IsCancellationRequested)
        {
            Problem = "Stopped.";
        }
        catch (AIFeatureOffException e)
        {
            Problem = $"The feature is off: {e.Reason}.";
        }
        catch (AIRateLimitedException e)
        {
            Problem = $"Too many calls; try again in {e.RetryAfter?.TotalSeconds ?? 60:0} s.";
        }
        catch (Exception e)
        {
            Problem = $"{e.GetType().Name}: {e.Message}";
        }
        finally
        {
            if (_running == running) _running = null;
            IsRunning = false;
            RefreshUsage();
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() => _running?.Cancel();

    private void RefreshUsage()
    {
        var today = _usage.Today("Assistant");
        DeviceUsage = $"On this device today: {today.Calls} calls, {today.InputTokens} tokens in, {today.OutputTokens} out";
    }
}
