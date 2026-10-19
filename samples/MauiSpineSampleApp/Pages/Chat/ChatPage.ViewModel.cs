using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using MauiSpineSampleApp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Maui.Spine.AI;

namespace MauiSpineSampleApp.Pages.Chat;

public partial class ChatPageViewModel : SampleViewModel
{
    private readonly IReadOnlyList<ObservableObject> _options;

    public ChatPageViewModel()
    {
        var services = IPlatformApplication.Current!.Services;

        // The in-app demo, Spine.AI's Assistant (the device first, then the push sample server) and the server's alone.
        // The server's Assistant has two functions: get_my_bookings runs there with the signed-in user, and show_time
        // runs on the phone or on the server, as this chat's options say.
        IChatClient demo = new DemoChatClient();
        var assistant = services.GetRequiredKeyedService<IChatClient>("Assistant");
        var server = services.GetRequiredKeyedService<IChatClient>("ServerAssistant");
        Client = demo;

        var model = new ChoiceGroup("Model", [
            new Choice("Demo", "Canned answers in the app, with a tool-call card.", () => Use(demo, "Demo answers in the app")),
            new Choice("Assistant", "Spine.AI's Assistant: Apple Intelligence on the device when it has it, else the push sample server.", () => Use(assistant, "Spine.AI's Assistant: the device first, then the server")),
            new Choice("Server", "The push sample server's Assistant. Ask \"Vilka bokningar har jag?\" signed in and signed out, or \"Vad är klockan?\".", () => Use(server, "The server's Assistant: ask about your bookings or the time")),
        ]);
        model.Select(0);
        Use(demo, "Demo answers in the app");

        _options = [
            model,
            new ToggleOption("Read the clock on the server", "show_time is on both sides; the chat's options choose the server instead of the phone (ChatOptions.Prefer).", () => ClockOnServer, value => ClockOnServer = value),
            new ToggleOption("Show function calls", "A card for each function the model called (ChatView.ShowToolCalls), for debugging; the model tells the user the result anyway.", () => ShowToolCalls, value => ShowToolCalls = value),
        ];

        // The header bar has one trailing slot; the chat's menu takes it from the base class's theme menu.
        PageActions.Remove(PageActions.Single(a => a.Menu == ThemeMenu));
        PageActions.Add(new PageAction(null, new MenuItems
        {
            new MenuAction("Options", null, ShowChatOptionsCommand),
            new MenuAction("New chat", null, NewChatCommand),
        }) { Svg = "more.svg", Description = "Chat" });
    }

    /// <summary>The conversation; the view adds to it, and clearing it starts over.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public IReadOnlyList<string> Suggestions { get; } = ["Vilka bokningar har jag?", "Vad är klockan?", "Summarise my race"];

    [ObservableProperty]
    public partial IChatClient Client { get; set; }

    [ObservableProperty]
    public partial string Using { get; set; } = "";

    /// <summary>The chat's options: where show_time runs.</summary>
    [ObservableProperty]
    public partial ChatOptions Options { get; set; } = new ChatOptions().Prefer(SampleFunctions.ShowTime.Name, SpineAIFunctionSide.Device);

    [ObservableProperty]
    public partial bool ClockOnServer { get; set; }

    partial void OnClockOnServerChanged(bool value) =>
        Options = new ChatOptions().Prefer(SampleFunctions.ShowTime.Name, value ? SpineAIFunctionSide.Server : SpineAIFunctionSide.Device);

    [ObservableProperty]
    public partial bool ShowToolCalls { get; set; } = true;

    private void Use(IChatClient client, string label)
    {
        Client = client;
        Using = label;
    }

    [RelayCommand]
    private Task ShowChatOptions() => ShowOptionsAsync("Chat", _options);

    [RelayCommand]
    private void NewChat() => Messages.Clear();
}
