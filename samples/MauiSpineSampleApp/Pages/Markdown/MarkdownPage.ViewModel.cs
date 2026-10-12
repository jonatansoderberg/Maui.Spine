namespace MauiSpineSampleApp.Pages.Markdown;

public partial class MarkdownPageViewModel : SampleViewModel
{
    public const string Everything = """
        # Race report

        You started at **10:02** and finished in *31:02*, which put you ~~sixth~~ **fifth** in the class.
        The [results](https://eventor.orientering.se) are up, and `D21` had 38 starters.

        ## Splits

        | Control | Split | Place |
        |:--|:-:|--:|
        | 1 | 2:41 | 3 |
        | 4 | **6:15** | 19 |
        | Finish | 0:22 | 1 |

        ### What to try next time

        1. Read the map before the start
        2. Take the safe route to control 4
           - along the path
           - then on the bearing
        3. Rest

        - [x] Run the race
        - [ ] Analyse the splits
        - [ ] Book the next one

        > Control 4 cost about two minutes.
        > The estimate assumes the straight line was runnable.

        ```csharp
        var lost = split - best;   // 2:13
        Console.WriteLine($"Lost {lost:m\:ss} at control 4");
        ```

        ---

        ![The Spine logo](https://raw.githubusercontent.com/jonatansoderberg/Maui.Spine/master/assets/icons/spine.png)

        Questions? Mail <race@example.com>.
        """;

    public const string ReleaseNotes = """
        ## Version 2.4

        - **New:** answers stream in as they are written
        - **Faster:** only the last block of an answer is redrawn
        - Fixed a crash when the clock changed

        See the [full notes](https://github.com/jonatansoderberg/Maui.Spine/releases).
        """;

    private CancellationTokenSource? _streaming;

    [ObservableProperty]
    public partial string Streamed { get; set; } = "";

    [ObservableProperty]
    public partial string Rebuilt { get; set; } = "";

    [ObservableProperty]
    public partial string LastLink { get; set; } = "Tap a link: it is reported here, then opens in the browser.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StreamCommand))]
    public partial bool IsStreaming { get; set; }

    public string Document => Everything;

    public string Notes => ReleaseNotes;

    // A few characters every 30 ms, the pace of a model's answer.
    [RelayCommand(CanExecute = nameof(CanStream))]
    private async Task Stream()
    {
        _streaming?.Cancel();
        var streaming = _streaming = new CancellationTokenSource();
        IsStreaming = true;
        Streamed = "";

        try
        {
            for (var at = 0; at < Everything.Length && !streaming.IsCancellationRequested; at += 4)
            {
                Streamed = Everything[..Math.Min(at + 4, Everything.Length)];
                await Task.Delay(30, streaming.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsStreaming = false;
        }
    }

    private bool CanStream() => !IsStreaming;
}
