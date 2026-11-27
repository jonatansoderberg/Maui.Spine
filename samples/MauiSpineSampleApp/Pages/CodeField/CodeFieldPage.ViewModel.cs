namespace MauiSpineSampleApp.Pages.Codes;

public partial class CodeFieldPageViewModel : SampleViewModel
{
    [ObservableProperty]
    public partial string OneTimeCode { get; set; } = "";

    [ObservableProperty]
    public partial string OneTimeText { get; set; } = "Six digits, sent when the last one is typed.";

    [ObservableProperty]
    public partial string GiftCode { get; set; } = "";

    [ObservableProperty]
    public partial CodeFieldState GiftState { get; set; }

    [ObservableProperty]
    public partial string GiftText { get; set; } = "Type a code; GOAL-2026-HM00 is the right one. O counts as 0, I and L as 1.";

    [ObservableProperty]
    public partial bool GiftTyping { get; set; }

    [RelayCommand]
    private void OneTimeCompleted(string code) => OneTimeText = $"Sent {code}.";

    // The server's answer, made up: the cells pulse while it is "checked", then light up or shake.
    [RelayCommand]
    private async Task GiftCompleted(string code)
    {
        GiftState = CodeFieldState.Checking;
        GiftText = "Checking…";
        await Task.Delay(1200);
        var right = code == "G0A12026HM00";
        GiftState = right ? CodeFieldState.Accepted : CodeFieldState.Rejected;
        GiftText = right ? "Accepted." : "Unknown code. Change a character and it is sent again.";
    }

    partial void OnGiftCodeChanged(string value)
    {
        if (GiftState == CodeFieldState.Rejected)
            GiftState = CodeFieldState.Editing;
    }

    // As a scan or a link would: the cells fill one after the other, then the code is sent.
    [RelayCommand]
    private async Task FillGift()
    {
        GiftTyping = false;
        GiftState = CodeFieldState.Editing;
        GiftCode = "";
        GiftCode = "GOAL2026HM00";
        await Task.Delay(550);
        await GiftCompleted(GiftCode);
    }

    [RelayCommand]
    private void ClearGift()
    {
        GiftState = CodeFieldState.Editing;
        GiftCode = "";
        GiftText = "Type a code; GOAL-2026-HM00 is the right one. O counts as 0, I and L as 1.";
        GiftTyping = false;
        GiftTyping = true;
    }
}
