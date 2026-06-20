using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using ProxmoxSharp.Api;
using ProxmoxSharp.Vm; // ProxmoxApiError (internal, same assembly)

namespace ProxmoxSharp.Lxc;

/// <summary>
/// LXC lifecycle write path (issue #149): start / stop / shutdown / delete for
/// containers, the counterpart to <see cref="QemuWriter"/>. Gives the converge
/// destroy lifecycle (and the CLI) an IaC way to tear a CT down — no root SSH + pct.
/// <para>
/// Each lifecycle op carries Proxmox query flags (forceStop/timeout/force/purge), so —
/// like <see cref="QemuWriter.DeleteAsync"/> — they go over the shared Kiota adapter as
/// raw form requests (same token-auth + TLS as the read client) rather than the typed
/// builders. All mutating ops return Proxmox's task UPID; poll it with
/// <see cref="WaitForTaskAsync"/>. The form-send + task-poll helpers intentionally mirror
/// QemuWriter; hoist a shared base if a third writer ever appears.
/// </para>
/// </summary>
public sealed class PctWriter
{
    private readonly IRequestAdapter _adapter;
    private readonly ProxmoxApiClient _client;

    public PctWriter(IRequestAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
        _client = new ProxmoxApiClient(adapter);
    }

    /// <summary>Build a writer with the same auth/TLS wiring as <see cref="ProxmoxApi.Create"/>.</summary>
    public static PctWriter Create(ProxmoxClientOptions options) => new(ProxmoxApi.CreateAdapter(options));

    private string BaseUrl => _adapter.BaseUrl ?? throw new InvalidOperationException("Adapter has no BaseUrl.");

    // --- writes (return a task UPID) ---------------------------------------

    /// <summary>Start a container (POST …/lxc/{vmid}/status/start).</summary>
    public Task<string?> StartAsync(string node, int vmid, CancellationToken ct = default) =>
        SendFormAsync(Method.POST, "{+baseurl}/nodes/{node}/lxc/{vmid}/status/start", PathParams(node, vmid), form: null, ct);

    /// <summary>Hard stop — kill the container (POST …/lxc/{vmid}/status/stop).</summary>
    public Task<string?> StopAsync(string node, int vmid, CancellationToken ct = default) =>
        SendFormAsync(Method.POST, "{+baseurl}/nodes/{node}/lxc/{vmid}/status/stop", PathParams(node, vmid), form: null, ct);

    /// <summary>
    /// Graceful shutdown (POST …/lxc/{vmid}/status/shutdown). <paramref name="forceStop"/>
    /// falls back to a hard stop if the clean shutdown exceeds <paramref name="timeout"/> seconds.
    /// </summary>
    public Task<string?> ShutdownAsync(string node, int vmid, bool forceStop = true, int timeout = 60, CancellationToken ct = default) =>
        SendFormAsync(Method.POST, "{+baseurl}/nodes/{node}/lxc/{vmid}/status/shutdown" + ShutdownQuery(forceStop, timeout),
            PathParams(node, vmid), form: null, ct);

    /// <summary>
    /// Destroy a container (DELETE …/lxc/{vmid}). <paramref name="purge"/> also removes it
    /// from related configs (backup jobs, HA, replication) and destroys unreferenced disks;
    /// <paramref name="force"/> destroys even while it is still running.
    /// </summary>
    public Task<string?> DeleteAsync(string node, int vmid, bool force = false, bool purge = true, CancellationToken ct = default) =>
        SendFormAsync(Method.DELETE, "{+baseurl}/nodes/{node}/lxc/{vmid}" + DeleteQuery(force, purge),
            PathParams(node, vmid), form: null, ct);

    // --- query builders (pure — unit-tested) -------------------------------

    /// <summary>Query string for DELETE: force destroy and/or purge refs + unreferenced disks.</summary>
    public static string DeleteQuery(bool force, bool purge)
    {
        var parts = new List<string>();
        if (force) parts.Add("force=1");
        if (purge) { parts.Add("purge=1"); parts.Add("destroy-unreferenced-disks=1"); }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
    }

    /// <summary>Query string for graceful shutdown: hard-stop fallback + timeout (seconds).</summary>
    public static string ShutdownQuery(bool forceStop, int timeout) =>
        $"?forceStop={(forceStop ? 1 : 0)}&timeout={timeout.ToString(CultureInfo.InvariantCulture)}";

    // --- task polling (mirrors QemuWriter.WaitForTaskAsync) ----------------

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
            if (data is not null && !string.Equals(data.Status?.ToString(), "running", StringComparison.OrdinalIgnoreCase))
                return data.Exitstatus;
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"Task {upid} did not finish within the timeout.");
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    // --- internals (mirror QemuWriter) -------------------------------------

    private static readonly Dictionary<string, ParsableFactory<IParsable>> ErrorMapping = new()
    {
        ["4XX"] = static _ => new ProxmoxApiError(),
        ["5XX"] = static _ => new ProxmoxApiError(),
    };

    private Dictionary<string, object> PathParams(string node, int vmid) => new()
    {
        ["baseurl"] = BaseUrl,
        ["node"] = node,
        ["vmid"] = vmid.ToString(CultureInfo.InvariantCulture),
    };

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

        Stream? stream;
        try
        {
            stream = await _adapter.SendPrimitiveAsync<Stream>(ri, ErrorMapping, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (ProxmoxApiError err)
        {
            var detail = err.AdditionalData.Count > 0
                ? string.Join("; ", err.AdditionalData.Select(kv => $"{kv.Key}={kv.Value}"))
                : "(empty body)";
            throw new InvalidOperationException(
                $"Proxmox {method} {urlTemplate} failed (HTTP {err.ResponseStatusCode}): {detail}", err);
        }
        if (stream is null) return null;
        await using var _ = stream.ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String
            ? d.GetString()
            : null;
    }
}
