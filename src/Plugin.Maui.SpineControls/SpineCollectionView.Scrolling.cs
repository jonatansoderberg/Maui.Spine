using System;

namespace Plugin.Maui.SpineControls;

public partial class SpineCollectionView
{
    // Collapsing is done entirely via TranslationY (pure GPU compositor
    // transform — zero layout/measure passes per frame).  An anchor is
    // planted on every direction change so all height calculations are
    // derived deterministically and never accumulated (no floating-point
    // drift).  At the top boundary the header snaps back to fully expanded
    // and state is reset.

    private const double LayoutEpsilon          = 0.5;
    private const double DirectionChangeMinDelta = 3.0;

    private double _lastAcceptedOffset = -1;

    private void OnScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (_headerBorder == null) return;

        var offset       = e.VerticalOffset;
        var maxH         = _maxHeight;
        var minH         = _minHeight;
        var collapseZone = _collapseZone;

        // Top edge: snap to fully expanded and reset state.
        if (offset <= 0)
        {
            if (_currentHeight >= 0 && _currentHeight < maxH)
            {
                _currentHeight      = maxH;
                _scrollDirection    = 0;
                _anchorOffset       = -1;
                _lastAcceptedOffset = -1;
                _lastOverlayOpacity = 0;

                void ResetHeader()
                {
                    _headerBorder!.TranslationY = 0;
                    if (_headerBottomActionsLayout != null) _headerBottomActionsLayout.TranslationY = 0;
                    if (_overlayView != null) _overlayView.Opacity = 0;
                }

                ResetHeader();
                ScheduleDragRegionUpdate();
            }
            return;
        }

        if (_currentHeight < 0)
            _currentHeight = maxH;

        // Derive direction: prefer VerticalDelta; fall back to offset comparison
        // when the platform reports 0 (common on Android mid-fling).
        int newDirection;
        if (e.VerticalDelta > 0)
            newDirection = 1;
        else if (e.VerticalDelta < 0)
            newDirection = -1;
        else if (_lastAcceptedOffset >= 0)
            newDirection = offset > _lastAcceptedOffset + LayoutEpsilon ? 1
                         : offset < _lastAcceptedOffset - LayoutEpsilon ? -1
                         : 0;
        else
            newDirection = 0;

        // Direction-change hysteresis: suppress micro-oscillations (~1-3 DIPs)
        // that RecyclerView reports during fling deceleration.
        if (newDirection != 0 && newDirection != _scrollDirection && _scrollDirection != 0
            && _lastAcceptedOffset >= 0
            && Math.Abs(offset - _lastAcceptedOffset) < DirectionChangeMinDelta)
        {
            return;
        }

        // Plant a new anchor on direction change.
        if (newDirection != 0 && newDirection != _scrollDirection)
        {
            _scrollDirection = newDirection;
            _anchorOffset    = offset;
            _anchorHeight    = _currentHeight;
        }

        _lastAcceptedOffset = offset;

        if (_anchorOffset < 0) return;

        // Deterministic height — computed from anchor, never accumulated.
        double travelled    = offset - _anchorOffset;
        double targetHeight = _scrollDirection > 0
            ? Math.Max(minH, _anchorHeight - travelled)
            : Math.Min(maxH, _anchorHeight - travelled);

        targetHeight = Math.Clamp(targetHeight, minH, maxH);

        // Prevent a visual gap between the header bottom and the first list
        // item: enforce targetHeight >= maxH - min(offset, collapseZone).
        targetHeight = Math.Max(targetHeight, maxH - Math.Min(offset, collapseZone));

        if (Math.Abs(targetHeight - _currentHeight) < LayoutEpsilon) return;

        _currentHeight = targetHeight;

        // TranslationY: 0 = fully expanded, -(maxH - minH) = fully collapsed.
        double translation    = -(maxH - _currentHeight);
        double t              = Math.Round(Math.Clamp((maxH - _currentHeight) / collapseZone, 0, 1), 2);
        bool   opacityChanged = t != _lastOverlayOpacity;
        if (opacityChanged) _lastOverlayOpacity = t;

        _headerBorder.TranslationY = translation;
        if (_headerBottomActionsLayout != null) _headerBottomActionsLayout.TranslationY = translation;
        if (_overlayView != null && opacityChanged) _overlayView.Opacity = t;
        ScheduleDragRegionUpdate();
    }
}
