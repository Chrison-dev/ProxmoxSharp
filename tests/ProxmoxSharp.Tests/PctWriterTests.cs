using ProxmoxSharp.Lxc;
using Xunit;

namespace ProxmoxSharp.Tests;

// The only branching logic in the LXC lifecycle write path (#149): the Proxmox query
// strings for destroy + graceful shutdown. The wire send + task polling mirror
// QemuWriter and are exercised via the CLI, like the VM path.
public class PctWriterTests
{
    [Fact]
    public void DeleteQuery_purge_only_is_the_default_shape() =>
        Assert.Equal("?purge=1&destroy-unreferenced-disks=1", PctWriter.DeleteQuery(force: false, purge: true));

    [Fact]
    public void DeleteQuery_force_and_purge_combine() =>
        Assert.Equal("?force=1&purge=1&destroy-unreferenced-disks=1", PctWriter.DeleteQuery(force: true, purge: true));

    [Fact]
    public void DeleteQuery_force_only() =>
        Assert.Equal("?force=1", PctWriter.DeleteQuery(force: true, purge: false));

    [Fact]
    public void DeleteQuery_neither_is_empty() =>
        Assert.Equal("", PctWriter.DeleteQuery(force: false, purge: false));

    [Fact]
    public void ShutdownQuery_encodes_forceStop_and_timeout()
    {
        Assert.Equal("?forceStop=1&timeout=60", PctWriter.ShutdownQuery(forceStop: true, timeout: 60));
        Assert.Equal("?forceStop=0&timeout=120", PctWriter.ShutdownQuery(forceStop: false, timeout: 120));
    }
}
