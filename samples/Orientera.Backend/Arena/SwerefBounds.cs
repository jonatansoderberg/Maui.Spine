namespace Orientera.Backend.Arena;

/// <summary>En markrektangel i SWEREF99 TM, i meter.</summary>
public readonly record struct SwerefBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}
