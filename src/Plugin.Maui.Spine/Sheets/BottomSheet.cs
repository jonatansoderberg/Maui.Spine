using Plugin.Maui.Spine.Core;

namespace Plugin.Maui.Spine.Extensions;

/// <summary>Identifies the size category of a <see cref="BottomSheetDetent"/>.</summary>
internal enum BottomSheetDetentType
{
    /// <summary>The sheet occupies most of the available height.</summary>
    Large,
    /// <summary>The sheet occupies approximately half of the available height.</summary>
    Medium,
    /// <summary>The sheet has a user-defined height.</summary>
    Custom
}

/// <summary>
/// Represents a single snap point used by the legacy <see cref="BottomSheetBuilder"/> API.
/// Prefer <see cref="SheetDetent"/> for new code.
/// </summary>
internal class BottomSheetDetent
{
    /// <summary>The size category of this detent.</summary>
    public BottomSheetDetentType Type { get; set; }

    /// <summary>Platform identifier string for this detent (used for custom detents).</summary>
    public string? Identifier { get; set; }

    /// <summary>Absolute height in pixels (used when <see cref="Type"/> is <see cref="BottomSheetDetentType.Custom"/>).</summary>
    public int Height { get; set; }
}

/// <summary>
/// Fluent builder for configuring a bottom sheet's detents and overlay.
/// Used internally by the Spine host page when presenting a sheet.
/// Prefer using <see cref="NavigableSheetAttribute"/> properties for declarative configuration.
/// </summary>
internal class BottomSheetBuilder
{
    /// <summary>Optional callback invoked when the sheet is dismissed.</summary>
    public Action? OnDissapearing { get; private set; } = null;

    /// <summary>Legacy detents list (used by platform-specific renderers).</summary>
    public List<BottomSheetDetent> Detents { get; } = new();

    /// <summary>The <see cref="SheetDetent"/> snap points the sheet can be dragged to.</summary>
    public List<SheetDetent> AllowedDetents { get; } = new();

    /// <summary>The <see cref="SheetDetent"/> the sheet opens at.</summary>
    public SheetDetent? SelectedDetent { get; private set; }

    /// <summary>Visual treatment applied to the page behind the sheet while it is open.</summary>
    public BackgroundPageOverlay BackgroundPageOverlay { get; private set; } = BackgroundPageOverlay.Dimmed;

    /// <summary>Backdrop material applied to the sheet surface itself (Windows only).</summary>
    public WindowBackdrop SheetBackdrop { get; private set; } = WindowBackdrop.None;

        /// <summary>Sets the <see cref="BackgroundPageOverlay"/> applied behind the sheet.</summary>
        public BottomSheetBuilder SetBackgroundPageOverlay(BackgroundPageOverlay overlay)
        {
            BackgroundPageOverlay = overlay;
            return this;
        }

        /// <summary>Sets the <see cref="WindowBackdrop"/> material applied to the sheet surface (Windows only).</summary>
        public BottomSheetBuilder SetSheetBackdrop(WindowBackdrop backdrop)
        {
            SheetBackdrop = backdrop;
            return this;
        }

        /// <summary>Registers a callback invoked when the sheet disappears.</summary>
        public BottomSheetBuilder AddDissapering(Action action)
        {
            OnDissapearing = action;
            return this;
        }

        /// <summary>Adds a <see cref="SheetDetent"/> snap point to <see cref="AllowedDetents"/>.</summary>
        public BottomSheetBuilder AddDetent(SheetDetent detent)
        {
            AllowedDetents.Add(detent);
            return this;
        }

        /// <summary>Sets the detent the sheet snaps to when it first opens.</summary>
        public BottomSheetBuilder SetSelectedDetent(SheetDetent detent)
        {
            SelectedDetent = detent;
            return this;
        }

        /// <summary>Adds a <see cref="BottomSheetDetentType.Large"/> legacy detent.</summary>
        public BottomSheetBuilder AddLargeDetent()
        {
            Detents.Add(new BottomSheetDetent{Type = BottomSheetDetentType.Large});
            return this;
        }

        /// <summary>Adds a <see cref="BottomSheetDetentType.Medium"/> legacy detent.</summary>
        public BottomSheetBuilder AddMediumDetent()
        {
            Detents.Add(new BottomSheetDetent{Type = BottomSheetDetentType.Medium});
            return this;
        }

        /// <summary>Adds a custom-height legacy detent.</summary>
        /// <param name="identifier">Stable identifier for this detent.</param>
        /// <param name="height">Absolute height in pixels.</param>
        public BottomSheetBuilder AddCustomDetent(string identifier, int height)
        {
            Detents.Add(new BottomSheetDetent
            {
                Identifier = identifier,
                Height = height,
                Type = BottomSheetDetentType.Custom
            });
            return this;
        }
    }
