using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProxmoxSharp;

/// <summary>
/// Minimal hand-written Proxmox VE client for the M2 read path — token auth,
/// transport, and the <c>{ "data": … }</c> envelope. It proves auth/transport
/// against the live cluster before the Kiota-generated surface lands (M3); the
/// generated request builders will replace these hand-written calls.
/// </summary>
public sealed class ProxmoxClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <param name="options">Connection + auth options.</param>
    /// <param name="httpClient">
    /// Optional injected client (for tests). When null, one is created and owned
    /// by this instance, honouring <see cref="ProxmoxClientOptions.VerifyTls"/>.
    /// </param>
    public ProxmoxClient(ProxmoxClientOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (httpClient is null)
        {
            var handler = new HttpClientHandler();
            if (!options.VerifyTls)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            _http = new HttpClient(handler);
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttp = false;
        }

        // PVE paths are relative to "<base>/"; ensure a trailing slash so relative
        // request URIs resolve correctly.
        _http.BaseAddress = options.BaseUrl.AbsoluteUri.EndsWith('/')
            ? options.BaseUrl
            : new Uri(options.BaseUrl.AbsoluteUri + "/");

        // Non-standard scheme: "Authorization: PVEAPIToken=<id>=<secret>" — no space
        // after the scheme word, so it must be set raw (not via AuthenticationHeaderValue).
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", options.AuthorizationHeader);
    }

    /// <summary>GET a PVE endpoint and unwrap its <c>{ "data": … }</c> envelope.</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content
            .ReadFromJsonAsync<Envelope<T>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return envelope is null ? default : envelope.Data;
    }

    /// <summary>List cluster nodes (<c>GET /nodes</c>).</summary>
    public async Task<IReadOnlyList<PveNode>> GetNodesAsync(CancellationToken cancellationToken = default)
        => await GetAsync<List<PveNode>>("nodes", cancellationToken).ConfigureAwait(false) ?? [];

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private sealed record Envelope<T>([property: JsonPropertyName("data")] T? Data);
}
