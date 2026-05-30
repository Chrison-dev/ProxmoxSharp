using Xunit;

namespace ProxmoxSharp.Tests;

/// <summary>
/// Read-only integration test for M4 discovery against the live cluster.
/// Skips when no <c>secrets.env</c> is present.
/// </summary>
public class DiscoveryTests
{
    [SkippableFact]
    public async Task Discover_returns_nodes_with_their_guests()
    {
        var options = SecretsEnv.TryLoadOptions();
        Skip.If(options is null, "No secrets.env with PROXMOX_* — skipping live discovery.");

        var client = ProxmoxApi.Create(options!);
        var snapshot = await new ProxmoxDiscovery(client).DiscoverAsync();

        Assert.NotEmpty(snapshot.Nodes);
        Assert.All(snapshot.Nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Node)));
        // The homelab runs many LXCs, so at least one node should report some.
        Assert.Contains(snapshot.Nodes, n => n.Lxc.Count > 0);
    }
}
