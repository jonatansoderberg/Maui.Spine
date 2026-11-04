using System.Text;
using System.Text.Json.Nodes;
using Plugin.Maui.Spine.Common;

namespace Plugin.Maui.Spine.Layouts.Tests;

/// <summary>A layout file's template, filled in with its data, is the layout Build writes: in every case.</summary>
public class TemplateTests(Xunit.Abstractions.ITestOutputHelper output)
{
    static void Same(LiveActivityLayout built, LiveActivityTemplate template, LiveActivityTemplateData data)
    {
        var expected = JsonNode.Parse(built.ToJson());
        var actual = JsonNode.Parse(template.Render(data));
        Assert.True(JsonNode.DeepEquals(expected, actual), $"expected {expected!.ToJsonString()}\n  actual {actual!.ToJsonString()}");
    }

    [Theory]
    [MemberData(nameof(GlucoseLayoutTests.Cases), MemberType = typeof(GlucoseLayoutTests))]
    public void The_reading_as_template_and_data_is_the_tree(string name)
    {
        var model = GlucoseLayoutTests.Case(name);
        Same(GlucoseLayout.Build(model), GlucoseLayout.Template, GlucoseLayout.Data(model));
        Same(GlucoseNoValueLayout.Build(model), GlucoseNoValueLayout.Template, GlucoseNoValueLayout.Data(model));
    }

    [Fact]
    public void A_list_with_and_without_items()
    {
        var model = new ScoreModel();
        Same(ScoreLayout.Build(model), ScoreLayout.Template, ScoreLayout.Data(model));
        model.Goals.Add("1–0 Ek 4'");
        model.Goals.Add("1–1 Berg 9'");
        model.Game = new Game { HomeGoals = 1, AwayGoals = 1 };
        Same(ScoreLayout.Build(model), ScoreLayout.Template, ScoreLayout.Data(model));
    }

    [Fact]
    public void The_template_is_the_same_every_time_and_named_by_its_content()
    {
        Assert.Matches(@"^glucose@[0-9a-f]{8}$", GlucoseLayout.Template.Id);
        Assert.Same(GlucoseLayout.Template, GlucoseLayout.Template);
        Assert.NotEqual(GlucoseLayout.Template.Id, GlucoseNoValueLayout.Template.Id);
        Assert.DoesNotContain("Alexander", GlucoseLayout.Template.Json);
    }

    [Fact]
    public void A_push_of_the_data_is_a_fraction_of_the_tree()
    {
        var model = GlucoseLayoutTests.Case("current");
        var tree = Encoding.UTF8.GetByteCount(GlucoseLayout.Build(model).ToJson());
        var data = Encoding.UTF8.GetByteCount(GlucoseLayout.Data(model).ToJson());
        var withoutGraph = Encoding.UTF8.GetByteCount(GlucoseLayout.Data(model with { LastHour = [] }).ToJson());
        output.WriteLine($"tree {tree} B, data {data} B, data without the graph {withoutGraph} B, template {Encoding.UTF8.GetByteCount(GlucoseLayout.Template.Json)} B");
        output.WriteLine(GlucoseLayout.Data(model).ToJson());
        Assert.True(data * 3 < tree, $"{data} bytes of data against {tree} of tree");
    }

    [Fact]
    public void Data_for_another_template_is_refused() =>
        Assert.Throws<ArgumentException>(() => GlucoseLayout.Template.Render(GlucoseNoValueLayout.Data(GlucoseLayoutTests.Case("current"))));
}
