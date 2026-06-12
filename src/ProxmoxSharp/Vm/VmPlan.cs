namespace ProxmoxSharp.Vm;

/// <summary>What the reconciler decided to do for a VM.</summary>
public enum VmActionKind
{
    /// <summary>VM does not exist → create it.</summary>
    Create,
    /// <summary>VM exists but some config keys differ → apply only those.</summary>
    SetConfig,
    /// <summary>VM exists and already satisfies the desired state → no-op.</summary>
    Skip,
}

/// <summary>A single config key the reconciler will set (with its prior value, if any).</summary>
public sealed record PlannedChange(string Key, string? From, string To)
{
    public override string ToString() =>
        From is null ? $"+ {Key} = {To}" : $"~ {Key}: {From} -> {To}";
}

/// <summary>
/// The reconciler's verdict for one VM: the action, the exact config changes it
/// carries, and any live keys the desired spec doesn't manage (reported, never
/// removed — adoption stays non-destructive).
/// </summary>
public sealed record VmPlan(
    string Node,
    int Vmid,
    VmActionKind Kind,
    IReadOnlyList<PlannedChange> Changes,
    IReadOnlyList<string> UnmanagedKeys)
{
    public bool HasChanges => Changes.Count > 0;
}
