using Orientera.Presentation;

namespace Orientera.Tests;

/// <summary>
/// A result page lists every class in the competition. Alphabetically that is forty class names
/// in no order a runner recognises; these pin the two orders that are recognisable.
/// </summary>
public class ClassOrderTests
{
    private static IReadOnlyList<string> Sorted(ClassOrder order, params string[] classes) =>
        [.. classes.OrderBy(order.Rank)];

    /// <summary>
    /// The three groups, in the order a result list is scanned: the main classes, the youth, and
    /// then the courses anyone may enter.
    /// </summary>
    [Fact]
    public void Main_classes_then_youth_then_open()
    {
        var order = ClassOrder.For([]);

        Assert.Equal(
            ["D21", "H45", "D16", "H16", "U1", "Blå 3,0", "Gubbar", "Öppen 5"],
            Sorted(order, "Öppen 5", "Gubbar", "D16", "U1", "H45", "Blå 3,0", "H16", "D21"));
    }

    /// <summary>The organiser's own list is the order on the entry form and on Eventor.</summary>
    [Fact]
    public void The_organiser_decides_the_order_inside_a_group()
    {
        var order = ClassOrder.For(["H21", "D21", "H45", "Öppen 5"]);

        Assert.Equal(
            ["H21", "D21", "H45", "Öppen 5"],
            Sorted(order, "Öppen 5", "H45", "H21", "D21"));
    }

    /// <summary>Twenty is the last youth year; twenty-one is where the main classes start.</summary>
    [Theory]
    [InlineData("D20", "D21")]
    [InlineData("H20", "H21")]
    [InlineData("HD12", "H35")]
    public void The_age_decides_which_group(string youth, string main)
    {
        var order = ClassOrder.For([]);

        Assert.Equal([main, youth], Sorted(order, youth, main));
    }

    /// <summary>
    /// And where their list does not reach, the name itself decides: letters, then the number.
    /// </summary>
    [Fact]
    public void What_the_list_does_not_name_is_read_as_letters_and_a_number()
    {
        var order = ClassOrder.For([]);

        Assert.Equal(
            ["D21", "D35", "H21", "D10", "D18", "H10"],
            Sorted(order, "H21", "D21", "D35", "D10", "H10", "D18"));
    }

    /// <summary>A number read as text put D2 next to D21 and D10 above D9.</summary>
    [Fact]
    public void A_class_number_is_a_number()
    {
        var order = ClassOrder.For([]);

        Assert.Equal(["D9", "D10", "D18", "D20"], Sorted(order, "D18", "D10", "D20", "D9"));
        Assert.Equal(["D21", "D35", "D100"], Sorted(order, "D100", "D35", "D21"));
    }

    /// <summary>
    /// A multi-day event names each stage inside the class. The class is what stands before the
    /// comma, and all five stages belong where their class does.
    /// </summary>
    [Fact]
    public void Every_stage_of_a_class_sits_where_the_class_sits()
    {
        var order = ClassOrder.For(["H45", "H50"]);

        Assert.Equal(
            ["H45, Etapp 1", "H45, Etapp 2", "H50, Etapp 1"],
            Sorted(order, "H50, Etapp 1", "H45, Etapp 2", "H45, Etapp 1")
                .OrderBy(order.Rank)
                .ThenBy(name => name, StringComparer.CurrentCulture)
                .ToList());
    }
}
