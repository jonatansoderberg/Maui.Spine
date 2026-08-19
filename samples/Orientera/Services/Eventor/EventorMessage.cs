namespace Orientera.Services.Eventor;

/// <summary>
/// What the app says about the Eventor login, in one place.
/// </summary>
/// <remarks>
/// A session dies quietly and shows up as four different symptoms: the results list empties,
/// Sverigelistan vanishes, the entry page says "du behöver vara inloggad" and the start field
/// loses its ranking. Each page used to explain its own symptom — "Inga resultat ännu", "Ingen
/// anslutning" — which are true sentences about the wrong thing, and none of them said the one
/// word that would let the runner act: logged out.
/// <para>
/// The silent resume (<see cref="EventorSessionResume"/>) removes most of these before anyone
/// sees them. This is for when it cannot: the password is gone, or replaying it did not work.
/// Then every page says the same thing, because it is the same fact.
/// </para>
/// </remarks>
public static class EventorMessage
{
    /// <summary>The heading, when the login is the reason a page has nothing.</summary>
    public static string Heading(EventorAccess access) => access switch
    {
        EventorAccess.NoSession => "Logga in på Eventor",
        EventorAccess.Expired => "Inloggningen har gått ut",
        EventorAccess.Unreachable => "Ingen kontakt med Eventor",
        _ => string.Empty,
    };

    /// <summary>What follows from it, and what to do.</summary>
    public static string Detail(EventorAccess access, string what) => access switch
    {
        EventorAccess.NoSession =>
            $"{what} läses med din egen inloggning. Logga in under Jag så visas de här.",
        EventorAccess.Expired =>
            $"Eventor känner inte längre igen inloggningen, så {what.ToLowerInvariant()} kan inte "
            + "läsas. Logga in igen under Jag.",
        EventorAccess.Unreachable =>
            "Eventor svarar inte just nu. Det som redan hämtats finns kvar.",
        _ => string.Empty,
    };

    /// <summary>Whether the login is what stands in the way, rather than the data.</summary>
    public static bool Explains(EventorAccess access) =>
        access is EventorAccess.NoSession or EventorAccess.Expired or EventorAccess.Unreachable;
}
