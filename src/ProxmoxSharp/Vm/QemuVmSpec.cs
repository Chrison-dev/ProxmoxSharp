namespace ProxmoxSharp.Vm;

/// <summary>
/// ProxmoxSharp-native desired state for a QEMU/KVM VM — the input to
/// <see cref="QemuParamEncoder"/> (→ Proxmox config params) and
/// <see cref="VmReconciler"/> (→ a diff against live state).
/// <para>
/// This is deliberately independent of the Homelab hub's <c>VmSpec</c> shape: the
/// hub maps its YAML shape onto this record at provision time (the SynoSharp #57
/// pattern — the client owns its own resource model). Only what the VM write path
/// actually sets lives here.
/// </para>
/// </summary>
public sealed record QemuVmSpec
{
    public required string Node { get; init; }
    public required int Vmid { get; init; }
    public string? Name { get; init; }

    public string? Machine { get; init; }
    public string? Bios { get; init; }            // seabios | ovmf
    public string? Cpu { get; init; }
    public int? Cores { get; init; }
    public int? Sockets { get; init; }
    public int? Memory { get; init; }             // MB
    public bool? Numa { get; init; }
    public string? Ostype { get; init; }
    public bool? Agent { get; init; }
    public bool? Onboot { get; init; }
    public bool? Protection { get; init; }
    public string? Scsihw { get; init; }
    public string? Vga { get; init; }

    public IReadOnlyList<QemuDisk> Disks { get; init; } = [];
    public QemuEfiDisk? Efidisk { get; init; }
    public QemuTpmState? Tpmstate { get; init; }
    public QemuCdrom? Cdrom { get; init; }
    public IReadOnlyList<QemuNet> Nets { get; init; } = [];
    public IReadOnlyList<QemuHostPci> HostPci { get; init; } = [];

    public IReadOnlyList<string> BootOrder { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>A QEMU disk (scsiN/virtioN/sataN). <see cref="Id"/> is the bus+slot, e.g. "scsi0".</summary>
public sealed record QemuDisk
{
    public required string Id { get; init; }
    public string? Storage { get; init; }
    public string? Source { get; init; }          // existing volume to adopt (e.g. "vm-1003-disk-1")
    public int? Size { get; init; }               // GB, for a freshly-allocated volume
    public bool? Ssd { get; init; }
    public bool? Iothread { get; init; }
    public bool? Discard { get; init; }
}

/// <summary>UEFI vars disk (efidisk0) — needed for bios: ovmf.</summary>
public sealed record QemuEfiDisk
{
    public string? Storage { get; init; }
    public string? Source { get; init; }
    public string? Efitype { get; init; }         // 2m | 4m
    public bool? PreEnrolledKeys { get; init; }
}

/// <summary>vTPM state disk (tpmstate0).</summary>
public sealed record QemuTpmState
{
    public string? Storage { get; init; }
    public string? Source { get; init; }
    public string? Version { get; init; }         // v1.2 | v2.0
}

/// <summary>Install/boot ISO attached to a CD-ROM (ide2 by default).</summary>
public sealed record QemuCdrom
{
    public string? Storage { get; init; }
    public string? Iso { get; init; }
    public string? Source { get; init; }          // explicit "storage:iso/<name>" override
    public string Bus { get; init; } = "ide2";
}

/// <summary>A virtual NIC (netN). <see cref="Id"/> e.g. "net0".</summary>
public sealed record QemuNet
{
    public required string Id { get; init; }
    public string Model { get; init; } = "virtio";
    public string? Bridge { get; init; }
    public string? Mac { get; init; }
    public int? Tag { get; init; }                // VLAN
    public bool? Firewall { get; init; }
}

/// <summary>
/// A PCI(e) passthrough device (hostpciN) — the gaming GPU. <see cref="Id"/> e.g. "hostpci0".
/// <para>
/// Provide EITHER <see cref="Mapping"/> (a Proxmox PCI resource-mapping name) or
/// <see cref="Host"/> (a raw PCI address). Prefer <see cref="Mapping"/>: Proxmox only lets
/// the real <c>root@pam</c> user set a raw <c>hostpciN</c> ("only root can set 'hostpciN'
/// config for non-mapped devices"), whereas a mapped device is settable by any token with
/// <c>Mapping.Use</c> — and it's node-portable.
/// </para>
/// </summary>
public sealed record QemuHostPci
{
    public required string Id { get; init; }
    public string? Mapping { get; init; }         // PCI resource-mapping name (token-settable; preferred)
    public string? Host { get; init; }            // raw PCI address, e.g. "0000:09:00" (root@pam only)
    public bool? Pcie { get; init; }
    public bool? XVga { get; init; }
    public bool? Rombar { get; init; }
    public string? Romfile { get; init; }
    public string? Mdev { get; init; }
}
