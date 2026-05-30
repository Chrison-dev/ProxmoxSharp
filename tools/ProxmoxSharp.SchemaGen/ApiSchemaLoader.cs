using System.Text.Json;

namespace ProxmoxSharp.SchemaGen;

/// <summary>
/// Loads Proxmox's <c>apidoc.js</c>, which is a JS file shaped like
/// <c>const apiSchema = [ … ];</c> followed by the api-viewer's rendering code.
/// We extract just the leading JSON array (bracket-depth scan that respects
/// string literals) and parse it.
/// </summary>
internal static class ApiSchemaLoader
{
    public static JsonDocument Load(string js)
    {
        var array = ExtractArray(js);
        return JsonDocument.Parse(array);
    }

    private static string ExtractArray(string js)
    {
        var anchor = js.IndexOf("apiSchema", StringComparison.Ordinal);
        if (anchor < 0)
        {
            throw new InvalidOperationException("Could not find 'apiSchema' in the input.");
        }

        var start = js.IndexOf('[', anchor);
        if (start < 0)
        {
            throw new InvalidOperationException("Could not find the start of the apiSchema array.");
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < js.Length; i++)
        {
            var c = js[i];

            if (inString)
            {
                if (escaped) { escaped = false; }
                else if (c == '\\') { escaped = true; }
                else if (c == '"') { inString = false; }
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '[': depth++; break;
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        return js[start..(i + 1)];
                    }
                    break;
            }
        }

        throw new InvalidOperationException("Unterminated apiSchema array.");
    }
}
