namespace Plugin.Maui.Spine.Core;

/// <summary>
/// Turns the device's attitude into how far a layer above the screen moves, from -1 to 1 along each
/// screen axis (x right, y down): toward the edge that tilts away from the viewer. The tilt is measured
/// against a neutral attitude that slowly follows the device, so a phone held upright, or lying on a
/// table, comes to rest at the centre; and it is smoothed against sensor noise. Used by
/// <c>Motion.Depth</c> on Android; iOS does the same in <c>UIInterpolatingMotionEffect</c>.
/// </summary>
internal sealed class TiltTracker
{
    /// <summary>The sine of the tilt that reaches full depth: about 20°.</summary>
    public const double Range = 0.35;

    /// <summary>The time constant, in seconds, of the neutral attitude following a held tilt.</summary>
    public const double SettleSeconds = 3;

    /// <summary>The time constant, in seconds, of the smoothing.</summary>
    public const double SmoothSeconds = 0.06;

    // Where the viewer is, in world coordinates: where the screen faced when it was last at rest.
    double _viewerX, _viewerY, _viewerZ;
    long _lastNanos;
    bool _started;

    /// <summary>The horizontal tilt, from -1 (left) to 1 (right).</summary>
    public double X { get; private set; }

    /// <summary>The vertical tilt, from -1 (up) to 1 (down).</summary>
    public double Y { get; private set; }

    /// <summary>Back to the centre: the next attitude becomes the neutral one.</summary>
    public void Reset()
    {
        _started = false;
        X = 0;
        Y = 0;
    }

    /// <param name="rotation">
    /// The device-to-world rotation, a row-major 3×3 matrix as Android's
    /// <c>SensorManager.getRotationMatrixFromVector</c> fills it.
    /// </param>
    /// <param name="timestampNanos">When the attitude was measured, in nanoseconds.</param>
    /// <param name="quarterTurns">
    /// How far the display is turned from the device's natural orientation, counter-clockwise, as
    /// Android's <c>Display.getRotation()</c>: 0 to 3.
    /// </param>
    public void Update(ReadOnlySpan<float> rotation, long timestampNanos, int quarterTurns)
    {
        // The screen's normal in world coordinates: the device's z axis, the matrix's third column.
        double normalX = rotation[2], normalY = rotation[5], normalZ = rotation[8];

        if (!_started)
        {
            (_viewerX, _viewerY, _viewerZ) = (normalX, normalY, normalZ);
            _lastNanos = timestampNanos;
            _started = true;
            X = 0;
            Y = 0;
            return;
        }

        var seconds = Math.Clamp((timestampNanos - _lastNanos) / 1e9, 0, 0.25);
        _lastNanos = timestampNanos;

        var follow = 1 - Math.Exp(-seconds / SettleSeconds);
        _viewerX += (normalX - _viewerX) * follow;
        _viewerY += (normalY - _viewerY) * follow;
        _viewerZ += (normalZ - _viewerZ) * follow;

        var length = Math.Sqrt(_viewerX * _viewerX + _viewerY * _viewerY + _viewerZ * _viewerZ);
        if (length < 1e-6)
        {
            Reset();
            return;
        }

        _viewerX /= length;
        _viewerY /= length;
        _viewerZ /= length;

        // The viewer in device coordinates (the transpose of the rotation, applied to the world
        // vector). A layer above the screen appears shifted away from the viewer.
        var shiftX = -(rotation[0] * _viewerX + rotation[3] * _viewerY + rotation[6] * _viewerZ);
        var shiftUp = -(rotation[1] * _viewerX + rotation[4] * _viewerY + rotation[7] * _viewerZ);

        // From the device's axes to the screen's, as the display is turned.
        var (screenX, screenUp) = (quarterTurns & 3) switch
        {
            1 => (-shiftUp, shiftX),
            2 => (-shiftX, -shiftUp),
            3 => (shiftUp, -shiftX),
            _ => (shiftX, shiftUp),
        };

        var smooth = 1 - Math.Exp(-seconds / SmoothSeconds);
        X += (Math.Clamp(screenX / Range, -1, 1) - X) * smooth;
        Y += (Math.Clamp(-screenUp / Range, -1, 1) - Y) * smooth;
    }
}
