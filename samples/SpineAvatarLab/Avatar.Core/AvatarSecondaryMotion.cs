namespace Plugin.Maui.Spine.Controls.Avatar.Core;

/// <summary>
/// Springs on top of the keyframed motion: squash and stretch with the voice, a small hop when
/// speech starts, a squash on interrupt, a head tilt that follows gaze and thinking. Keyframes say
/// where the avatar goes; the springs make it overshoot and settle, which is what reads as alive.
/// </summary>
/// <remarks>
/// Renderer-neutral: the frame carries three numbers (<see cref="AvatarRenderFrame.Squash"/>,
/// <see cref="AvatarRenderFrame.Tilt"/>, <see cref="AvatarRenderFrame.Lift"/>) and each renderer applies
/// them to its root or head. Off under Reduce Motion. Integrated in fixed 1/120 s steps, so the result
/// depends on time, not on the frame rate.
/// </remarks>
internal sealed class AvatarSecondaryMotion
{
    private const double Step = 1.0 / 120;

    private Spring _squash = new(frequency: 4.2, damping: 0.32);
    private Spring _tilt = new(frequency: 1.9, damping: 0.42);
    private Spring _lift = new(frequency: 3.4, damping: 0.38);
    private float _lastOutput, _lastInput;

    /// <summary>0 turns secondary motion off, 1 is the designed amount; up to 2 exaggerates it.</summary>
    public float Amount { get; set; } = 1;

    public void OnStateChanged(AvatarState from, AvatarState to)
    {
        switch (to)
        {
            case AvatarState.Speaking:
                _lift.Velocity += 0.09;
                _squash.Velocity += 0.35;
                break;
            case AvatarState.Interrupted:
                _squash.Velocity -= 1.6;
                break;
            case AvatarState.Listening:
                _lift.Velocity += 0.05;
                break;
            case AvatarState.Thinking:
                _tilt.Velocity += 0.25;
                break;
            case AvatarState.Idle when from is AvatarState.Speaking:
                _squash.Velocity -= 0.25;
                break;
        }
    }

    public void OnGesture() => _squash.Velocity -= 0.3;

    public void Update(double dt, AvatarRenderFrame f, ReadOnlySpan<float> stateWeights, bool off)
    {
        if (off || Amount <= 0)
        {
            _squash = _squash.Rest();
            _tilt = _tilt.Rest();
            _lift = _lift.Rest();
            return;
        }

        var speaking = stateWeights[(int)AvatarState.Speaking];
        var listening = stateWeights[(int)AvatarState.Listening];
        var thinking = stateWeights[(int)AvatarState.Thinking];

        // Onsets kick the spring: a syllable starting is a push, not a level.
        var output = f.OutputLevel * f.OutputReactiveWeight;
        var input = f.InputLevel * f.InputReactiveWeight;
        if (dt > 0)
        {
            if (output - _lastOutput > 0.12f)
                _squash.Velocity += (output - _lastOutput) * 1.6;
            if (input - _lastInput > 0.15f)
                _lift.Velocity += (input - _lastInput) * 0.12;
        }
        _lastOutput = output;
        _lastInput = input;

        var squashTarget = 0.045 * output + 0.02 * input;
        var liftTarget = 0.012 * listening + 0.01 * output;
        var tiltTarget = 0.07 * thinking + (0.35 + 0.25 * listening) * f.GazeX - 0.02 * speaking * Math.Sin(f.ElapsedSeconds * 2.1);

        for (var t = dt; t > 1e-9; t -= Step)
        {
            var h = Math.Min(Step, t);
            _squash.Advance(squashTarget, h);
            _tilt.Advance(tiltTarget, h);
            _lift.Advance(liftTarget, h);
        }

        f.Squash = (float)(Math.Clamp(_squash.Value, -0.2, 0.2) * Amount);
        f.Tilt = (float)(Math.Clamp(_tilt.Value, -0.3, 0.3) * Amount);
        f.Lift = (float)(Math.Clamp(_lift.Value, -0.08, 0.08) * Amount);
    }

    /// <summary>A damped spring; underdamped (damping &lt; 1) so it overshoots once or twice and settles.</summary>
    private struct Spring(double frequency, double damping)
    {
        public double Value, Velocity;

        public void Advance(double target, double dt)
        {
            var omega = 2 * Math.PI * frequency;
            var acceleration = -2 * damping * omega * Velocity - omega * omega * (Value - target);
            Velocity += acceleration * dt;
            Value += Velocity * dt;
        }

        public readonly Spring Rest() => new(frequency, damping);
    }
}
