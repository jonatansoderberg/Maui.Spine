using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.UI.Text;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    static void ConfigureTypography()
    {
        foreach (var key in new[] { Text.FontFeaturesMapperKey, nameof(ITextStyle.Font), nameof(ILabel.Text) })
            LabelHandler.Mapper.AppendToMapping(key, ApplyFontFeatures);
    }

    // WinUI exposes features as Typography attached properties; the tags it has no property for
    // are ignored. TrimToCapHeight has no metric to work from on TextBlock and is a no-op here.
    static void ApplyFontFeatures(IElementHandler handler, IElement element)
    {
        if (element is not Label label || handler.PlatformView is not TextBlock textBlock)
            return;

        foreach (var tag in Text.ParseTags(Text.GetFontFeatures(label)))
        {
            switch (tag)
            {
                case "tnum": Typography.SetNumeralAlignment(textBlock, FontNumeralAlignment.Tabular); break;
                case "pnum": Typography.SetNumeralAlignment(textBlock, FontNumeralAlignment.Proportional); break;
                case "lnum": Typography.SetNumeralStyle(textBlock, FontNumeralStyle.Lining); break;
                case "onum": Typography.SetNumeralStyle(textBlock, FontNumeralStyle.OldStyle); break;
                case "smcp": Typography.SetCapitals(textBlock, FontCapitals.SmallCaps); break;
                case "kern": Typography.SetKerning(textBlock, true); break;
                case "liga": Typography.SetStandardLigatures(textBlock, true); break;
                case "ss01": Typography.SetStylisticSet1(textBlock, true); break;
                case "ss02": Typography.SetStylisticSet2(textBlock, true); break;
                case "ss03": Typography.SetStylisticSet3(textBlock, true); break;
                case "ss04": Typography.SetStylisticSet4(textBlock, true); break;
                case "ss05": Typography.SetStylisticSet5(textBlock, true); break;
            }
        }
    }
}
