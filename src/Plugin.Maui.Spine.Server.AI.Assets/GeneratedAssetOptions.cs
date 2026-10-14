namespace Plugin.Maui.Spine.Server.AI.Assets;

/// <summary>A feature's asset settings, under <c>Ai:Features:&lt;name&gt;:Asset</c>.</summary>
public sealed class GeneratedAssetOptions
{
    /// <summary>
    /// The images' version, part of every path. Raise it when the prompt or the model changes: every key is then
    /// missing, and images are made again one at a time as they are asked for. Old versions stay archived.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>Whether <c>GET /ai/assets/{feature}/{key}</c> answers for the feature. Server code can always look up.</summary>
    public bool Expose { get; set; }

    /// <summary>How long a failed image waits before a lookup orders it again. One hour by default.</summary>
    public TimeSpan FailureCoolDown { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Orders per minute and caller. Lookups of ready and pending images do not count.</summary>
    public AICallerRates? OrdersPerMinute { get; set; }

    /// <summary>
    /// The images' media type, asked of the model and used for the file extension. Unset, <c>image/png</c>, and the
    /// model's default format is not overridden.
    /// </summary>
    public string? MediaType { get; set; }
}
