namespace MauiSpineSampleApp.Pages.Marquee;

public partial class MarqueePageViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string DynamicText { get; set; } = "Hello, World!";

    private static readonly string[] _latinWords =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua",
    ];

    private static readonly Random _random = new();

    [RelayCommand]
    private void ChangeDynamicText()
    {
        var words = Enumerable.Range(0, _random.Next(3, 20))
            .Select(_ => _latinWords[_random.Next(_latinWords.Length)])
            .ToList();

        words[0] = char.ToUpper(words[0][0]) + words[0][1..];
        DynamicText = string.Join(" ", words) + ".";
    }
}
