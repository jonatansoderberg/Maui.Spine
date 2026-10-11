using System.Globalization;

namespace Plugin.Maui.Spine.Controls.Avatar.Core;

public enum AvatarPathCommand : byte { MoveTo, LineTo, CubicTo, QuadTo, Close }

/// <summary>
/// Path data in the restricted grammar of the authoring profile: absolute M, L, C, Q and Z only.
/// Not a general SVG parser. Two paths with equal <see cref="Commands"/> can be interpolated point by point.
/// </summary>
public sealed class AvatarPathData
{
    private AvatarPathData(AvatarPathCommand[] commands, float[] points)
    {
        Commands = commands;
        Points = points;
    }

    public AvatarPathCommand[] Commands { get; }

    /// <summary>The coordinates of every command in order: 2 for M and L, 6 for C, 4 for Q, none for Z.</summary>
    public float[] Points { get; }

    public bool HasSameTopology(AvatarPathData other) => Commands.AsSpan().SequenceEqual(other.Commands);

    public static int Arity(AvatarPathCommand command) => command switch
    {
        AvatarPathCommand.MoveTo or AvatarPathCommand.LineTo => 2,
        AvatarPathCommand.CubicTo => 6,
        AvatarPathCommand.QuadTo => 4,
        _ => 0,
    };

    public const int MaxCommands = 512;

    public static AvatarPathData Parse(string data, string path)
    {
        var commands = new List<AvatarPathCommand>();
        var points = new List<float>();
        var i = 0;

        while (true)
        {
            SkipSeparators(data, ref i);
            if (i >= data.Length)
                break;

            var command = data[i] switch
            {
                'M' => AvatarPathCommand.MoveTo,
                'L' => AvatarPathCommand.LineTo,
                'C' => AvatarPathCommand.CubicTo,
                'Q' => AvatarPathCommand.QuadTo,
                'Z' => AvatarPathCommand.Close,
                var c => throw new AvatarFormatException(path, $"path command '{c}' at {i} is not one of the absolute M, L, C, Q, Z"),
            };
            i++;

            if (commands.Count == 0 && command != AvatarPathCommand.MoveTo)
                throw new AvatarFormatException(path, "a path must start with M");
            if (commands.Count >= MaxCommands)
                throw new AvatarFormatException(path, $"more than {MaxCommands} path commands");

            commands.Add(command);
            for (var n = 0; n < Arity(command); n++)
                points.Add(ReadNumber(data, ref i, path));
        }

        if (commands.Count == 0)
            throw new AvatarFormatException(path, "empty path");

        return new AvatarPathData([.. commands], [.. points]);
    }

    private static void SkipSeparators(string data, ref int i)
    {
        while (i < data.Length && (char.IsWhiteSpace(data[i]) || data[i] == ','))
            i++;
    }

    private static float ReadNumber(string data, ref int i, string path)
    {
        SkipSeparators(data, ref i);
        var start = i;
        if (i < data.Length && data[i] is '-' or '+')
            i++;
        while (i < data.Length && (char.IsAsciiDigit(data[i]) || data[i] == '.'))
            i++;
        if (i < data.Length && data[i] is 'e' or 'E')
        {
            i++;
            if (i < data.Length && data[i] is '-' or '+')
                i++;
            while (i < data.Length && char.IsAsciiDigit(data[i]))
                i++;
        }

        if (!float.TryParse(data.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            throw new AvatarFormatException(path, $"expected a finite number at {start}");

        return value;
    }
}
