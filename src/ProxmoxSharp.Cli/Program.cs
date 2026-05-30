using System.Text.Json;
using ProxmoxSharp;

// proxmoxsharp — a thin CLI over the ProxmoxSharp library (read-only).
//
// Commands: discover | nodes | version
// Config (env): PROXMOX_BASE_URL, PROXMOX_TOKEN_ID, PROXMOX_TOKEN_SECRET,
//               PROXMOX_VERIFY_TLS (optional, 'false' for self-signed nodes)

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine(
        """
        proxmoxsharp — read-only Proxmox VE client

        Usage: proxmoxsharp <command>
          discover   Dump a structured ClusterSnapshot as JSON
          nodes      List cluster nodes
          version    Show the PVE version

        Config (env): PROXMOX_BASE_URL, PROXMOX_TOKEN_ID, PROXMOX_TOKEN_SECRET,
                      PROXMOX_VERIFY_TLS (optional, 'false' for self-signed)
        """);
    return 0;
}

var options = LoadOptions();
if (options is null)
{
    Console.Error.WriteLine(
        "Missing PVE config. Set PROXMOX_BASE_URL, PROXMOX_TOKEN_ID, PROXMOX_TOKEN_SECRET " +
        "(and optionally PROXMOX_VERIFY_TLS=false for self-signed nodes).");
    return 2;
}

var client = ProxmoxApi.Create(options);

switch (command)
{
    case "discover":
        var snapshot = await new ProxmoxDiscovery(client).DiscoverAsync();
        Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));
        return 0;

    case "nodes":
        var nodes = await client.Nodes.GetAsNodesGetResponseAsync();
        foreach (var n in nodes?.Data ?? [])
        {
            Console.WriteLine($"{n.Node,-14}{n.Status}");
        }
        return 0;

    case "version":
        var version = await client.Version.GetAsVersionGetResponseAsync();
        Console.WriteLine(version?.Data?.Version ?? "(unknown)");
        return 0;

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Try: discover | nodes | version");
        return 1;
}

static ProxmoxClientOptions? LoadOptions()
{
    var baseUrl = Environment.GetEnvironmentVariable("PROXMOX_BASE_URL");
    var tokenId = Environment.GetEnvironmentVariable("PROXMOX_TOKEN_ID");
    var secret = Environment.GetEnvironmentVariable("PROXMOX_TOKEN_SECRET");
    if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(tokenId) || string.IsNullOrEmpty(secret))
    {
        return null;
    }

    var verifyTls = !string.Equals(
        Environment.GetEnvironmentVariable("PROXMOX_VERIFY_TLS"), "false", StringComparison.OrdinalIgnoreCase);

    return new ProxmoxClientOptions
    {
        BaseUrl = new Uri(baseUrl),
        TokenId = tokenId,
        TokenSecret = secret,
        VerifyTls = verifyTls,
    };
}
