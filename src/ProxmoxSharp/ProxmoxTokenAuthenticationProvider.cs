using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace ProxmoxSharp;

/// <summary>
/// Kiota authentication provider that adds Proxmox's
/// <c>Authorization: PVEAPIToken=user@realm!tokenid=secret</c> header to every request.
/// </summary>
public sealed class ProxmoxTokenAuthenticationProvider : IAuthenticationProvider
{
    private readonly string _headerValue;

    public ProxmoxTokenAuthenticationProvider(ProxmoxClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _headerValue = options.AuthorizationHeader;
    }

    public Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.TryAdd("Authorization", _headerValue);
        return Task.CompletedTask;
    }
}
