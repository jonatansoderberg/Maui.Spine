namespace MauiBottomSheetPoc;

public partial class App
{
    public App()
    {
        InitializeComponent();

#if IOS || MACCATALYST
        // Apple draws the thumb of a switch and a slider white in every state; only the track takes
        // the accent. The shared styles colour the thumb for Material 3, so it is dropped here.
        UsePlatformThumb(typeof(Switch));
        UsePlatformThumb(typeof(Slider));
#endif
    }

#if IOS || MACCATALYST
    private void UsePlatformThumb(Type type)
    {
        if (!Resources.TryGetValue(type.FullName!, out var value) || value is not Style style)
            return;

        var thumb = type == typeof(Switch) ? Switch.ThumbColorProperty : Slider.ThumbColorProperty;
        foreach (var setter in style.Setters.Where(s => s.Property == thumb).ToList())
            style.Setters.Remove(setter);

        foreach (var setter in style.Setters.Where(s => s.Property == VisualStateManager.VisualStateGroupsProperty))
        {
            if (setter.Value is not VisualStateGroupList groups)
                continue;

            foreach (var state in groups.SelectMany(g => g.States))
            {
                foreach (var stateSetter in state.Setters.Where(s => s.Property == thumb).ToList())
                    state.Setters.Remove(stateSetter);
            }
        }
    }
#endif
}
