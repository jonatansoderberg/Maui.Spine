using System.Text.RegularExpressions;

namespace Plugin.Maui.Spine.Server.Messaging.AzureStorage;

/// <summary>Azure Storage's rules for queue names, checked before anything is created so the error names the subscriber.</summary>
internal static partial class StorageQueueNames
{
    public const string PoisonSuffix = "-poison";

    public static string For(string prefix, MessageLane lane) => prefix + lane.Name;

    public static string PoisonFor(string queue) => queue + PoisonSuffix;

    public static void Validate(string queue, MessageLane lane)
    {
        // The poison queue Azure Functions makes must be valid too.
        if (!Rule().IsMatch(PoisonFor(queue)))
            throw new InvalidOperationException(
                $"Queue '{queue}' for subscriber '{lane.Subscriber}' is not a valid Azure Storage queue name with its '{PoisonSuffix}' queue: names are 3–63 lowercase letters, digits and single hyphens. Shorten the subscriber's name or the queue prefix.");
    }

    [GeneratedRegex("^(?=.{3,63}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Rule();
}
