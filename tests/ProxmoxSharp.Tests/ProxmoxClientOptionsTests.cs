using ProxmoxSharp;

namespace ProxmoxSharp.Tests;

public class ProxmoxClientOptionsTests
{
    [Fact]
    public void AuthorizationHeader_uses_the_PVEAPIToken_format()
    {
        var options = new ProxmoxClientOptions
        {
            BaseUrl = new Uri("https://hpe-01:8006/api2/json"),
            TokenId = "root@pam!homelab",
            TokenSecret = "00000000-0000-0000-0000-000000000000",
        };

        Assert.Equal(
            "PVEAPIToken=root@pam!homelab=00000000-0000-0000-0000-000000000000",
            options.AuthorizationHeader);
    }

    [Fact]
    public void VerifyTls_defaults_to_true()
    {
        var options = new ProxmoxClientOptions
        {
            BaseUrl = new Uri("https://hpe-01:8006/api2/json"),
            TokenId = "root@pam!homelab",
            TokenSecret = "secret",
        };

        Assert.True(options.VerifyTls);
    }
}
