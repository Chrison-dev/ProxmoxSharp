using ProxmoxSharp;

namespace ProxmoxSharp.Tests;

/// <summary>
/// Loads dev secrets from a gitignored <c>secrets.env</c> at the ProxmoxSharp repo
/// root. Returns null when absent so integration tests skip cleanly. Format is
/// documented in <c>secrets.env.example</c>.
/// </summary>
internal static class SecretsEnv
{
    public static ProxmoxClientOptions? TryLoadOptions()
    {
        var path = FindSecretsFile();
        if (path is null)
        {
            return null;
        }

        var values = ParseEnv(path);
        if (!values.TryGetValue("PROXMOX_BASE_URL", out var baseUrl) ||
            !values.TryGetValue("PROXMOX_TOKEN_ID", out var tokenId) ||
            !values.TryGetValue("PROXMOX_TOKEN_SECRET", out var secret))
        {
            return null;
        }

        // Verify TLS unless explicitly set to "false" (PVE nodes use self-signed certs).
        var verifyTls = !values.TryGetValue("PROXMOX_VERIFY_TLS", out var v)
            || !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);

        return new ProxmoxClientOptions
        {
            BaseUrl = new Uri(baseUrl),
            TokenId = tokenId,
            TokenSecret = secret,
            VerifyTls = verifyTls,
        };
    }

    private static string? FindSecretsFile()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "secrets.env");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static Dictionary<string, string> ParseEnv(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            var value = line[(eq + 1)..].Trim();
            // Strip a single pair of matching surrounding quotes, if present.
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            result[line[..eq].Trim()] = value;
        }
        return result;
    }
}
