using ProxmoxSharp.Vm;

namespace ProxmoxSharp.Tests;

public class VmReconcilerTests
{
    [Fact]
    public void Absent_vm_plans_a_create_with_all_params()
    {
        var plan = VmReconciler.Reconcile(QemuParamEncoderTests.Bazzite(), live: null);

        Assert.Equal(VmActionKind.Create, plan.Kind);
        Assert.Equal(1003, plan.Vmid);
        Assert.Contains(plan.Changes, c => c.Key == "hostpci0" && c.From is null);
        Assert.Contains(plan.Changes, c => c.Key == "scsi0");
        Assert.All(plan.Changes, c => Assert.Null(c.From));  // everything is a fresh add
    }

    // The headline acceptance criterion (#115): reconciling the Bazzite spec against
    // the live, hand-built VM 1003 — identical except it lacks the GPU — must plan
    // EXACTLY ONE change: + hostpci0. Proxmox's extra size=120G on the disk must NOT
    // register as drift.
    [Fact]
    public void Adopting_1003_plans_only_the_added_gpu()
    {
        var live = Live1003WithoutGpu();

        var plan = VmReconciler.Reconcile(QemuParamEncoderTests.Bazzite(), live);

        Assert.Equal(VmActionKind.SetConfig, plan.Kind);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("hostpci0", change.Key);
        Assert.Null(change.From);
        Assert.Equal("0000:09:00,pcie=1,x-vga=1", change.To);
    }

    [Fact]
    public void Fully_satisfied_state_is_a_skip()
    {
        var live = Live1003WithoutGpu();
        live["hostpci0"] = "0000:09:00,pcie=1,x-vga=1";  // GPU already attached

        var plan = VmReconciler.Reconcile(QemuParamEncoderTests.Bazzite(), live);

        Assert.Equal(VmActionKind.Skip, plan.Kind);
        Assert.False(plan.HasChanges);
    }

    [Fact]
    public void Live_only_keys_are_reported_as_unmanaged_never_removed()
    {
        var live = Live1003WithoutGpu();
        live["smbios1"] = "uuid=6f4378f4-cc71-4bdc-9fef-ae234a95457f";  // Proxmox-managed, not in our spec

        var plan = VmReconciler.Reconcile(QemuParamEncoderTests.Bazzite(), live);

        Assert.Contains("smbios1", plan.UnmanagedKeys);
        Assert.DoesNotContain(plan.Changes, c => c.Key == "smbios1");
    }

    [Theory]
    // extra options Proxmox adds (size, reordering) don't count as drift
    [InlineData("local-lvm:vm-1003-disk-1,iothread=1,ssd=1", "local-lvm:vm-1003-disk-1,iothread=1,size=120G,ssd=1", true)]
    [InlineData("local-lvm:vm-1003-disk-1,iothread=1,ssd=1", "iothread=1,ssd=1,local-lvm:vm-1003-disk-1,size=120G", true)]
    // a genuinely different option IS drift
    [InlineData("local-lvm:vm-1003-disk-1,iothread=1,ssd=1", "local-lvm:vm-1003-disk-1,ssd=1", false)]
    // a different volume id IS drift
    [InlineData("local-lvm:vm-1003-disk-1", "local-lvm:vm-9999-disk-1", false)]
    public void Satisfied_uses_subset_semantics(string want, string have, bool expected)
    {
        Assert.Equal(expected, VmReconciler.Satisfied(want, have));
    }

    // Mirrors the real `qm config 1003` (minus hostpci0): the Bazzite encoder's keys,
    // but with Proxmox's own additions (disk size, reordered options, extra fields).
    private static Dictionary<string, string> Live1003WithoutGpu() => new(StringComparer.Ordinal)
    {
        ["machine"] = "q35",
        ["bios"] = "ovmf",
        ["cpu"] = "host",
        ["cores"] = "6",
        ["memory"] = "12288",
        ["ostype"] = "l26",
        ["agent"] = "1",
        ["scsihw"] = "virtio-scsi-single",
        ["scsi0"] = "local-lvm:vm-1003-disk-1,iothread=1,size=120G,ssd=1",   // + size=120G (Proxmox-added)
        ["efidisk0"] = "local-lvm:vm-1003-disk-0,efitype=4m,pre-enrolled-keys=1,size=4M",
        ["net0"] = "virtio=BC:24:11:03:32:13,bridge=vmbr0",                   // + a MAC we didn't pin
        ["name"] = "bazzite",
        ["digest"] = "abc123",                                               // Proxmox bookkeeping
    };
}
