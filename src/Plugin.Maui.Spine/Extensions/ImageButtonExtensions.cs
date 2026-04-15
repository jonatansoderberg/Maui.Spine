namespace Plugin.Maui.Spine.Extensions;

/// <summary>
/// Extension methods that apply standard Fluent-style visual states to an <see cref="ImageButton"/>.
/// </summary>
public static class ImageButtonVisualStates
{
    /// <summary>
    /// Applies Normal, PointerOver, Pressed, and Disabled visual states to <paramref name="button"/>
    /// using Windows 11 Fluent overlay values that automatically adapt to light/dark themes.
    /// </summary>
    /// <param name="button">The <see cref="ImageButton"/> to configure.</param>
    /// <param name="hideDisabled">When <see langword="true"/> the button is invisible in the Disabled state instead of dimmed.</param>
    public static void ApplyCommonVisualStates(this ImageButton button, bool hideDisabled = false)
    {
        void ApplyStates()
        {
            bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;

            // Windows 11 Fluent overlay values
            Color hoverOverlay = dark ? Color.FromRgba(255, 255, 255, 0.08f)
                                      : Color.FromRgba(0, 0, 0, 0.06f);

            Color pressedOverlay = dark ? Color.FromRgba(255, 255, 255, 0.12f)
                                        : Color.FromRgba(0, 0, 0, 0.10f);

            var commonStates = new VisualStateGroup { Name = "CommonStates" };

            var normalState = new VisualState { Name = "Normal" };
            normalState.Setters.Add(new Setter { Property = ImageButton.BackgroundColorProperty, Value = Colors.Transparent });
            normalState.Setters.Add(new Setter { Property = ImageButton.OpacityProperty, Value = 1.0 });

            var pointerOverState = new VisualState { Name = "PointerOver" };
            pointerOverState.Setters.Add(new Setter { Property = ImageButton.BackgroundColorProperty, Value = hoverOverlay });
            pointerOverState.Setters.Add(new Setter { Property = ImageButton.OpacityProperty, Value = 1.0 });

            var pressedState = new VisualState { Name = "Pressed" };
            pressedState.Setters.Add(new Setter { Property = ImageButton.BackgroundColorProperty, Value = pressedOverlay });
            pressedState.Setters.Add(new Setter { Property = ImageButton.OpacityProperty, Value = 1.0 });

            var disabledState = new VisualState { Name = "Disabled" };
            disabledState.Setters.Add(new Setter { Property = ImageButton.BackgroundColorProperty, Value = Colors.Transparent });
            if (hideDisabled)
            {
                disabledState.Setters.Add(new Setter { Property = ImageButton.OpacityProperty, Value = 0 });
            }
            else
            {
                disabledState.Setters.Add(new Setter { Property = ImageButton.OpacityProperty, Value = 0.4 });
            }

            commonStates.States.Add(normalState);
            commonStates.States.Add(pointerOverState);
            commonStates.States.Add(pressedState);
            commonStates.States.Add(disabledState);

            VisualStateManager.SetVisualStateGroups(button, new VisualStateGroupList { commonStates });
        }

        // Initial apply
        ApplyStates();

        // Subscribe once to theme switching (idempotent)
        // Important: use weak event pattern to avoid leaks if many buttons
        //Application.Current.RequestedThemeChanged -= OnThemeChanged;
        //Application.Current.RequestedThemeChanged += OnThemeChanged;

        //void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
        //{
        //    ApplyStates();
        //}
    }
}
