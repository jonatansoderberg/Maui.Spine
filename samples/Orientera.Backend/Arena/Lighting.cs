using System.Globalization;
using System.Numerics;
using Orientera.Services.Sources;

namespace Orientera.Backend.Arena;

/// <summary>Slutgraderingens rattar: lokalkontrast, S-kurva, delad toning, mättnad och vegetationslyft.</summary>
public sealed record GradeSettings(float Local, float Contrast, float Warmth, float Saturation, float Vegetation);

/// <summary>Årstidens utseende: snö, ljusstyrka och gradering. Ljusets färg styrs inte här — den följer solhöjden.</summary>
public sealed record SeasonLook(bool Snow, float Gain, float Ambient, GradeSettings Grade)
{
    public static readonly IReadOnlyDictionary<ArenaSeason, SeasonLook> All = new Dictionary<ArenaSeason, SeasonLook>
    {
        [ArenaSeason.Sommar] = new(false, 1.74f, 0.34f, new(0.66f, 0.40f, 0.085f, 1.32f, 0.55f)),
        [ArenaSeason.Var] = new(false, 1.78f, 0.36f, new(0.64f, 0.38f, 0.070f, 1.34f, 0.66f)),
        [ArenaSeason.Host] = new(false, 1.70f, 0.33f, new(0.68f, 0.42f, 0.115f, 1.28f, 0.26f)),
        [ArenaSeason.Vinter] = new(true, 1.70f, 0.38f, new(0.58f, 0.36f, 0.055f, 1.06f, 0.30f)),
    };
}

/// <summary>
/// Ljusets riktning, färg och styrka vid en given solhöjd.
/// </summary>
/// <remarks>
/// Tre regimer. I dagsljus går låg sol genom mer atmosfär, som sprider bort det blå —
/// kvällstonen behöver inte väljas, den faller ut. Under horisonten finns ingen sol att
/// skugga med, och då byts ljuskällan mot månen: vid fullmåne står den mitt emot solen, så
/// azimuten är solens plus 180. Nattbilden är en stilisering — ortofotot är taget i dagsljus,
/// och ingen efemerid säger att månen faktiskt lyser den natten.
/// </remarks>
public sealed record Lighting
{
    /// <summary>Solhöjd under vilken det inte finns dagsljus kvar att rendera i.</summary>
    public const double Twilight = -6.0;

    /// <summary>
    /// Solhöjden varje dagbild ljussätts vid, oavsett vad klockan säger.
    /// </summary>
    /// <remarks>
    /// Skymning är vackert att stå i och dunkelt att titta på: låg sol lägger halva terrängen
    /// i skugga och färgar resten orange, och terrängen är hela poängen med bilden. De flesta
    /// närtävlingar startar 18:30 och skulle få just det ljuset — de ljussätts i stället som
    /// mitt på dagen. Azimuten är tävlingstidens, så skuggorna faller åt olika håll mellan
    /// tävlingar; bara höjden lyfts.
    /// </remarks>
    public const double DayFloor = 35.0;

    public required double Azimuth { get; init; }
    public required double Altitude { get; init; }
    public required bool Night { get; init; }
    public required Vector3 Sun { get; init; }
    public required Vector3 Sky { get; init; }
    public required Vector3 Haze { get; init; }
    public required float HazeStrength { get; init; }
    public required float Gain { get; init; }
    public required float Ambient { get; init; }
    public GradeSettings? Grade { get; init; }
    public required string Label { get; init; }

    /// <summary>
    /// Ljuset en tävlingsbild ska ha: ren dag eller ren natt, aldrig skymningen emellan.
    /// </summary>
    /// <remarks>
    /// En nattävling är natt oavsett vad solen gör. I juni står den bara ett par grader under
    /// horisonten klockan tio, och en gryningsljus nattbild vore fel på ett annat sätt.
    /// </remarks>
    public static Lighting For(double altitudeDegrees, double azimuthDegrees, bool nightRace) =>
        // Nattgrenen nedan läser bara att höjden ligger under Twilight; själva värdet används inte.
        nightRace || altitudeDegrees < Twilight
            ? At(Twilight - 1.0, azimuthDegrees)
            : At(Math.Max(altitudeDegrees, DayFloor), azimuthDegrees);

    public static Lighting At(double altitudeDegrees, double azimuthDegrees)
    {
        if (altitudeDegrees < Twilight)
        {
            return new Lighting
            {
                Azimuth = (azimuthDegrees + 180.0) % 360.0,
                Altitude = 32.0,
                Night = true,
                Sun = new Vector3(0.58f, 0.72f, 1.00f),
                Sky = new Vector3(0.30f, 0.38f, 0.60f),
                Haze = new Vector3(0.13f, 0.17f, 0.30f),
                HazeStrength = 0.50f,
                Gain = 0.66f,
                Ambient = 1.55f,
                Grade = new GradeSettings(0.34f, 0.22f, -0.045f, 0.68f, 0f),
                Label = "natt, månsken (stiliserat)",
            };
        }

        var t = (float)Math.Clamp((altitudeDegrees - 4.0) / 46.0, 0, 1);
        return new Lighting
        {
            Azimuth = azimuthDegrees,
            Altitude = Math.Max(altitudeDegrees, 5.0),
            Night = false,
            Sun = Vector3.Lerp(new Vector3(1.48f, 0.90f, 0.52f), new Vector3(1.06f, 1.00f, 0.93f), t),
            Sky = Vector3.Lerp(new Vector3(0.50f, 0.66f, 1.08f), new Vector3(0.76f, 0.85f, 1.00f), t),
            Haze = Vector3.Lerp(new Vector3(0.96f, 0.85f, 0.70f), new Vector3(0.82f, 0.85f, 0.90f), t),
            HazeStrength = 0.50f - 0.12f * t,
            Gain = 1.0f,
            Ambient = 1.0f,
            Grade = null,
            Label = string.Create(CultureInfo.InvariantCulture,
                $"solhöjd {altitudeDegrees:F0}°, azimut {azimuthDegrees:F0}°"),
        };
    }
}
