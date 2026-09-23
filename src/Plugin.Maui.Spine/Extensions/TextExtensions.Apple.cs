#if IOS || MACCATALYST

using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace Plugin.Maui.Spine.Extensions;

public static partial class SpineExtensions
{
    // Core Text's OpenType feature form (iOS 13+): any four-letter tag with an on/off value,
    // which covers tnum as well as stylistic sets without mapping to Apple's own selectors.
    static readonly NSString FeatureSettingsKey = new("NSCTFontFeatureSettingsAttribute");
    static readonly NSString OpenTypeTagKey = new("CTFeatureOpenTypeTag");
    static readonly NSString OpenTypeValueKey = new("CTFeatureOpenTypeValue");

    static void ConfigureTypography()
    {
        // MAUI rebuilds the UIFont on these; the features have to go back on afterwards.
        foreach (var key in new[] { Text.FontFeaturesMapperKey, nameof(ITextStyle.Font), nameof(ILabel.Text), nameof(ITextStyle.CharacterSpacing) })
            LabelHandler.Mapper.AppendToMapping(key, ApplyFontFeatures);

        foreach (var key in new[] { Text.TrimToCapHeightMapperKey, nameof(ITextStyle.Font) })
            LabelHandler.Mapper.AppendToMapping(key, ApplyCapHeightTrim);
    }

    static void ApplyFontFeatures(IElementHandler handler, IElement element)
    {
        if (element is not Label label || handler.PlatformView is not UILabel platformLabel || platformLabel.Font is not { } font)
            return;

        var features = Text.ParseTags(Text.GetFontFeatures(label))
            .Select(tag => (NSObject)new NSDictionary(OpenTypeTagKey, new NSString(tag), OpenTypeValueKey, NSNumber.FromInt32(1)))
            .ToArray();

        if (features.Length == 0)
            return;

        // CreateWithAttributes replaces the feature-settings attribute on the label's own
        // descriptor, so family, size and weight stay and repeated application never stacks.
        var attributes = new NSDictionary(FeatureSettingsKey, NSArray.FromNSObjects(features));
        platformLabel.Font = UIFont.FromDescriptor(font.FontDescriptor.CreateWithAttributes(attributes), 0);
    }

    static void ApplyCapHeightTrim(IElementHandler handler, IElement element)
    {
        if (element is not Label label || handler.PlatformView is not UILabel platformLabel || platformLabel.Font is not { } font)
            return;

        if (!Text.GetTrimToCapHeight(label))
            return;

        // Descender is negative in UIKit; CapOffset wants the distance below the baseline.
        var offset = Text.CapOffset((double)font.Ascender, -(double)font.Descender, (double)font.CapHeight);
        label.TranslationY = -offset;
    }
}

#endif
