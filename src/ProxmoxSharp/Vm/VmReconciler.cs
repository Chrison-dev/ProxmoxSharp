namespace ProxmoxSharp.Vm;

/// <summary>
/// Pure diff of a desired <see cref="QemuVmSpec"/> against a live Proxmox config
/// dict (as returned by <c>GET …/qemu/{vmid}/config</c>) → a <see cref="VmPlan"/>.
/// <para>
/// <b>Adoption-safe subset semantics.</b> A config key is considered satisfied
/// when every comma-option token the desired spec declares is already present in
/// the live value — extra options Proxmox adds on its own (e.g. <c>size=120G</c> on
/// a disk, or reordered tokens) are ignored. This is what lets reconciling the
/// hand-built VM 1003 against the Bazzite spec produce exactly one action,
/// <c>+ hostpci0</c>, instead of churning every Proxmox-normalised field.
/// </para>
/// <para>
/// Keys present live but absent from the desired spec are reported as
/// <see cref="VmPlan.UnmanagedKeys"/> and never removed — we only manage what we declare.
/// </para>
/// </summary>
public static class VmReconciler
{
    public static VmPlan Reconcile(QemuVmSpec desired, IReadOnlyDictionary<string, string>? live)
    {
        ArgumentNullException.ThrowIfNull(desired);
        var want = QemuParamEncoder.Encode(desired);

        if (live is null)
        {
            // Create: every desired param is a fresh addition.
            var creates = want
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new PlannedChange(kv.Key, null, kv.Value))
                .ToList();
            return new VmPlan(desired.Node, desired.Vmid, VmActionKind.Create, creates, []);
        }

        var changes = new List<PlannedChange>();
        foreach (var (key, wantVal) in want.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!live.TryGetValue(key, out var haveVal))
            {
                changes.Add(new PlannedChange(key, null, wantVal));
            }
            else if (!Satisfied(wantVal, haveVal))
            {
                changes.Add(new PlannedChange(key, haveVal, wantVal));
            }
        }

        var unmanaged = live.Keys
            .Where(k => !want.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        var kind = changes.Count > 0 ? VmActionKind.SetConfig : VmActionKind.Skip;
        return new VmPlan(desired.Node, desired.Vmid, kind, changes, unmanaged);
    }

    /// <summary>
    /// True when every token the desired value declares is matched by the live value.
    /// Tokens are the comma-separated parts of a Proxmox option string (the leading
    /// positional volume/address plus each <c>k=v</c> option). Extra live tokens are
    /// allowed (Proxmox-added), which is what keeps adoption non-destructive.
    /// <para>
    /// Two tokens match beyond plain equality, so a freshly-<i>created</i> VM also
    /// re-plans clean (not just adopted ones):
    /// <list type="bullet">
    ///   <item>a bare key satisfies its valued form — e.g. a NIC model <c>virtio</c>
    ///   is satisfied by the live <c>virtio=&lt;mac&gt;</c> Proxmox auto-assigned;</item>
    ///   <item>a size-allocation volume <c>storage:N</c> is satisfied by the realized
    ///   <c>storage:&lt;volid&gt;</c> + a <c>size=NG</c> token (the volume Proxmox cut).</item>
    /// </list>
    /// </para>
    /// </summary>
    public static bool Satisfied(string want, string have)
    {
        var haveTokens = Tokenize(have);
        return Tokenize(want).All(t => TokenMatched(t, haveTokens));
    }

    private static bool TokenMatched(string want, HashSet<string> have)
    {
        if (have.Contains(want)) return true;

        // (a) bare key ↔ valued form: NIC model `virtio` matches live `virtio=<mac>`.
        if (!want.Contains('=') && have.Any(h => h.StartsWith(want + "=", StringComparison.Ordinal)))
            return true;

        // (b) size-allocation `storage:N` matches a realized `storage:<volid>` + `size=NG`.
        if (TrySplitSizeAlloc(want, out var storage, out var sizeGb)
            && have.Contains($"size={sizeGb}G")
            && have.Any(h => h.StartsWith(storage + ":", StringComparison.Ordinal) && !h.Contains('=')))
            return true;

        return false;
    }

    // Recognises a fresh-allocation volume token "storage:N" (exactly one ':' and an
    // all-digit size) — distinct from an adopted volid "storage:vm-123-disk-0" or a
    // PCI address "0000:09:00" (two colons), neither of which parse here.
    private static bool TrySplitSizeAlloc(string token, out string storage, out string sizeGb)
    {
        storage = ""; sizeGb = "";
        var i = token.IndexOf(':');
        if (i <= 0 || token.IndexOf(':', i + 1) >= 0) return false;   // need exactly one ':'
        var s = token[..i];
        var n = token[(i + 1)..];
        if (n.Length == 0 || !n.All(char.IsDigit)) return false;
        storage = s; sizeGb = n;
        return true;
    }

    private static HashSet<string> Tokenize(string value) =>
        new(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.Ordinal);
}
