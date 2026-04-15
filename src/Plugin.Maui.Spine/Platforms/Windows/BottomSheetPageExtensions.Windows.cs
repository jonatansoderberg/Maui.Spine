using Microsoft.Maui.Platform;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Plugin.Maui.Spine.Core;
using Plugin.Maui.Spine.Extensions;
using Plugin.Maui.Spine.Presentation;
using Plugin.Maui.Spine.Sheets;
using Border = Microsoft.UI.Xaml.Controls.Border;
using Brush = Microsoft.UI.Xaml.Media.Brush;
using Colors = Microsoft.UI.Colors;
using CornerRadius = Microsoft.UI.Xaml.CornerRadius;
using Grid = Microsoft.UI.Xaml.Controls.Grid;
using GridLength = Microsoft.UI.Xaml.GridLength;
using GridUnitType = Microsoft.UI.Xaml.GridUnitType;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using MauiPage = Microsoft.Maui.Controls.Page;
using RowDefinition = Microsoft.UI.Xaml.Controls.RowDefinition;
using Thickness = Microsoft.UI.Xaml.Thickness;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using Window = Microsoft.UI.Xaml.Window;

namespace Plugin.Maui.Spine;

/// <summary>
/// Windows-specific extension methods for presenting a bottom sheet on a MAUI <see cref="MauiPage"/>.
/// </summary>
internal static class BottomSheetPageExtensions
{
    internal static Action? ActiveBottomSheetDismiss { get; private set; }

    internal static void DismissActiveBottomSheet() => ActiveBottomSheetDismiss?.Invoke();

    /// <summary>
    /// Displays a bottom sheet on Windows by embedding <paramref name="bottomSheetFactory"/>'s content
    /// in a WinUI popup with draggable detent snapping and optional background overlay.
    /// Returns <see langword="true"/> when dismissed via the accept path, <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="page">The MAUI page that provides the <c>XamlRoot</c> for the popup.</param>
    /// <param name="bottomSheetFactory">Factory that creates the sheet content view.</param>
    /// <param name="builder">Optional delegate to configure detents and overlay.</param>
    public static async Task<bool> DisplayBottomSheet(
        this MauiPage page,
        Func<IView> bottomSheetFactory,
        Action<BottomSheetBuilder>? builder = null)
    {
        var bottomSheetContent = bottomSheetFactory();

        // Single active bottom sheet gate managed here.
        var userInteraction = new AwaitUserInteraction();

        var mauiContext = page.Handler?.MauiContext
            ?? throw new Exception("MauiContext is null");

        var window = page.Window;
        var xamlRoot = page.ToPlatform(mauiContext).XamlRoot;

        var isDarkTheme = (xamlRoot.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;

        // Defer platform view creation until the popup is opened.
        var nativeHost = new Grid();
        //nativeHost.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 255, 0, 0));
        nativeHost.VerticalAlignment = VerticalAlignment.Stretch;
        nativeHost.HorizontalAlignment = HorizontalAlignment.Stretch;

        // Tracks the last settled detent so dismiss cancellation can restore it.
        double lastSettledHeight = 0;

        // helper to invoke VM guard (if any)
        async Task<bool> CanDismissAsync()
        {
            // The sheet content is a NavigationRegion. The current page VM lives under FrontView.Content.
            if (bottomSheetContent is NavigationRegion region
                && region.BindingContext is NavigationRegionViewModel regionVm
                && regionVm.CurrentRegionViewModel is ViewModelBase currentVm)
            {
                return await currentVm.OnCloseRequestedAsync();
            }

            // Fallback: direct VM on the root bottom sheet view
            if (bottomSheetContent is BindableObject bo && bo.BindingContext is ViewModelBase vm)
            {
                return await vm.OnCloseRequestedAsync();
            }

            return true;
        }

        async Task RequestDismissAsync(bool result = false)
        {
            if (!await CanDismissAsync())
            {
                AnimateToDetent(lastSettledHeight);
                return;
            }

            userInteraction.Release(result);
        }

        var size = xamlRoot.Content.ActualSize;

        var bottomSheetBuilder = new BottomSheetBuilder();
        builder?.Invoke(bottomSheetBuilder);

        Brush overlayBrush = bottomSheetBuilder.BackgroundPageOverlay switch
        {
            BackgroundPageOverlay.None => new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BackgroundPageOverlay.Dimmed => new SolidColorBrush(Windows.UI.Color.FromArgb(110, 0, 0, 0)),
            _ => isDarkTheme
                ? new AcrylicBrush { TintColor = Colors.Gray, TintOpacity = 0.2, FallbackColor = Colors.Gray }
                : new AcrylicBrush { TintColor = Colors.White, TintOpacity = 0.6, FallbackColor = Colors.White }
        };

        var overlay = new Grid
        {
            Width = size.X,
            Height = size.Y,
            Background = overlayBrush,
            IsHitTestVisible = true
        };

        overlay.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        overlay.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var resizeHandle = new WinBottomSheetHandle();
        resizeHandle.VerticalAlignment = VerticalAlignment.Top;
        
        nativeHost.Padding = new Microsoft.UI.Xaml.Thickness(0, 12, 0, 0);

        var sheetContent = new Grid();
        //{
        //    Spacing = 0
        //};

        sheetContent.Children.Add(nativeHost);
        sheetContent.Children.Add(resizeHandle);

        Brush sheetBackgroundBrush = bottomSheetBuilder.SheetBackdrop switch
        {
            WindowBackdrop.Acrylic => isDarkTheme
                ? new AcrylicBrush { TintColor = Colors.Gray, TintOpacity = 0.2, FallbackColor = Colors.Gray }
                : new AcrylicBrush { TintColor = Colors.White, TintOpacity = 0.6, FallbackColor = Colors.White },
            WindowBackdrop.Mica => isDarkTheme
                ? new AcrylicBrush { TintColor = Colors.Gray, TintOpacity = 0.2, FallbackColor = Colors.Gray }
                : new AcrylicBrush { TintColor = Colors.White, TintOpacity = 0.6, FallbackColor = Colors.White },
            _ => isDarkTheme
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 32, 32, 32))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 243, 243))
        };

        var sheet = new Border
        {
            Background = sheetBackgroundBrush,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Child = sheetContent,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var translate = new TranslateTransform();
        sheet.RenderTransform = translate;
        sheet.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0);

        overlay.Children.Add(sheet);

        var popup = new Popup
        {
            XamlRoot = xamlRoot,
            Child = overlay,
            IsLightDismissEnabled = false
        };

        double DismissThreshold() => window.Height * 0.15;

        var allowedDetents = bottomSheetBuilder.AllowedDetents.Count > 0
            ? bottomSheetBuilder.AllowedDetents
            : new List<SheetDetent> { SheetDetent.MediumDetent };
        var selectedDetent = bottomSheetBuilder.SelectedDetent ?? allowedDetents[0];

        var nativeWindow = page.Window.Handler?.PlatformView as Window;
        double titleBarHeight = (nativeWindow?.AppWindow?.TitleBar is { } titleBar
            ? titleBar.Height / xamlRoot.RasterizationScale
            : 32) + 10;

        double ResolveDetentHeight(SheetDetent detent)
        {
            var usableHeight = window.Height - titleBarHeight;
            if (detent.IsPercentage) return usableHeight * detent.Percentage!.Value;
            if (detent.IsAbsolute) return detent.AbsoluteHeight!.Value;
            return usableHeight * 0.5;
        }

        double[] GetSnapHeights() => [.. allowedDetents.Select(ResolveDetentHeight).OrderBy(h => h)];

        sheet.Height = ResolveDetentHeight(selectedDetent);
        lastSettledHeight = sheet.Height;

        ShowWithAnimation(sheet.Height);

        window.SizeChanged += OnWindowSizeChanged;

        void ShowWithAnimation(double targetHeight)
        {
            sheet.Height = targetHeight;
            translate.Y = targetHeight;
            popup.IsOpen = true;

            if (nativeHost.Children.Count == 0)
            {
                try
                {
                    nativeHost.Children.Add(bottomSheetContent.ToPlatform(mauiContext));
                }
                catch
                {
                }
            }

            AnimateIn();
        }

        void AnimateIn()
        {
            overlay.Opacity = 0;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(130),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            var slideIn = new DoubleAnimation
            {
                From = translate.Y,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };

            var storyboard = new Storyboard();

            Storyboard.SetTarget(slideIn, translate);
            Storyboard.SetTargetProperty(slideIn, "Y");
            storyboard.Children.Add(slideIn);

            Storyboard.SetTarget(fadeIn, overlay);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            storyboard.Children.Add(fadeIn);

            storyboard.Begin();
        }

        var isDragging = false;
        uint pointerId = 0;
        double dragStartY = 0;
        double startHeight = 0;
        var dragIsDismiss = false;

        resizeHandle.PointerPressed += (s, e) =>
        {
            var pt = e.GetCurrentPoint(null);

            isDragging = true;
            dragIsDismiss = false;

            pointerId = pt.PointerId;
            dragStartY = pt.Position.Y;
            startHeight = sheet.Height;

            resizeHandle.CapturePointer(e.Pointer);
        };

        resizeHandle.PointerMoved += (s, e) =>
        {
            if (!isDragging)
                return;

            var pt = e.GetCurrentPoint(null);
            if (pt.PointerId != pointerId)
                return;

            var delta = pt.Position.Y - dragStartY;

            if (delta > 0)
            {
                dragIsDismiss = true;
                var next = startHeight - delta;
                sheet.Height = Math.Max(0, next);
            }
            else
            {
                dragIsDismiss = false;
                var snaps = GetSnapHeights();
                var next = startHeight - delta;
                sheet.Height = Math.Clamp(next, snaps[0], snaps[^1]);
            }
        };

        resizeHandle.PointerReleased += async (s, e) =>
        {
            if (e.GetCurrentPoint(null).PointerId != pointerId)
                return;

            isDragging = false;
            resizeHandle.ReleasePointerCapture(e.Pointer);

            // Only dismiss when the sheet has been dragged below the minimum snap
            // point by more than the dismiss threshold. This ensures dragging down
            // from a larger detent (e.g. 75%) snaps to a smaller one (e.g. 50%)
            // instead of being incorrectly treated as a dismiss.
            var snaps = GetSnapHeights();
            var minSnap = snaps[0];

            if (dragIsDismiss && sheet.Height < minSnap - DismissThreshold())
            {
                await RequestDismissAsync(false);
                return;
            }

            SnapHeight();
        };

        void CloseInternal()
        {
            var sheetTranslate = sheet.RenderTransform as TranslateTransform
                ?? new TranslateTransform();

            sheet.RenderTransform = sheetTranslate;

            var heightAnimation = new DoubleAnimation
            {
                From = 0,
                To = sheet.Height,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseIn
                }
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(heightAnimation, sheetTranslate);
            Storyboard.SetTargetProperty(heightAnimation, "Y");

            storyboard.Children.Add(heightAnimation);

            storyboard.Completed += (_, _) =>
            {
                var fadeAnimation = new DoubleAnimation
                {
                    From = overlay.Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(100),
                    EasingFunction = new CubicEase
                    {
                        EasingMode = EasingMode.EaseIn
                    }
                };

                var fadeStoryboard = new Storyboard();
                Storyboard.SetTarget(fadeAnimation, overlay);
                Storyboard.SetTargetProperty(fadeAnimation, "Opacity");
                fadeStoryboard.Children.Add(fadeAnimation);

                fadeStoryboard.Completed += (_, _) =>
                {
                    nativeHost.Children.Clear();

                    if (popup.IsOpen)
                    {
                        popup.IsOpen = false;
                    }

                    window.SizeChanged -= OnWindowSizeChanged;

                    overlay.Children.Clear();
                    popup.Child = null;

                    userInteraction.Release();
                };

                fadeStoryboard.Begin();
            };

            storyboard.Begin();
        }

        void SnapHeight()
        {
            var current = sheet.Height;

            var closest = GetSnapHeights()
                .OrderBy(h => Math.Abs(h - current))
                .First();

            if (closest <= 0)
                return;

            var snapAnimation = new DoubleAnimation
            {
                From = current,
                To = closest,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(snapAnimation, sheet);
            Storyboard.SetTargetProperty(snapAnimation, "Height");
            storyboard.Children.Add(snapAnimation);
            storyboard.Completed += (_, _) =>
            {
                sheet.Height = closest;
                lastSettledHeight = closest;
            };
            storyboard.Begin();
        }

        void AnimateToDetent(double targetHeight)
        {
            var heightAnimation = new DoubleAnimation
            {
                From = sheet.Height,
                To = targetHeight,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(heightAnimation, sheet);
            Storyboard.SetTargetProperty(heightAnimation, "Height");
            storyboard.Children.Add(heightAnimation);
            storyboard.Completed += (_, _) => sheet.Height = targetHeight;
            storyboard.Begin();
        }

        ActiveBottomSheetDismiss = () =>
        {
            _ = MainThread.InvokeOnMainThreadAsync(async () => await RequestDismissAsync(false));
        };

        overlay.PointerPressed += Overlay_PointerPressed;

        // Wait for bottom sheet to close
        await userInteraction.WaitForUserInteraction();

        CloseInternal();

        // clear hook after closing
        ActiveBottomSheetDismiss = null;

        return userInteraction.Result;

        async void Overlay_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var originalSource = e.OriginalSource as UIElement;
            if (originalSource == overlay)
            {
                await RequestDismissAsync(false);
            }
        }

                void OnWindowSizeChanged(object? sender, EventArgs e)
                {
                    var size = xamlRoot.Content.ActualSize;
                    if (size.X <= 0 || size.Y <= 0)
                        return;
                    overlay.Width = size.X;
                    overlay.Height = size.Y;
                    SnapHeight();
                }
            }

            /// <summary>
            /// A WinUI drag handle rendered at the top of a Windows bottom sheet popup.
            /// Displays a rounded pill indicator and sets the resize cursor to communicate draggability.
            /// </summary>
            private sealed class WinBottomSheetHandle : Grid
            {
                public WinBottomSheetHandle()
                {
                    Padding = new Thickness(0, 8, 0, 4);
                    HorizontalAlignment = HorizontalAlignment.Stretch;
                    Background = new SolidColorBrush(Colors.Transparent);

                    ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);

                    Children.Add(new Border
                    {
                        Width = 40,
                        Height = 4,
                        CornerRadius = new CornerRadius(2),
                        Background = new SolidColorBrush(Colors.Gray),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                }
            }
        }
