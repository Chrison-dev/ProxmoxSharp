using Microsoft.Kiota.Http.HttpClientLibrary;
using ProxmoxSharp.Generated;

namespace ProxmoxSharp;

/// <summary>
/// Builds a fully-wired, Kiota-generated <see cref="ProxmoxApiClient"/> — token
/// auth, the target base URL, and self-signed-cert handling. This is the intended
/// entry point for the generated read surface (e.g. <c>client.Version.GetAsync()</c>,
/// <c>client.Nodes.GetAsync()</c>).
/// </summary>
public static class ProxmoxApi
{
    public static ProxmoxApiClient Create(ProxmoxClientOptions options)
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
        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: new HttpClient(handler))
        {
            BaseUrl = options.BaseUrl.AbsoluteUri.TrimEnd('/'),
        };

        return new ProxmoxApiClient(adapter);
    }
}
