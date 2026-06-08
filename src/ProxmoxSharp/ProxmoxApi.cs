using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary;
using ProxmoxSharp.Api;

namespace ProxmoxSharp;

/// <summary>
/// Builds a fully-wired, Kiota-generated <see cref="ProxmoxApiClient"/> — token
/// auth, the target base URL, and self-signed-cert handling. This is the intended
/// entry point for the generated read surface (e.g. <c>client.Version.GetAsync()</c>,
/// <c>client.Nodes.GetAsync()</c>).
/// </summary>
public static class ProxmoxApi
{
    public static ProxmoxApiClient Create(ProxmoxClientOptions options) =>
        new(CreateAdapter(options));

    /// <summary>
    /// Builds the configured Kiota <see cref="IRequestAdapter"/> on its own — token
    /// auth, base URL, self-signed-cert handling. The write path (<see cref="QemuWriter"/>)
    /// shares this so it inherits the exact same auth/TLS wiring as the read client,
    /// while still being able to hand-build requests for Proxmox's indexed params
    /// (hostpci0/scsi0/…) that the generated typed query surface can't express.
    /// </summary>
    public static IRequestAdapter CreateAdapter(ProxmoxClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var authProvider = new ProxmoxTokenAuthenticationProvider(options);

        var handler = new HttpClientHandler();
        if (!options.VerifyTls)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        // The generated client registers the default (JSON) serializers itself.
        return new HttpClientRequestAdapter(authProvider, httpClient: new HttpClient(handler))
        {
            BaseUrl = options.BaseUrl.AbsoluteUri.TrimEnd('/'),
        };
    }
}
