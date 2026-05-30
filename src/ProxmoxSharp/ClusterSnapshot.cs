namespace ProxmoxSharp;

/// <summary>A structured, read-only snapshot of live cluster state (the M4 discover output).</summary>
public sealed record ClusterSnapshot
{
    public required IReadOnlyList<NodeSnapshot> Nodes { get; init; }
}

/// <summary>A node and the guests/storage/network it hosts.</summary>
public sealed record NodeSnapshot
{
    public required string Node { get; init; }
    public string? Status { get; init; }
    public long? MaxMem { get; init; }
    public long? Uptime { get; init; }
    public IReadOnlyList<GuestSnapshot> Lxc { get; init; } = [];
    public IReadOnlyList<GuestSnapshot> Qemu { get; init; } = [];
    public IReadOnlyList<StorageSnapshot> Storage { get; init; } = [];
    public IReadOnlyList<NetworkSnapshot> Network { get; init; } = [];
}

/// <summary>An LXC or QEMU guest.</summary>
public sealed record GuestSnapshot
{
    public long? VmId { get; init; }
    public string? Name { get; init; }
    public string? Status { get; init; }
    public long? MaxMem { get; init; }
}

public sealed record StorageSnapshot
{
    public string? Storage { get; init; }
    public string? Type { get; init; }
    public bool? Active { get; init; }
    public string? Content { get; init; }
}

public sealed record NetworkSnapshot
{
    public string? Iface { get; init; }
    public string? Type { get; init; }
    public string? Address { get; init; }
}
