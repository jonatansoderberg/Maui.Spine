namespace Orientera.Backend.Arena;

/// <summary>
/// Murkontrollen: står den orange muren kvar där den ritades, efter bildmodellens pass?
/// </summary>
/// <remarks>
/// Muren är arrangörens faktiska gränsdragning. Flyttar eller raderar modellen den ljuger
/// bilden om var tävlingsområdet ligger, och då får bilden inte cachas — bättre inget svar
/// än fel svar. Kontrollen mäter på murens egna ytor ur renderingen, inte på en gissning:
/// varje kvadrat i muren ska fortfarande vara övervägande varnings-orange i utdatan.
///
/// Trösklarna är medvetet generösa — modellen får skugga och tona muren, den får bara inte
/// flytta eller ta bort den. Prototypens kontroll larmade falskt tre gånger; det är därför
/// täckningen loggas och gränsen ligger lågt.
/// </remarks>
public static class WallCheck
{
    /// <summary>Andel av murens kvadrar som fortfarande bär orange. Under detta är muren borta eller flyttad.</summary>
    public const double RequiredCoverage = 0.6;

    public static bool Survived(ColorGrid enhanced,
        IReadOnlyList<(double Distance, (double X, double Y)[] Quad, (double X, double Y)[] Top)> quads,
        out double coverage)
    {
        coverage = Coverage(enhanced, quads);
        return coverage >= RequiredCoverage;
    }

    public static double Coverage(ColorGrid enhanced,
        IReadOnlyList<(double Distance, (double X, double Y)[] Quad, (double X, double Y)[] Top)> quads)
    {
        if (quads.Count == 0)
            return 1.0;

        var passed = 0;
        foreach (var (_, quad, _) in quads)
        {
            var centerX = quad.Average(p => p.X);
            var centerY = quad.Average(p => p.Y);
            if (OrangeAround(enhanced, centerX, centerY))
                passed++;
        }
        return passed / (double)quads.Count;
    }

    /// <summary>Minst en fjärdedel orange i ett litet fönster kring punkten.</summary>
    private static bool OrangeAround(ColorGrid image, double x, double y, int radius = 3)
    {
        var total = 0;
        var orange = 0;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var px = (int)x + dx;
                var py = (int)y + dy;
                if (px < 0 || px >= image.Width || py < 0 || py >= image.Height)
                    continue;
                total++;
                var i = image.IndexOf(px, py);
                if (IsOrange(image.Values[i], image.Values[i + 1], image.Values[i + 2]))
                    orange++;
            }
        }
        return total > 0 && orange >= total / 4;
    }

    /// <summary>
    /// Varnings-orange, med marginal för modellens ljussättning: klart rödtyngd, grönt en
    /// bråkdel av rött, blått nästan inget.
    /// </summary>
    private static bool IsOrange(float r, float g, float b) =>
        r > 0.28f && r > 1.35f * b && g > 0.15f * r && g < 0.85f * r;
}
