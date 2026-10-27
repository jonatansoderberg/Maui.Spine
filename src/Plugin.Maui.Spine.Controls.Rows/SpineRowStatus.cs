namespace Plugin.Maui.Spine.Controls;

/// <summary>What a <see cref="SpineRow"/> says about the thing it stands for, as a mark at its end.</summary>
public enum SpineRowStatus
{
    /// <summary>No mark.</summary>
    None,

    /// <summary>Set up and working: a white check on a green circle.</summary>
    Ok,

    /// <summary>Needs the user, such as a missing key or a sign-in that ran out: a white "!" on an orange circle.</summary>
    NeedsAttention,
}
