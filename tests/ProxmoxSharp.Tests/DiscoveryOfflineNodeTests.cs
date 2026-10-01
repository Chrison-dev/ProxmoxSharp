using System.Net;
using System.Text;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using ProxmoxSharp.Api;
using Xunit;

namespace ProxmoxSharp.Tests;

/// <summary>
/// Issue #24: one node of the cluster offline (the homelab's Wake-on-LAN desktop-01
/// sleeps most of the time). PVE answers per-node calls for it with HTTP 595, which
/// used to throw out of <see cref="ProxmoxDiscovery.DiscoverAsync"/>. Discovery must
/// return the rest, and still list the offline node's guests (from /cluster/resources)
/// so a consumer never reads them as absent.
/// </summary>
public class DiscoveryOfflineNodeTests
{
    private const string ClusterResources = """
        {"data":[
          {"type":"lxc","vmid":100,"node":"pve1","name":"forgejo","status":"running","maxmem":2147483648,"maxcpu":2},
          {"type":"lxc","vmid":4001,"node":"pve2","name":"monitoring","status":"unknown","maxmem":4294967296,"maxcpu":4,"tags":"iac;monitoring"},
          {"type":"qemu","vmid":1001,"node":"pve2","name":"plex-vm","status":"unknown","maxmem":8589934592,"maxcpu":4}
        ]}
        """;

    // pve1 answers everything; pve2 answers nothing but 595, like a sleeping node.
    // Records every path so tests can assert which calls were (not) made.
    private sealed class StubHandler(string nodesJson) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath.TrimEnd('/');
            Paths.Add(path + request.RequestUri.Query);

            if (path.Contains("/nodes/pve2/"))
            {
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)595)
                {
                    Content = new StringContent("", Encoding.UTF8, "text/plain"),
                    ReasonPhrase = "No route to host",
                });
            }

            var json = path switch
            {
                var p when p.EndsWith("/nodes") => nodesJson,
                var p when p.EndsWith("/cluster/resources") => ClusterResources,
                var p when p.EndsWith("/pve1/lxc") =>
                    """{"data":[{"vmid":100,"name":"forgejo","status":"running","maxmem":2147483648,"cpus":2}]}""",
                _ => """{"data":[]}""",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ProxmoxApiClient Client(HttpMessageHandler handler) =>
        new(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: new HttpClient(handler))
        {
            BaseUrl = "https://proxmox.test/api2/json",
        });

    private static void AssertOfflineNodeFromClusterResources(ClusterSnapshot snapshot)
    {
        Assert.Equal(["pve1", "pve2"], snapshot.Nodes.Select(n => n.Node));

        var pve1 = snapshot.Nodes[0];
        Assert.True(pve1.Reachable);
        Assert.Equal(100, Assert.Single(pve1.Lxc).VmId);

        var pve2 = snapshot.Nodes[1];
        Assert.False(pve2.Reachable);
        var ct = Assert.Single(pve2.Lxc);
        Assert.Equal(4001, ct.VmId);
        Assert.Equal("monitoring", ct.Name);
        Assert.Equal("unknown", ct.Status);
        Assert.Equal(4294967296, ct.MaxMem);
        Assert.Equal(4, ct.Cores);
        Assert.Equal("iac;monitoring", ct.Tags);
        Assert.Equal(1001, Assert.Single(pve2.Qemu).VmId);
        Assert.Empty(pve2.Storage);
        Assert.Empty(pve2.Network);
    }

    [Fact]
    public async Task Offline_node_is_not_queried_and_its_guests_come_from_cluster_resources()
    {
        var handler = new StubHandler("""
            {"data":[{"node":"pve1","status":"online","maxmem":34359738368,"uptime":12345},
                     {"node":"pve2","status":"offline"}]}
            """);

        var snapshot = await new ProxmoxDiscovery(Client(handler)).DiscoverAsync();

        AssertOfflineNodeFromClusterResources(snapshot);
        Assert.DoesNotContain(handler.Paths, p => p.Contains("/nodes/pve2/"));
        Assert.Contains(handler.Paths, p => p.Contains("/cluster/resources") && p.Contains("type=vm"));
    }

    [Fact]
    public async Task Node_listed_online_that_answers_595_is_treated_as_offline()
    {
        // The node went to sleep between /nodes and its own calls.
        var handler = new StubHandler("""
            {"data":[{"node":"pve1","status":"online"},{"node":"pve2","status":"online"}]}
            """);

        var snapshot = await new ProxmoxDiscovery(Client(handler)).DiscoverAsync();

        AssertOfflineNodeFromClusterResources(snapshot);
    }

    [Fact]
    public async Task All_nodes_online_makes_no_cluster_resources_call()
    {
        var handler = new StubHandler("""{"data":[{"node":"pve1","status":"online"}]}""");

        var snapshot = await new ProxmoxDiscovery(Client(handler)).DiscoverAsync();

        Assert.True(Assert.Single(snapshot.Nodes).Reachable);
        Assert.DoesNotContain(handler.Paths, p => p.Contains("/cluster/resources"));
    }
}
