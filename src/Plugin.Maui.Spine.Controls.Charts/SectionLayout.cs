namespace Plugin.Maui.Spine.Controls;

/// <summary>A section's name over a scrolling chart: where it begins, in points from the plot's leading edge, and its name's widths.</summary>
internal readonly record struct ChartSection(float Offset, float LongWidth, float ShortWidth);

/// <summary>Where a section's name is drawn: from its left edge, long or short, and how strongly.</summary>
internal readonly record struct PlacedSection(int Index, float X, bool Short, float Opacity);

/// <summary>
/// Where the sections' names over a <see cref="ChartScroller"/> go, as the Home app's forecast chart places its days:
/// a name stands a little inside where its section begins; the section at the leading edge keeps its name there
/// (also while the chart is pulled past its start); when the next name leaves no room it is first shortened, then
/// pushed out by that name, fading; a name reaching in at the trailing edge fades in. Two names never overlap. Free of
/// MAUI so it can be tested.
/// </summary>
internal static class SectionLayout
{
    public static List<PlacedSection> Place(IReadOnlyList<ChartSection> sections, float width, float gap, float inset)
    {
        var placed = new List<PlacedSection>();
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            if (section.Offset >= width)
                break;
            var next = i + 1 < sections.Count ? sections[i + 1].Offset + inset : float.PositiveInfinity;
            var x = Math.Max(section.Offset + inset, 0);
            var room = next - gap - x;
            var isShort = false;
            var own = section.LongWidth;
            if (room < own)
            {
                isShort = true;
                own = section.ShortWidth;
            }
            var opacity = 1f;
            if (room < own)
            {
                x = next - gap - own;
                opacity = Math.Clamp(1 + x / own, 0, 1);
            }
            else if (x + own > width)
            {
                opacity = Math.Clamp((width - x) / own, 0, 1);
            }
            if (opacity > 0.02f)
                placed.Add(new PlacedSection(i, x, isShort, opacity));
        }
        return placed;
    }
}
