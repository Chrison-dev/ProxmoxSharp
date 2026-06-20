using System.Text.Json;
using ProxmoxSharp;
using ProxmoxSharp.Lxc;
using ProxmoxSharp.Vm;

// proxmoxsharp — a thin CLI over the ProxmoxSharp library.
//
// Commands: discover | nodes | version | vm <…> | lxc <start|stop|shutdown|delete>
// Config (env): PROXMOX_BASE_URL, PROXMOX_TOKEN_ID, PROXMOX_TOKEN_SECRET,
//               PROXMOX_VERIFY_TLS (optional, 'false' for self-signed nodes)

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine(
        """
        proxmoxsharp — Proxmox VE client (read + VM write path)

        Usage: proxmoxsharp <command>
          discover            Dump a structured ClusterSnapshot as JSON
          nodes               List cluster nodes
          version             Show the PVE version

          vm plan  <spec.json>            Diff a VM spec against live state (dry-run)
          vm apply <spec.json> [--confirm]  Apply the diff (dry-run unless --confirm)
          vm start    <node> <vmid>      Start a VM
          vm stop     <node> <vmid>      Hard-stop a VM
          vm delete   <node> <vmid> --confirm [--no-purge]   Destroy a VM
          vm pci      <node>             List PCI devices (passthrough discovery)
          vm show     <node> <vmid>      Dump a VM's live config

          lxc start    <node> <vmid>     Start a container
          lxc stop     <node> <vmid>     Hard-stop (kill) a container
          lxc shutdown <node> <vmid>     Graceful shutdown (hard-stop fallback)
          lxc delete   <node> <vmid> --confirm [--force] [--no-purge]   Destroy a container

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

    case "vm":
        return await RunVm(args, options);

    case "lxc":
        return await RunLxc(args, options);

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Try: discover | nodes | version | vm | lxc");
        return 1;
}

// vm <plan|apply|start|stop|delete|pci|show> — the VM write path (#115).
static async Task<int> RunVm(string[] args, ProxmoxClientOptions options)
{
    var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "";
    var writer = QemuWriter.Create(options);

    switch (sub)
    {
        case "plan":
        case "apply":
        {
            if (args.Length < 3) { Console.Error.WriteLine($"usage: proxmoxsharp vm {sub} <spec.json> [--confirm]"); return 2; }
            var spec = LoadSpec(args[2]);
            if (spec is null) return 2;
            var live = await writer.GetConfigRawAsync(spec.Node, spec.Vmid);
            var plan = VmReconciler.Reconcile(spec, live);
            PrintPlan(plan);

            if (sub == "plan" || !args.Contains("--confirm"))
            {
                if (plan.HasChanges) Console.WriteLine("\n[dry-run] re-run `vm apply <spec> --confirm` to apply.");
                return 0;
            }
            if (plan.Kind == VmActionKind.Skip) { Console.WriteLine("\nNothing to apply."); return 0; }

            string? upid;
            if (plan.Kind == VmActionKind.Create)
            {
                Console.WriteLine($"\nCreating VM {spec.Vmid} on {spec.Node}…");
                upid = await writer.CreateAsync(spec);
            }
            else
            {
                var changes = plan.Changes.ToDictionary(c => c.Key, c => c.To, StringComparer.Ordinal);
                Console.WriteLine($"\nApplying {changes.Count} config change(s) to VM {spec.Vmid} on {spec.Node}…");
                upid = await writer.SetConfigAsync(spec.Node, spec.Vmid, changes);
            }
            await WaitAndReport(writer, spec.Node, upid);
            return 0;
        }

        case "start":
        case "stop":
        {
            if (!TryNodeVmid(args, out var node, out var vmid)) return 2;
            var upid = sub == "start" ? await writer.StartAsync(node, vmid) : await writer.StopAsync(node, vmid);
            await WaitAndReport(writer, node, upid);
            return 0;
        }

        case "delete":
        {
            if (!TryNodeVmid(args, out var node, out var vmid)) return 2;
            if (!args.Contains("--confirm")) { Console.Error.WriteLine("Refusing to delete without --confirm."); return 2; }
            var purge = !args.Contains("--no-purge");
            Console.WriteLine($"Destroying VM {vmid} on {node} (purge={purge})…");
            var upid = await writer.DeleteAsync(node, vmid, purge);
            await WaitAndReport(writer, node, upid);
            return 0;
        }

        case "pci":
        {
            if (args.Length < 3) { Console.Error.WriteLine("usage: proxmoxsharp vm pci <node>"); return 2; }
            foreach (var d in await writer.ListPciAsync(args[2]))
                Console.WriteLine($"{d.Id,-12} iommu={d.IommuGroup?.ToString() ?? "-",-4} {d.VendorName} {d.DeviceName}");
            return 0;
        }

        case "show":
        {
            if (!TryNodeVmid(args, out var node, out var vmid)) return 2;
            var cfg = await writer.GetConfigRawAsync(node, vmid);
            if (cfg is null) { Console.Error.WriteLine($"VM {vmid} not found on {node}."); return 1; }
            foreach (var (k, v) in cfg.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                Console.WriteLine($"{k}: {v}");
            return 0;
        }

        default:
            Console.Error.WriteLine("usage: proxmoxsharp vm <plan|apply|start|stop|delete|pci|show>");
            return 2;
    }
}

// lxc <start|stop|shutdown|delete> — the LXC lifecycle write path (#149).
static async Task<int> RunLxc(string[] args, ProxmoxClientOptions options)
{
    var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "";
    var writer = PctWriter.Create(options);

    switch (sub)
    {
        case "start":
        case "stop":
        case "shutdown":
        {
            if (!TryNodeVmid(args, out var node, out var vmid)) return 2;
            var upid = sub switch
            {
                "start" => await writer.StartAsync(node, vmid),
                "stop" => await writer.StopAsync(node, vmid),
                _ => await writer.ShutdownAsync(node, vmid),
            };
            await WaitAndReportLxc(writer, node, upid);
            return 0;
        }

        case "delete":
        {
            if (!TryNodeVmid(args, out var node, out var vmid)) return 2;
            if (!args.Contains("--confirm")) { Console.Error.WriteLine("Refusing to delete without --confirm."); return 2; }
            var purge = !args.Contains("--no-purge");
            var force = args.Contains("--force");
            Console.WriteLine($"Destroying LXC {vmid} on {node} (force={force}, purge={purge})…");
            var upid = await writer.DeleteAsync(node, vmid, force, purge);
            await WaitAndReportLxc(writer, node, upid);
            return 0;
        }

        default:
            Console.Error.WriteLine("usage: proxmoxsharp lxc <start|stop|shutdown|delete> <node> <vmid> [--force] [--no-purge] --confirm");
            return 2;
    }
}

static bool TryNodeVmid(string[] args, out string node, out int vmid)
{
    node = ""; vmid = 0;
    if (args.Length < 4 || !int.TryParse(args[3], out vmid))
    {
        var cmd = args.Length > 0 ? args[0] : "vm";
        Console.Error.WriteLine($"usage: proxmoxsharp {cmd} {(args.Length > 1 ? args[1] : "<sub>")} <node> <vmid> …");
        return false;
    }
    node = args[2];
    return true;
}

static QemuVmSpec? LoadSpec(string path)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"Spec not found: {path}"); return null; }
    try
    {
        return JsonSerializer.Deserialize<QemuVmSpec>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to parse spec {path}: {ex.Message}");
        return null;
    }
}

static void PrintPlan(VmPlan plan)
{
    Console.WriteLine($"VM {plan.Vmid} on {plan.Node}: {plan.Kind}");
    if (!plan.HasChanges) Console.WriteLine("  (no changes — desired state already satisfied)");
    foreach (var c in plan.Changes) Console.WriteLine($"  {c}");
    if (plan.UnmanagedKeys.Count > 0)
        Console.WriteLine($"  (unmanaged live keys, left untouched: {string.Join(", ", plan.UnmanagedKeys)})");
}

static async Task WaitAndReport(QemuWriter writer, string node, string? upid)
{
    if (string.IsNullOrEmpty(upid)) { Console.WriteLine("Done (no task)."); return; }
    Console.WriteLine($"Task {upid} — waiting…");
    var exit = await writer.WaitForTaskAsync(node, upid);
    Console.WriteLine($"Task finished: {exit ?? "(no exit status)"}");
}

static async Task WaitAndReportLxc(PctWriter writer, string node, string? upid)
{
    if (string.IsNullOrEmpty(upid)) { Console.WriteLine("Done (no task)."); return; }
    Console.WriteLine($"Task {upid} — waiting…");
    var exit = await writer.WaitForTaskAsync(node, upid);
    Console.WriteLine($"Task finished: {exit ?? "(no exit status)"}");
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
