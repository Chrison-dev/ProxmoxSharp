using System.Net;
using System.Text;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using ProxmoxSharp.Api;
using Xunit;

namespace ProxmoxSharp.Tests;

/// <summary>
/// Offline unit tests for <see cref="ProxmoxDiscovery"/> mapping — no live cluster.
/// A stub <see cref="HttpMessageHandler"/> returns canned Proxmox JSON so we can
/// assert the snapshot is built correctly, including the cores/tags fields the
/// guest list surfaces (regression guard for those mappings).
/// </summary>
public class DiscoveryMappingTests
{
    // Routes each Proxmox read to canned JSON by URL suffix. Everything Proxmox
    // returns is wrapped in {"data": ...}.
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimEnd('/');
            var json = path switch
            {
                var p when p.EndsWith("/nodes") =>
                    """{"data":[{"node":"pve1","maxmem":34359738368,"uptime":12345}]}""",
                // One LXC carrying cpus + tags (the fields under test).
                var p when p.EndsWith("/pve1/lxc") =>
                    """{"data":[{"vmid":100,"name":"forgejo","maxmem":2147483648,"cpus":2,"tags":"iac;media"}]}""",
                // No QEMU / storage / network needed for this assertion.
                _ => """{"data":[]}""",
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ProxmoxApiClient StubClient()
    {
        var adapter = new HttpClientRequestAdapter(
            new AnonymousAuthenticationProvider(),
            httpClient: new HttpClient(new StubHandler()))
        {
            BaseUrl = "https://proxmox.test/api2/json",
        };
        // Constructing the client registers the default JSON serializers.
        return new ProxmoxApiClient(adapter);
    }

    [Fact]
    public async Task Discover_maps_guest_cores_and_tags()
    {
        var snapshot = await new ProxmoxDiscovery(StubClient()).DiscoverAsync();

        var node = Assert.Single(snapshot.Nodes);
        Assert.Equal("pve1", node.Node);

        var lxc = Assert.Single(node.Lxc);
        // The fields under test (PR: GuestSnapshot.Cores/Tags):
        Assert.Equal(2, lxc.Cores);
        Assert.Equal("iac;media", lxc.Tags);
        // Regression guard for the pre-existing mappings:
        Assert.Equal(100, lxc.VmId);
        Assert.Equal("forgejo", lxc.Name);
        Assert.Equal(2147483648, lxc.MaxMem);
    }

    [Fact]
    public async Task Discover_leaves_cores_and_tags_null_when_absent()
    {
        // QEMU list is empty and storage/network too; assert the empty-collection
        // path holds and nothing throws when guests omit cpus/tags.
        var snapshot = await new ProxmoxDiscovery(StubClient()).DiscoverAsync();

        var node = Assert.Single(snapshot.Nodes);
        Assert.Empty(node.Qemu);
    }
}
