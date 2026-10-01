using Microsoft.Kiota.Abstractions;
using ProxmoxSharp.Api;
using ProxmoxSharp.Api.Cluster.Resources;
using ProxmoxSharp.Api.Nodes;

namespace ProxmoxSharp;

/// <summary>
/// Read-only discovery (M4): walks the cluster via the generated client and
/// produces a structured <see cref="ClusterSnapshot"/> — nodes and the
/// guests/storage/network they host. This is the in-code, repeatable replacement
/// for the earlier MCP-driven sweep, and the input the hub reconciles against the
/// <c>/Infrastructure</c> shapes.
/// </summary>
/// <remarks>
/// A node that is not online (powered off, asleep, partitioned) cannot answer
/// per-node calls: PVE proxies them and returns HTTP 595 "no route to host". Such
/// a node comes back with <see cref="NodeSnapshot.Reachable"/> = <c>false</c>, no
/// storage/network, and its guests taken from the cluster-wide
/// <c>/cluster/resources</c>, which still lists them from the shared config.
/// Leaving those guests out instead would make a consumer read "absent" and plan
/// to create them.
/// </remarks>
public sealed class ProxmoxDiscovery
{
    // PVE's proxy status when the target node is unreachable.
    private const int NoRouteToNode = 595;

    private readonly ProxmoxApiClient _client;

    public ProxmoxDiscovery(ProxmoxApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <summary>
    /// Builds a snapshot of the whole cluster (1 + 4×N read calls, N = online node
    /// count, plus one <c>/cluster/resources</c> call if any node is unreachable).
    /// </summary>
    public async Task<ClusterSnapshot> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var nodes = (await _client.Nodes.GetAsNodesGetResponseAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))?.Data ?? [];

        List<ResourcesGetResponse_data>? clusterGuests = null;
        var snapshots = new List<NodeSnapshot>(nodes.Count);
        foreach (var node in nodes)
        {
            if (string.IsNullOrEmpty(node.Node))
            {
                continue;
            }

            NodeSnapshot? snapshot = null;
            // A missing status is still tried: the 595 catch covers it if the node is down.
            if (node.Status is not (NodesGetResponse_data_status.Offline or NodesGetResponse_data_status.Unknown))
            {
                try
                {
                    snapshot = await DiscoverOnlineNodeAsync(node, cancellationToken).ConfigureAwait(false);
                }
                catch (ApiException ex) when (ex.ResponseStatusCode == NoRouteToNode)
                {
                    // Went away between the node list and its own calls: treat as offline.
                }
            }

            if (snapshot is null)
            {
                clusterGuests ??= await ClusterGuestsAsync(cancellationToken).ConfigureAwait(false);
                snapshot = UnreachableNode(node, clusterGuests);
            }

            snapshots.Add(snapshot);
        }

        return new ClusterSnapshot { Nodes = snapshots };
    }

    private async Task<NodeSnapshot> DiscoverOnlineNodeAsync(NodesGetResponse_data node, CancellationToken cancellationToken)
    {
        var nodeBuilder = _client.Nodes[node.Node];

        var lxc = (await nodeBuilder.Lxc.GetAsLxcGetResponseAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))?.Data ?? [];
        var qemu = (await nodeBuilder.Qemu.GetAsQemuGetResponseAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))?.Data ?? [];
        var storage = (await nodeBuilder.Storage.GetAsStorageGetResponseAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))?.Data ?? [];
        var network = (await nodeBuilder.Network.GetAsNetworkGetResponseAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))?.Data ?? [];

        return new NodeSnapshot
        {
            Node = node.Node!,
            Status = node.Status?.ToString(),
            MaxMem = node.Maxmem,
            Uptime = node.Uptime,
            Lxc = lxc.Select(g => new GuestSnapshot
            {
                VmId = g.Vmid,
                Name = g.Name,
                Status = g.Status?.ToString(),
                MaxMem = g.Maxmem,
                Cores = g.Cpus is { } c ? (int)c : null,
                Tags = g.Tags,
            }).ToList(),
            Qemu = qemu.Select(g => new GuestSnapshot
            {
                VmId = g.Vmid,
                Name = g.Name,
                Status = g.Status?.ToString(),
                MaxMem = g.Maxmem,
                Cores = g.Cpus is { } c ? (int)c : null,
                Tags = g.Tags,
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
        };
    }

    // Answered by whichever node we are talking to, from the shared cluster config,
    // so it lists guests on offline nodes too (with status "unknown").
    private async Task<List<ResourcesGetResponse_data>> ClusterGuestsAsync(CancellationToken cancellationToken) =>
        (await _client.Cluster.Resources.GetAsResourcesGetResponseAsync(
                rc => rc.QueryParameters.TypeAsGetTypeQueryParameterType = GetTypeQueryParameterType.Vm,
                cancellationToken).ConfigureAwait(false))?.Data ?? [];

    private static NodeSnapshot UnreachableNode(NodesGetResponse_data node, List<ResourcesGetResponse_data> clusterGuests)
    {
        var hosted = clusterGuests.Where(r => string.Equals(r.Node, node.Node, StringComparison.Ordinal)).ToList();
        return new NodeSnapshot
        {
            Node = node.Node!,
            Status = node.Status?.ToString(),
            MaxMem = node.Maxmem,
            Uptime = node.Uptime,
            Reachable = false,
            Lxc = hosted.Where(r => r.Type == ResourcesGetResponse_data_type.Lxc).Select(FromResource).ToList(),
            Qemu = hosted.Where(r => r.Type == ResourcesGetResponse_data_type.Qemu).Select(FromResource).ToList(),
        };
    }

    private static GuestSnapshot FromResource(ResourcesGetResponse_data r) => new()
    {
        VmId = r.Vmid,
        Name = r.Name,
        Status = r.Status,
        MaxMem = r.Maxmem,
        Cores = r.Maxcpu is { } c ? (int)c : null,
        Tags = r.Tags,
    };
}
