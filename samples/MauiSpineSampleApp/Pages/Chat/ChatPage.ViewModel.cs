using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace MauiSpineSampleApp.Pages.Chat;

public partial class ChatPageViewModel : SampleViewModel
{
    private readonly IChatClient _demo = new DemoChatClient();
    private readonly IChatClient _assistant;

    public ChatPageViewModel()
    {
        // Spine.AI's Assistant from the AI client page: the device first, then the push sample server.
        _assistant = IPlatformApplication.Current!.Services.GetRequiredKeyedService<IChatClient>("Assistant");
        Client = _demo;
        PageActions.Add(new PageAction("New chat", NewChatCommand));
    }

    /// <summary>The conversation; the view adds to it, and clearing it starts over.</summary>
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public IReadOnlyList<string> Suggestions { get; } = ["Summarise my race", "Show the splits as a table"];

    [ObservableProperty]
    public partial IChatClient Client { get; set; }

    [ObservableProperty]
    public partial bool UseAssistant { get; set; }

    partial void OnUseAssistantChanged(bool value) => Client = value ? _assistant : _demo;

    [RelayCommand]
    private void NewChat() => Messages.Clear();
}
