using ProxmoxSharp.Vm;

namespace ProxmoxSharp.Tests;

// The error-prone heart of the VM write path: turning a typed spec into Proxmox's
// comma-option config strings. Modelled on the real desktop-01 VMs 1002/1003.
public class QemuParamEncoderTests
{
    [Fact]
    public void HostPci_encodes_the_passthrough_line_like_the_working_win_vm()
    {
        // VM 1002's working GPU line: hostpci0: 0000:09:00,pcie=1,x-vga=1
        var v = QemuParamEncoder.EncodeHostPci(new QemuHostPci
        {
            Id = "hostpci0", Host = "0000:09:00", Pcie = true, XVga = true,
        });
        Assert.Equal("0000:09:00,pcie=1,x-vga=1", v);
    }

    [Fact]
    public void HostPci_omits_rombar_unless_explicitly_disabled()
    {
        Assert.Equal("0000:09:00", QemuParamEncoder.EncodeHostPci(new QemuHostPci { Id = "hostpci0", Host = "0000:09:00" }));
        Assert.Equal("0000:09:00,rombar=0", QemuParamEncoder.EncodeHostPci(new QemuHostPci { Id = "hostpci0", Host = "0000:09:00", Rombar = false }));
        // rombar defaults on → not emitted even when explicitly true
        Assert.Equal("0000:09:00", QemuParamEncoder.EncodeHostPci(new QemuHostPci { Id = "hostpci0", Host = "0000:09:00", Rombar = true }));
    }

    [Fact]
    public void Disk_adopts_an_existing_volume_with_options()
    {
        var v = QemuParamEncoder.EncodeDisk(new QemuDisk
        {
            Id = "scsi0", Storage = "local-lvm", Source = "vm-1003-disk-1", Iothread = true, Ssd = true,
        });
        Assert.Equal("local-lvm:vm-1003-disk-1,iothread=1,ssd=1", v);
    }

    [Fact]
    public void Disk_allocates_a_fresh_volume_by_size_when_no_source()
    {
        var v = QemuParamEncoder.EncodeDisk(new QemuDisk { Id = "scsi0", Storage = "local-lvm", Size = 120, Ssd = true });
        Assert.Equal("local-lvm:120,ssd=1", v);
    }

    [Fact]
    public void Disk_without_storage_or_source_throws()
    {
        Assert.Throws<ArgumentException>(() => QemuParamEncoder.EncodeDisk(new QemuDisk { Id = "scsi0" }));
    }

    [Fact]
    public void EfiDisk_encodes_adopted_volume_with_secure_boot_keys()
    {
        var v = QemuParamEncoder.EncodeEfiDisk(new QemuEfiDisk
        {
            Storage = "local-lvm", Source = "vm-1003-disk-0", Efitype = "4m", PreEnrolledKeys = true,
        });
        Assert.Equal("local-lvm:vm-1003-disk-0,efitype=4m,pre-enrolled-keys=1", v);
    }

    [Fact]
    public void Net_encodes_model_mac_bridge_and_firewall()
    {
        var v = QemuParamEncoder.EncodeNet(new QemuNet
        {
            Id = "net0", Model = "virtio", Mac = "BC:24:11:03:32:13", Bridge = "vmbr0", Firewall = true,
        });
        Assert.Equal("virtio=BC:24:11:03:32:13,bridge=vmbr0,firewall=1", v);
    }

    [Fact]
    public void Net_without_mac_lets_proxmox_assign_one()
    {
        Assert.Equal("virtio,bridge=vmbr0", QemuParamEncoder.EncodeNet(new QemuNet { Id = "net0", Bridge = "vmbr0" }));
    }

    [Fact]
    public void Cdrom_encodes_iso_as_cdrom_media()
    {
        var v = QemuParamEncoder.EncodeCdrom(new QemuCdrom { Storage = "local", Iso = "bazzite-deck-stable-live.iso" });
        Assert.Equal("local:iso/bazzite-deck-stable-live.iso,media=cdrom", v);
    }

    [Fact]
    public void Boot_encodes_ordered_device_list()
    {
        Assert.Equal("order=scsi0;ide2;net0", QemuParamEncoder.EncodeBoot(["scsi0", "ide2", "net0"]));
    }

    [Fact]
    public void Encode_full_bazzite_spec_produces_expected_keys_and_values()
    {
        var p = QemuParamEncoder.Encode(Bazzite());

        Assert.Equal("q35", p["machine"]);
        Assert.Equal("ovmf", p["bios"]);
        Assert.Equal("host", p["cpu"]);
        Assert.Equal("6", p["cores"]);
        Assert.Equal("12288", p["memory"]);
        Assert.Equal("l26", p["ostype"]);
        Assert.Equal("1", p["agent"]);
        Assert.Equal("virtio-scsi-single", p["scsihw"]);
        Assert.Equal("local-lvm:vm-1003-disk-1,iothread=1,ssd=1", p["scsi0"]);
        Assert.Equal("local-lvm:vm-1003-disk-0,efitype=4m,pre-enrolled-keys=1", p["efidisk0"]);
        Assert.Equal("virtio=BC:24:11:03:32:13,bridge=vmbr0", p["net0"]);
        Assert.Equal("0000:09:00,pcie=1,x-vga=1", p["hostpci0"]);
        // identity/placement are NOT config params
        Assert.False(p.ContainsKey("vmid"));
        Assert.False(p.ContainsKey("node"));
    }

    // The IaC shape for stacks/Gaming/bazzite.vm.yaml, as a native spec.
    internal static QemuVmSpec Bazzite() => new()
    {
        Node = "desktop-01",
        Vmid = 1003,
        Name = "bazzite",
        Machine = "q35",
        Bios = "ovmf",
        Cpu = "host",
        Cores = 6,
        Memory = 12288,
        Ostype = "l26",
        Agent = true,
        Scsihw = "virtio-scsi-single",
        Disks = [new QemuDisk { Id = "scsi0", Storage = "local-lvm", Source = "vm-1003-disk-1", Iothread = true, Ssd = true }],
        Efidisk = new QemuEfiDisk { Storage = "local-lvm", Source = "vm-1003-disk-0", Efitype = "4m", PreEnrolledKeys = true },
        // Adoption pins the existing MAC so the reconciler won't try to regenerate it.
        Nets = [new QemuNet { Id = "net0", Mac = "BC:24:11:03:32:13", Bridge = "vmbr0" }],
        HostPci = [new QemuHostPci { Id = "hostpci0", Host = "0000:09:00", Pcie = true, XVga = true }],
    };
}
