namespace ProxmoxSharp;

/// <summary>
/// Connection options for a Proxmox VE cluster.
/// <para>
/// The read-only path authenticates with an API token, sent as the
/// <c>Authorization: PVEAPIToken=user@realm!tokenid=secret</c> header. This is the
/// first runtime seed; the client + Kiota-generated request builders land in M2/M3
/// (see docs/plans/BL-009-proxmoxsharp-codegen.md in the Homelab hub).
/// </para>
/// </summary>
public sealed record ProxmoxClientOptions
{
    /// <summary>Base URL of the PVE API, e.g. <c>https://hpe-01:8006/api2/json</c>.</summary>
    public required Uri BaseUrl { get; init; }

    /// <summary>Token id in the form <c>user@realm!tokenid</c> (e.g. <c>root@pam!homelab</c>).</summary>
    public required string TokenId { get; init; }

    /// <summary>Token secret (the UUID issued when the token was created).</summary>
    public required string TokenSecret { get; init; }

    /// <summary>
    /// Verify the node's TLS certificate. Homelab nodes commonly use self-signed
    /// certs, so this can be turned off — but it defaults to on.
    /// </summary>
    public bool VerifyTls { get; init; } = true;

    /// <summary>The value for the <c>Authorization</c> header Proxmox expects.</summary>
    public string AuthorizationHeader => $"PVEAPIToken={TokenId}={TokenSecret}";
}
