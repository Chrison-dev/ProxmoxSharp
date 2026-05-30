using ProxmoxSharp.Api;

namespace ProxmoxSharp;

/// <summary>
/// Read-only discovery (M4): walks the cluster via the generated client and
/// produces a structured <see cref="ClusterSnapshot"/> — nodes and the
/// guests/storage/network they host. This is the in-code, repeatable replacement
/// for the earlier MCP-driven sweep, and the input the hub reconciles against the
/// <c>/Infrastructure</c> shapes.
/// </summary>
public sealed class ProxmoxDiscovery
{
    private readonly ProxmoxApiClient _client;

    public ProxmoxDiscovery(ProxmoxApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>Builds a snapshot of the whole cluster (1 + 4×N read calls, N = node count).</summary>
    public async Task<ClusterSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var nodes = (await _client.Nodes.GetAsNodesGetResponseAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))?.Data ?? [];

        var snapshots = new List<NodeSnapshot>(nodes.Count);
        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.Node))
            {
                continue;
            }

            var nodeBuilder = _client.Nodes[node.Node];

            var lxc = (await nodeBuilder.Lxc.GetAsLxcGetResponseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var qemu = (await nodeBuilder.Qemu.GetAsQemuGetResponseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var storage = (await nodeBuilder.Storage.GetAsStorageGetResponseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];
            var network = (await nodeBuilder.Network.GetAsNetworkGetResponseAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))?.Data ?? [];

            snapshots.Add(new NodeSnapshot
            {
                Node = node.Node,
                Status = node.Status?.ToString(),
                MaxMem = node.Maxmem,
                Uptime = node.Uptime,
                Lxc = lxc.Select(g => new GuestSnapshot
                {
                    VmId = g.Vmid,
                    Name = g.Name,
                    Status = g.Status?.ToString(),
                    MaxMem = g.Maxmem,
                }).ToList(),
                Qemu = qemu.Select(g => new GuestSnapshot
                {
                    VmId = g.Vmid,
                    Name = g.Name,
                    Status = g.Status?.ToString(),
                    MaxMem = g.Maxmem,
                }).ToList(),
                Storage = storage.Select(s => new StorageSnapshot
                {
                    Storage = s.Storage,
                    Type = s.Type,
                    Active = s.Active,
                    Content = s.Content,
                }).ToList(),
                Network = network.Select(n => new NetworkSnapshot
                {
                    Iface = n.Iface,
                    Type = n.Type?.ToString(),
                    Address = n.Address,
                }).ToList(),
            });
        }

        return new ClusterSnapshot { Nodes = snapshots };
    }
}
