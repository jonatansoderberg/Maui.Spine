using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using System;

namespace Plugin.Maui.SpineControls;

public partial class SpineCollectionView
{
    // ────────────────────────────────────────────────────────────────────────
    // Header construction
    // ────────────────────────────────────────────────────────────────────────

    private Border BuildHeaderBorder()
    {
        _titleLabel = new Label
        {
            Text = HeaderTitle ?? string.Empty,
            FontFamily = HeaderTitleFontFamily,
            FontSize = HeaderTitleFontSize,
            TextTransform = TextTransform.Uppercase,
            TextColor = HeaderTitleColor ?? Colors.White,
            ZIndex = 100,
            Margin = new Thickness(10, -4),
            Shadow = new Shadow { Offset = new Point(0, 0), Radius = 2, Brush = new SolidColorBrush(Colors.Black), Opacity = 0.8f }
        };
        AbsoluteLayout.SetLayoutBounds(_titleLabel, new Rect(0, 1, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(_titleLabel, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.PositionProportional);

        _headerOverlayLayout = new AbsoluteLayout();
        _headerOverlayLayout.Add(_titleLabel);

        _headerImage = new Image
        {
            Aspect = Aspect.AspectFill,
            Source = HeaderImageSource,
            Shadow = new Shadow { Offset = new Point(0, 2), Radius = 6, Brush = new SolidColorBrush(Colors.Black), Opacity = 0.8f }
        };
        _headerImage.Loaded += OnHeaderImageLoaded;

        var innerGrid = new Grid();
        innerGrid.Add(_headerImage);

        if (HeaderOverlayContent != null)
        {
            _overlayView = HeaderOverlayContent;
            _overlayView.Opacity = 0;
            innerGrid.Add(_overlayView);
        }

        innerGrid.Add(_headerOverlayLayout);

        _currentHeight = _maxHeight;

        // HeightRequest is fixed — it never changes. Collapsing is done purely
        // via TranslationY (a GPU-only transform that causes zero layout passes).
        return new Border
        {
            HeightRequest    = _maxHeight,
            Margin           = new Thickness(0, 0, HeaderScrollBarInset, 0),
            ZIndex           = 1000,
            StrokeThickness  = 0,
            VerticalOptions  = LayoutOptions.Start,
            InputTransparent = true,
            Content          = innerGrid
        };
    }

    // ────────────────────────────────────────────────────────────────────────
    // Property-changed handlers
    // ────────────────────────────────────────────────────────────────────────

    private void OnHeaderImageSourceChanged(ImageSource? source)
    {
        if (_headerImage != null) _headerImage.Source = source;
    }

    private void OnHeaderTitleChanged(string? title)
    {
        if (_titleLabel != null) _titleLabel.Text = title ?? string.Empty;
    }

    private void OnHeaderTitleColorChanged(Color? color)
    {
        if (_titleLabel != null) _titleLabel.TextColor = color ?? Colors.White;
    }

    private void OnHeaderTitleFontFamilyChanged(string? family)
    {
        if (_titleLabel != null) _titleLabel.FontFamily = family;
    }

    private void OnHeaderTitleFontSizeChanged(double size)
    {
        if (_titleLabel != null) _titleLabel.FontSize = size;
    }

    private void OnHeaderScrollBarInsetChanged(double inset)
    {
        var margin = new Thickness(0, 0, inset, 0);
        if (_headerBorder != null) _headerBorder.Margin = margin;
        if (_headerTopActionsLayout != null) _headerTopActionsLayout.Margin = margin;
        if (_headerBottomActionsLayout != null) _headerBottomActionsLayout.Margin = margin;
    }

    private void OnHeaderMaxHeightChanged(double value)
    {
        _maxHeight    = value;
        _collapseZone = _maxHeight - _minHeight;
    }

    private void OnHeaderMinHeightChanged(double value)
    {
        _minHeight    = value;
        _collapseZone = _maxHeight - _minHeight;
    }

    private void OnHeaderOverlayContentChanged(View? view)
    {
        if (_headerBorder?.Content is not Grid innerGrid) return;

        if (_overlayView != null)
            innerGrid.Remove(_overlayView);

        _overlayView = view;

        if (_overlayView != null)
        {
            _overlayView.Opacity = 0;
            innerGrid.Insert(innerGrid.Count - 1, _overlayView);
        }
    }

    private void OnHeaderTopContentChanged(View? view)
    {
        if (_headerTopActionsLayout == null) return; // applied later in OnParentSet

        if (_headerTopContent != null)
        {
            _headerTopActionsLayout.Remove(_headerTopContent);
            _headerTopContent = null;
        }

        _headerTopContent = view;

        if (_headerTopContent != null)
        {
            AbsoluteLayout.SetLayoutBounds(_headerTopContent, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(_headerTopContent, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
            _headerTopActionsLayout.Add(_headerTopContent);
        }

        ScheduleDragRegionUpdate();
    }

    private void OnHeaderBottomContentChanged(View? view)
    {
        if (_headerBottomActionsLayout == null) return; // applied later in OnParentSet

        if (_headerBottomContent != null)
        {
            _headerBottomActionsLayout.Remove(_headerBottomContent);
            _headerBottomContent = null;
        }

        _headerBottomContent = view;

        if (_titleLabel != null)
            _titleLabel.IsVisible = view == null;

        if (_headerBottomContent != null)
        {
            AbsoluteLayout.SetLayoutBounds(_headerBottomContent, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(_headerBottomContent, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
            _headerBottomActionsLayout.Add(_headerBottomContent);
        }

        ScheduleDragRegionUpdate();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Image corner-radius clipping
    // ────────────────────────────────────────────────────────────────────────

    private void OnHeaderImageLoaded(object? sender, EventArgs e)
    {
        if (sender is Image image)
        {
            image.SizeChanged += (_, _) => ApplyImageCornerClip(image);
            ApplyImageCornerClip(image);
        }
    }

    private void ApplyImageCornerClip(Image image)
    {
        var width = image.Width;
        var height = image.Height;
        const float radius = 2f;

        var hash = image.Bounds.GetHashCode();
        if (hash == _clipHash) return;
        _clipHash = hash;

        image.Clip = new PathGeometry
        {
            Figures =
            {
                new PathFigure
                {
                    StartPoint = new Point(0, 0),
                    Segments =
                    {
                        new LineSegment { Point = new Point(width, 0) },
                        new LineSegment { Point = new Point(width, height - radius) },
                        new BezierSegment
                        {
                            Point1 = new Point(width, height),
                            Point2 = new Point(width - radius, height),
                            Point3 = new Point(width - radius, height)
                        },
                        new LineSegment { Point = new Point(radius, height) },
                        new BezierSegment
                        {
                            Point1 = new Point(0, height),
                            Point2 = new Point(0, height - radius),
                            Point3 = new Point(0, height - radius)
                        }
                    },
                    IsClosed = true
                }
            }
        };
    }
}
