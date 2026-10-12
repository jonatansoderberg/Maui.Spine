using Microsoft.Extensions.AI;
using Xunit;

namespace Plugin.Maui.Spine.Controls.Tests;

public class ChatAnswerTests
{
    private static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text);

    private static ChatResponseUpdate Of(params AIContent[] contents) => new() { Role = ChatRole.Assistant, Contents = [.. contents] };

    [Fact]
    public void Text_pieces_add_up_and_say_the_text_changed()
    {
        var answer = new ChatAnswer();

        Assert.False(answer.HasContent);
        Assert.Equal((true, false), answer.Add(Text("Hej ")));
        Assert.Equal((true, false), answer.Add(Text("**då**.")));

        Assert.Equal("Hej **då**.", answer.Text);
        Assert.True(answer.HasContent);
    }

    [Fact]
    public void An_update_with_only_usage_changes_nothing_visible()
    {
        var answer = new ChatAnswer();
        answer.Add(Text("Hej"));

        Assert.Equal((false, false), answer.Add(Of(new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 3 }))));
        Assert.Equal(10, answer.ToResponse().Usage?.InputTokenCount);
    }

    [Fact]
    public void A_call_and_its_result_are_one_tool_card()
    {
        var answer = new ChatAnswer();

        answer.Add(Of(new FunctionCallContent("c1", "get_results", new Dictionary<string, object?> { ["class"] = "D21" })));
        var running = Assert.Single(answer.ToolCalls);
        Assert.Equal(("get_results", false), (running.Name, running.IsDone));
        Assert.Equal("D21", running.Arguments?["class"]);

        Assert.Equal((false, true), answer.Add(new ChatResponseUpdate { Role = ChatRole.Tool, Contents = [new FunctionResultContent("c1", "5th of 38")] }));
        var done = Assert.Single(answer.ToolCalls);
        Assert.Equal(("get_results", true, (object?)"5th of 38"), (done.Name, done.IsDone, done.Result));
    }

    [Fact]
    public void Several_calls_keep_their_order_and_a_result_without_a_call_still_shows()
    {
        var answer = new ChatAnswer();

        answer.Add(Of(new FunctionCallContent("a", "first"), new FunctionCallContent("b", "second")));
        answer.Add(Of(new FunctionResultContent("b", "ok")));
        answer.Add(Of(new FunctionResultContent("z", "orphan")));

        Assert.Equal(["first", "second", ""], answer.ToolCalls.Select(c => c.Name));
        Assert.Equal([false, true, true], answer.ToolCalls.Select(c => c.IsDone));
    }

    [Fact]
    public void The_response_folds_the_updates_into_messages()
    {
        var answer = new ChatAnswer();
        answer.Add(new ChatResponseUpdate(ChatRole.Assistant, "Hej ") { MessageId = "m1" });
        answer.Add(new ChatResponseUpdate(ChatRole.Assistant, "då.") { MessageId = "m1" });

        var message = Assert.Single(answer.ToResponse().Messages);
        Assert.Equal(ChatRole.Assistant, message.Role);
        Assert.Equal("Hej då.", message.Text);
    }
}
