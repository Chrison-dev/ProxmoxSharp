using Xunit;

namespace ProxmoxSharp.Tests;

/// <summary>
/// Read-only integration tests that exercise the Kiota-generated client (M3),
/// proving the full apidoc.js → OpenAPI → Kiota → live-call pipeline. Skips when
/// no <c>secrets.env</c> is present.
/// </summary>
public class GeneratedClientTests
{
    [SkippableFact]
    public async Task Generated_client_reads_version()
    {
        var options = SecretsEnv.TryLoadOptions();
        Skip.If(options is null, "No secrets.env with PROXMOX_* — skipping live read.");

        var client = ProxmoxApi.Create(options!);

        var response = await client.Version.GetAsVersionGetResponseAsync();

        Assert.NotNull(response);
        Assert.NotNull(response!.Data);
        Assert.False(string.IsNullOrWhiteSpace(response.Data!.Version));
    }

    [SkippableFact]
    public async Task Generated_client_reads_nodes()
    {
        var options = SecretsEnv.TryLoadOptions();
        Skip.If(options is null, "No secrets.env with PROXMOX_* — skipping live read.");

        var client = ProxmoxApi.Create(options!);

        var response = await client.Nodes.GetAsNodesGetResponseAsync();

        Assert.NotNull(response);
        Assert.NotNull(response!.Data);
        Assert.NotEmpty(response.Data!);
    }
}
