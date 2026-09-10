namespace Plugin.Maui.Spine.Common;

/// <summary>
/// Where a Live Activity channel lives on each platform. On iOS it is an APNs broadcast channel, named
/// by the id APNs gave it. Android has no channels, so the same id names an FCM topic the app follows
/// while an activity on the channel runs. The server and the app both derive the topic here, so the
/// two cannot disagree.
/// </summary>
public static class LiveActivityChannels
{
    private const string TopicPrefix = "spine-la-";

    /// <summary>The FCM topic for <paramref name="channel"/>.</summary>
    /// <param name="channel">An APNs channel id, or a name made up for Android alone.</param>
    /// <returns>A name FCM accepts as a topic.</returns>
    /// <remarks>
    /// An APNs channel id is base64, and FCM allows only letters, digits and <c>-_.~%</c> in a topic
    /// name, so the id is written as base64url: <c>+</c> and <c>/</c> become <c>-</c> and <c>_</c>, and
    /// the padding goes.
    /// </remarks>
    /// <exception cref="ArgumentException">The channel has characters no topic can carry.</exception>
    public static string Topic(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var topic = TopicPrefix + channel.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (topic.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '~' or '%')))
            throw new ArgumentException($"\"{channel}\" cannot be written as an FCM topic name.", nameof(channel));

        return topic;
    }
}
