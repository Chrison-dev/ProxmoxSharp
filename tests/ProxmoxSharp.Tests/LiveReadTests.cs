using Xunit;

namespace ProxmoxSharp.Tests;

/// <summary>
/// Read-only integration tests against a live Proxmox cluster. They run only when
/// a <c>secrets.env</c> (with PROXMOX_*) is present at the repo root; otherwise
/// they skip. Use a dedicated read-only token (e.g. role PVEAuditor).
/// </summary>
public class LiveReadTests
{
    [SkippableFact]
    public async Task GetNodes_returns_the_cluster_nodes()
    {
        var options = SecretsEnv.TryLoadOptions();
        Skip.If(options is null, "No secrets.env with PROXMOX_* — skipping live read.");

        using var client = new ProxmoxClient(options!);
        var nodes = await client.GetNodesAsync();

        Assert.NotEmpty(nodes);
        Assert.All(nodes, node => Assert.False(string.IsNullOrWhiteSpace(node.Node)));
    }
}
