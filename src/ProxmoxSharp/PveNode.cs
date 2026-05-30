using System.Text.Json.Serialization;

namespace ProxmoxSharp;

/// <summary>
/// A Proxmox cluster node as returned by <c>GET /nodes</c>. Minimal hand-written
/// shape for the M2 read path — superseded by the generated model in M3.
/// </summary>
public sealed record PveNode
{
    [JsonPropertyName("node")] public string Node { get; init; } = "";
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("cpu")] public double? Cpu { get; init; }
    [JsonPropertyName("maxcpu")] public int? MaxCpu { get; init; }
    [JsonPropertyName("mem")] public long? Mem { get; init; }
    [JsonPropertyName("maxmem")] public long? MaxMem { get; init; }
    [JsonPropertyName("uptime")] public long? Uptime { get; init; }
}
