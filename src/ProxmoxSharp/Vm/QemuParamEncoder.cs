using System.Globalization;

namespace ProxmoxSharp.Vm;

/// <summary>
/// Pure translation of a <see cref="QemuVmSpec"/> into the flat
/// <c>key → value</c> Proxmox config params the API expects
/// (e.g. <c>hostpci0 → "0000:09:00,pcie=1,x-vga=1"</c>,
/// <c>scsi0 → "local-lvm:vm-1003-disk-1,iothread=1,ssd=1"</c>).
/// <para>
/// This is the one place that knows Proxmox's comma-option "cryptic argv" — the
/// analog of SynoSharp's positional-argv encoding. It is deliberately pure (no I/O)
/// so the fiddly serialization is exhaustively unit-testable, and so
/// <see cref="VmReconciler"/> can diff the result against a live config dict.
/// </para>
/// <para>
/// Identity (vmid) and placement (node) are NOT config params and are not emitted
/// here — the writer supplies vmid on create; node is a path segment.
/// </para>
/// </summary>
public static class QemuParamEncoder
{
    private static string Bool(bool b) => b ? "1" : "0";
    private static string Int(int i) => i.ToString(CultureInfo.InvariantCulture);

    /// <summary>Full config param set for a create (or the desired-state baseline for a diff).</summary>
    public static IReadOnlyDictionary<string, string> Encode(QemuVmSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var p = new Dictionary<string, string>(StringComparer.Ordinal);

        void Set(string k, string? v) { if (!string.IsNullOrEmpty(v)) p[k] = v!; }

        Set("name", spec.Name);
        Set("machine", spec.Machine);
        Set("bios", spec.Bios);
        Set("cpu", spec.Cpu);
        if (spec.Cores is { } c) Set("cores", Int(c));
        if (spec.Sockets is { } s) Set("sockets", Int(s));
        if (spec.Memory is { } m) Set("memory", Int(m));
        if (spec.Numa is { } numa) Set("numa", Bool(numa));
        Set("ostype", spec.Ostype);
        if (spec.Agent is { } ag) Set("agent", Bool(ag));
        if (spec.Onboot is { } ob) Set("onboot", Bool(ob));
        if (spec.Protection is { } pr) Set("protection", Bool(pr));
        Set("scsihw", spec.Scsihw);
        Set("vga", spec.Vga);

        foreach (var d in spec.Disks) Set(d.Id, EncodeDisk(d));
        if (spec.Efidisk is { } efi) Set("efidisk0", EncodeEfiDisk(efi));
        if (spec.Tpmstate is { } tpm) Set("tpmstate0", EncodeTpm(tpm));
        if (spec.Cdrom is { } cd) Set(cd.Bus, EncodeCdrom(cd));
        foreach (var n in spec.Nets) Set(n.Id, EncodeNet(n));
        foreach (var h in spec.HostPci) Set(h.Id, EncodeHostPci(h));

        if (spec.BootOrder.Count > 0) Set("boot", EncodeBoot(spec.BootOrder));
        if (spec.Tags.Count > 0) Set("tags", string.Join(";", spec.Tags));

        return p;
    }

    /// <summary>scsi0/virtio0 value: "&lt;storage&gt;:&lt;volid|size&gt;[,opt=…]".</summary>
    public static string EncodeDisk(QemuDisk d)
    {
        ArgumentNullException.ThrowIfNull(d);
        // Adopt an existing volume (storage:volid) or allocate a fresh one (storage:GB).
        var volume = (d.Storage, d.Source, d.Size) switch
        {
            ({ } st, { } src, _) when !string.IsNullOrEmpty(src) => $"{st}:{src}",
            ({ } st, _, { } sz) => $"{st}:{Int(sz)}",
            (_, { } src, _) when !string.IsNullOrEmpty(src) => src!,
            _ => throw new ArgumentException($"Disk '{d.Id}' needs a storage+source or storage+size."),
        };
        var opts = new List<string> { volume };
        if (d.Discard is true) opts.Add("discard=on");
        if (d.Iothread is true) opts.Add("iothread=1");
        if (d.Ssd is true) opts.Add("ssd=1");
        return string.Join(",", opts);
    }

    /// <summary>efidisk0 value.</summary>
    public static string EncodeEfiDisk(QemuEfiDisk e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var volume = !string.IsNullOrEmpty(e.Source) ? $"{e.Storage}:{e.Source}"
            : !string.IsNullOrEmpty(e.Storage) ? $"{e.Storage}:1"   // fresh EFI vars volume
            : throw new ArgumentException("efidisk needs storage (+ optional source).");
        var opts = new List<string> { volume };
        if (!string.IsNullOrEmpty(e.Efitype)) opts.Add($"efitype={e.Efitype}");
        if (e.PreEnrolledKeys is true) opts.Add("pre-enrolled-keys=1");
        return string.Join(",", opts);
    }

    /// <summary>tpmstate0 value.</summary>
    public static string EncodeTpm(QemuTpmState t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var volume = !string.IsNullOrEmpty(t.Source) ? $"{t.Storage}:{t.Source}"
            : !string.IsNullOrEmpty(t.Storage) ? $"{t.Storage}:1"
            : throw new ArgumentException("tpmstate needs storage (+ optional source).");
        var opts = new List<string> { volume };
        if (!string.IsNullOrEmpty(t.Version)) opts.Add($"version={t.Version}");
        return string.Join(",", opts);
    }

    /// <summary>ide2 (cdrom) value: "&lt;volid&gt;,media=cdrom".</summary>
    public static string EncodeCdrom(QemuCdrom c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var volid = !string.IsNullOrEmpty(c.Source) ? c.Source!
            : !string.IsNullOrEmpty(c.Storage) && !string.IsNullOrEmpty(c.Iso) ? $"{c.Storage}:iso/{c.Iso}"
            : throw new ArgumentException("cdrom needs source, or storage+iso.");
        return $"{volid},media=cdrom";
    }

    /// <summary>net0 value: "&lt;model&gt;[=&lt;mac&gt;],bridge=…[,tag=…][,firewall=1]".</summary>
    public static string EncodeNet(QemuNet n)
    {
        ArgumentNullException.ThrowIfNull(n);
        var head = string.IsNullOrEmpty(n.Mac) ? n.Model : $"{n.Model}={n.Mac}";
        var opts = new List<string> { head };
        if (!string.IsNullOrEmpty(n.Bridge)) opts.Add($"bridge={n.Bridge}");
        if (n.Tag is { } tag) opts.Add($"tag={Int(tag)}");
        if (n.Firewall is { } fw) opts.Add($"firewall={Bool(fw)}");
        return string.Join(",", opts);
    }

    /// <summary>hostpci0 value: "&lt;host&gt;[,pcie=1][,x-vga=1][,rombar=0][,romfile=…][,mdev=…]".</summary>
    public static string EncodeHostPci(QemuHostPci h)
    {
        ArgumentNullException.ThrowIfNull(h);
        if (string.IsNullOrEmpty(h.Host)) throw new ArgumentException($"hostpci '{h.Id}' needs a Host PCI address.");
        var opts = new List<string> { h.Host };
        if (h.Pcie is true) opts.Add("pcie=1");
        if (h.XVga is true) opts.Add("x-vga=1");
        if (h.Rombar is false) opts.Add("rombar=0");   // only emit when explicitly disabled (default is on)
        if (!string.IsNullOrEmpty(h.Romfile)) opts.Add($"romfile={h.Romfile}");
        if (!string.IsNullOrEmpty(h.Mdev)) opts.Add($"mdev={h.Mdev}");
        return string.Join(",", opts);
    }

    /// <summary>boot value: "order=scsi0;ide2;net0".</summary>
    public static string EncodeBoot(IReadOnlyList<string> order) => $"order={string.Join(";", order)}";
}
