using System.Text;
using System.Text.Json;
using Microsoft.Kiota.Abstractions;
using ProxmoxSharp.Api;

namespace ProxmoxSharp.Vm;

/// <summary>
/// L1 of the VM write path: the thin layer that actually touches the wire.
/// <para>
/// create / setConfig / delete carry Proxmox's indexed params (hostpci0/scsi0/…),
/// which the generated typed query surface can't express, so they're sent as a
/// form-urlencoded body over the shared Kiota adapter (same token-auth + TLS as the
/// read client). start / stop / shutdown / pci / task-status have no such params and
/// use the generated builders directly. All mutating ops return Proxmox's task UPID;
/// poll it with <see cref="WaitForTaskAsync"/>.
/// </para>
/// </summary>
public sealed class QemuWriter
{
    private readonly IRequestAdapter _adapter;
    private readonly ProxmoxApiClient _client;

    public QemuWriter(IRequestAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
        _client = new ProxmoxApiClient(adapter);
    }

    /// <summary>Build a writer with the same auth/TLS wiring as <see cref="ProxmoxApi.Create"/>.</summary>
    public static QemuWriter Create(ProxmoxClientOptions options) => new(ProxmoxApi.CreateAdapter(options));

    private string BaseUrl => _adapter.BaseUrl ?? throw new InvalidOperationException("Adapter has no BaseUrl.");

    // --- reads -------------------------------------------------------------

    /// <summary>
    /// Live config of a VM as a flat key→value dict (concrete keys: scsi0, hostpci0, …),
    /// the input <see cref="VmReconciler"/> diffs against. Returns null if the VM does
    /// not exist (so the reconciler plans a create).
    /// </summary>
    public async Task<Dictionary<string, string>?> GetConfigRawAsync(string node, int vmid, CancellationToken ct = default)
    {
        var ri = new RequestInformation(Method.GET, "{+baseurl}/nodes/{node}/qemu/{vmid}/config", PathParams(node, vmid));
        ri.Headers.TryAdd("Accept", "application/json");
        Stream? stream;
        try
        {
            stream = await _adapter.SendPrimitiveAsync<Stream>(ri, errorMapping: null, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (ApiException)
        {
            // Proxmox returns an error (not 404) when the VM config doesn't exist → treat as absent.
            return null;
        }
        if (stream is null) return null;

        await using var _ = stream.ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;

        var config = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in data.EnumerateObject())
        {
            config[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                ? prop.Value.GetString()!
                : prop.Value.GetRawText();
        }
        return config;
    }

    /// <summary>PCI devices on a node (for passthrough validation: does 0000:09:00 exist, its IOMMU group, …).</summary>
    public async Task<IReadOnlyList<PciDevice>> ListPciAsync(string node, CancellationToken ct = default)
    {
        var resp = await _client.Nodes[node].Hardware.Pci.GetAsPciGetResponseAsync(cancellationToken: ct).ConfigureAwait(false);
        return (resp?.Data ?? [])
            .Select(d => new PciDevice(d.Id, d.DeviceName, d.VendorName, d.Iommugroup))
            .ToList();
    }

    // --- writes (return a task UPID) ---------------------------------------

    /// <summary>Create a VM (POST /nodes/{node}/qemu). The full encoded config + vmid go in the form body.</summary>
    public Task<string?> CreateAsync(QemuVmSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var form = new Dictionary<string, string>(QemuParamEncoder.Encode(spec), StringComparer.Ordinal)
        {
            ["vmid"] = spec.Vmid.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return SendFormAsync(Method.POST, "{+baseurl}/nodes/{node}/qemu",
            new Dictionary<string, object> { ["baseurl"] = BaseUrl, ["node"] = spec.Node }, form, ct);
    }

    /// <summary>Apply only the given config keys (PUT …/{vmid}/config). Sync on a stopped VM; a UPID on a running one.</summary>
    public Task<string?> SetConfigAsync(string node, int vmid, IReadOnlyDictionary<string, string> changes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0) return Task.FromResult<string?>(null);
        return SendFormAsync(Method.PUT, "{+baseurl}/nodes/{node}/qemu/{vmid}/config", PathParams(node, vmid),
            new Dictionary<string, string>(changes, StringComparer.Ordinal), ct);
    }

    public async Task<string?> StartAsync(string node, int vmid, CancellationToken ct = default) =>
        (await _client.Nodes[node].Qemu[(long)vmid].Status.Start.PostAsStartPostResponseAsync(cancellationToken: ct).ConfigureAwait(false))?.Data;

    /// <summary>Graceful ACPI shutdown (guest agent / power button).</summary>
    public async Task<string?> ShutdownAsync(string node, int vmid, CancellationToken ct = default) =>
        (await _client.Nodes[node].Qemu[(long)vmid].Status.Shutdown.PostAsShutdownPostResponseAsync(cancellationToken: ct).ConfigureAwait(false))?.Data;

    /// <summary>Hard stop (pull the power).</summary>
    public async Task<string?> StopAsync(string node, int vmid, CancellationToken ct = default) =>
        (await _client.Nodes[node].Qemu[(long)vmid].Status.Stop.PostAsStopPostResponseAsync(cancellationToken: ct).ConfigureAwait(false))?.Data;

    /// <summary>Destroy a VM. <paramref name="purge"/> also removes its disks + any refs (jobs, HA, …).</summary>
    public Task<string?> DeleteAsync(string node, int vmid, bool purge = true, CancellationToken ct = default)
    {
        var query = purge ? "?purge=1&destroy-unreferenced-disks=1" : "";
        return SendFormAsync(Method.DELETE, "{+baseurl}/nodes/{node}/qemu/{vmid}" + query, PathParams(node, vmid), form: null, ct);
    }

    // --- task polling ------------------------------------------------------

    /// <summary>
    /// Poll a task UPID until it leaves the "running" state; returns its exit status
    /// ("OK" on success). Throws on timeout.
    /// </summary>
    public async Task<string?> WaitForTaskAsync(string node, string upid, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
        while (true)
        {
            var status = await _client.Nodes[node].Tasks[upid].Status
                .GetAsStatusGetResponseAsync(cancellationToken: ct).ConfigureAwait(false);
            var data = status?.Data;
            // data.Status is a generated enum (Running | Stopped); ToString() gives the member name.
            if (data is not null && !string.Equals(data.Status?.ToString(), "running", StringComparison.OrdinalIgnoreCase))
                return data.Exitstatus;
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"Task {upid} did not finish within the timeout.");
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    // --- internals ---------------------------------------------------------

    private Dictionary<string, object> PathParams(string node, int vmid) => new()
    {
        ["baseurl"] = BaseUrl,
        ["node"] = node,
        ["vmid"] = Id(vmid),
    };

    private static string Id(int vmid) => vmid.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string?> SendFormAsync(
        Method method, string urlTemplate, Dictionary<string, object> pathParams,
        IReadOnlyDictionary<string, string>? form, CancellationToken ct)
    {
        var ri = new RequestInformation(method, urlTemplate, pathParams);
        ri.Headers.TryAdd("Accept", "application/json");
        if (form is not null)
        {
            var body = string.Join("&", form.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            ri.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)), "application/x-www-form-urlencoded");
        }

        var stream = await _adapter.SendPrimitiveAsync<Stream>(ri, errorMapping: null, cancellationToken: ct).ConfigureAwait(false);
        if (stream is null) return null;
        await using var _ = stream.ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
    }
}

/// <summary>A PCI device reported by a node (subset relevant to passthrough).</summary>
public sealed record PciDevice(string? Id, string? DeviceName, string? VendorName, long? IommuGroup);
