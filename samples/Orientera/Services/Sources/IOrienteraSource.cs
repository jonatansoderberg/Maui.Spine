namespace Orientera.Services.Sources;

/// <summary>
/// Everything the app reads, in one seam. The views depend on the narrow interfaces; the
/// composition root swaps one implementation of this for another — fake dataset or backend —
/// without any of them knowing.
/// </summary>
public interface IOrienteraSource
    : IEventSource, IPeopleSource, IParticipationSource, ILiveSource, IProgressSource, ILiveloxSource;
