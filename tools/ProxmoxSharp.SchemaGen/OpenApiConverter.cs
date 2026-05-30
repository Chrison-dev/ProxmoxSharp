using System.Text;
using System.Text.Json;

namespace ProxmoxSharp.SchemaGen;

/// <summary>
/// Converts the parsed Proxmox apiSchema tree into an OpenAPI 3.0 document.
/// Handles the Proxmox-specific shape: per-property <c>optional</c> flags (inverted
/// into OpenAPI <c>required</c>), <c>{name}</c> path templates, array/object
/// returns, enums, and the <c>{ "data": … }</c> response envelope.
/// </summary>
internal sealed class OpenApiConverter
{
    private readonly IReadOnlyList<string> _include;
    private readonly IReadOnlySet<string> _methods;
    private readonly string _version;
    private readonly Dictionary<string, object?> _paths = new(StringComparer.Ordinal);

    public int PathCount { get; private set; }
    public int OperationCount { get; private set; }

    public OpenApiConverter(IReadOnlyList<string> include, IReadOnlySet<string> methods, string version)
    {
        _include = include;
        _methods = methods;
        _version = version;
    }

    public Dictionary<string, object?> Convert(JsonElement root)
    {
        foreach (var entry in root.EnumerateArray())
        {
            WalkNode(entry);
        }

        return new Dictionary<string, object?>
        {
            ["openapi"] = "3.0.3",
            ["info"] = new Dictionary<string, object?>
            {
                ["title"] = "Proxmox VE API",
                ["version"] = _version,
                ["description"] = "Generated from apidoc.js by ProxmoxSharp.SchemaGen.",
            },
            ["servers"] = new List<object?> { new Dictionary<string, object?> { ["url"] = "https://proxmox.example/api2/json" } },
            ["paths"] = _paths,
        };
    }

    private void WalkNode(JsonElement node)
    {
        if (node.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
        {
            var path = pathEl.GetString()!;
            if (ShouldInclude(path) && node.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
            {
                var pathItem = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var methodProp in info.EnumerateObject())
                {
                    if (!_methods.Contains(methodProp.Name))
                    {
                        continue;
                    }
                    pathItem[methodProp.Name.ToLowerInvariant()] = BuildOperation(path, methodProp.Name, methodProp.Value);
                    OperationCount++;
                }

                if (pathItem.Count > 0)
                {
                    _paths[path] = pathItem;
                    PathCount++;
                }
            }
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                WalkNode(child);
            }
        }
    }

    private bool ShouldInclude(string path)
        => _include.Count == 0 || _include.Any(p => path == p || path.StartsWith(p + "/", StringComparison.Ordinal) || path.StartsWith(p, StringComparison.Ordinal) && p == path);

    private object BuildOperation(string path, string method, JsonElement def)
    {
        var op = new Dictionary<string, object?>
        {
            ["operationId"] = OperationId(method, path),
            ["responses"] = BuildResponses(def),
        };

        if (TryGetString(def, "description", out var description))
        {
            op["description"] = description;
        }

        var parameters = ConvertParameters(path, def);
        if (parameters.Count > 0)
        {
            op["parameters"] = parameters;
        }

        return op;
    }

    private List<object?> ConvertParameters(string path, JsonElement def)
    {
        var result = new List<object?>();
        if (!def.TryGetProperty("parameters", out var pars) ||
            !pars.TryGetProperty("properties", out var props) ||
            props.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var pathTokens = ExtractPathTokens(path);
        foreach (var prop in props.EnumerateObject())
        {
            var inPath = pathTokens.Contains(prop.Name);
            var param = new Dictionary<string, object?>
            {
                ["name"] = prop.Name,
                ["in"] = inPath ? "path" : "query",
                ["required"] = inPath || !IsOptional(prop.Value),
                ["schema"] = ConvertSchema(prop.Value),
            };
            if (TryGetString(prop.Value, "description", out var d))
            {
                param["description"] = d;
            }
            result.Add(param);
        }

        return result;
    }

    private object BuildResponses(JsonElement def)
    {
        object dataSchema = def.TryGetProperty("returns", out var returns)
            ? ConvertSchema(returns)
            : new Dictionary<string, object?> { ["type"] = "object", ["additionalProperties"] = true };

        // Proxmox wraps every payload as { "data": <result> }.
        var envelope = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?> { ["data"] = dataSchema },
        };

        return new Dictionary<string, object?>
        {
            ["200"] = new Dictionary<string, object?>
            {
                ["description"] = TryGetString(def, "description", out var d) ? d : "OK",
                ["content"] = new Dictionary<string, object?>
                {
                    ["application/json"] = new Dictionary<string, object?> { ["schema"] = envelope },
                },
            },
        };
    }

    /// <summary>Convert a single Proxmox property/return schema into an OpenAPI schema.</summary>
    private object ConvertSchema(JsonElement def)
    {
        var schema = new Dictionary<string, object?>();
        if (TryGetString(def, "description", out var description))
        {
            schema["description"] = description;
        }

        var type = TryGetString(def, "type", out var t) ? t : null;
        type ??= def.TryGetProperty("properties", out _) ? "object"
            : def.TryGetProperty("items", out _) ? "array"
            : "string";

        switch (type)
        {
            case "array":
                schema["type"] = "array";
                schema["items"] = def.TryGetProperty("items", out var items)
                    ? ConvertSchema(items)
                    : new Dictionary<string, object?> { ["type"] = "object", ["additionalProperties"] = true };
                break;

            case "object":
                schema["type"] = "object";
                AddObjectProperties(def, schema);
                break;

            case "integer":
                // Proxmox "integer" is unbounded (e.g. memory in bytes overflows
                // Int32), so always widen to int64 → Kiota generates `long`.
                schema["type"] = "integer";
                schema["format"] = "int64";
                break;

            case "number":
            case "boolean":
                schema["type"] = type;
                break;

            default: // string and Proxmox custom scalar types
                schema["type"] = "string";
                CopyEnum(def, schema);
                break;
        }

        return schema;
    }

    private void AddObjectProperties(JsonElement def, Dictionary<string, object?> schema)
    {
        if (def.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            var required = new List<object?>();
            foreach (var prop in props.EnumerateObject())
            {
                properties[prop.Name] = ConvertSchema(prop.Value);
                if (!IsOptional(prop.Value))
                {
                    required.Add(prop.Name);
                }
            }

            if (properties.Count > 0)
            {
                schema["properties"] = properties;
            }
            if (required.Count > 0)
            {
                schema["required"] = required;
            }
        }
        else
        {
            // Free-form object (no declared properties).
            schema["additionalProperties"] = true;
        }
    }

    private static void CopyEnum(JsonElement def, Dictionary<string, object?> schema)
    {
        if (def.TryGetProperty("enum", out var en) && en.ValueKind == JsonValueKind.Array)
        {
            var values = new List<object?>();
            foreach (var v in en.EnumerateArray())
            {
                values.Add(v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString());
            }
            if (values.Count > 0)
            {
                schema["enum"] = values;
            }
        }
    }

    private static bool IsOptional(JsonElement prop)
        => prop.TryGetProperty("optional", out var o)
            && (o.ValueKind == JsonValueKind.Number && o.GetInt32() == 1
                || o.ValueKind == JsonValueKind.True);

    private static HashSet<string> ExtractPathTokens(string path)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while ((i = path.IndexOf('{', i)) >= 0)
        {
            var end = path.IndexOf('}', i);
            if (end < 0)
            {
                break;
            }
            tokens.Add(path[(i + 1)..end]);
            i = end + 1;
        }
        return tokens;
    }

    private static string OperationId(string method, string path)
    {
        var sb = new StringBuilder(method.ToLowerInvariant());
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg.StartsWith('{') && seg.EndsWith('}'))
            {
                sb.Append("By").Append(Pascal(seg[1..^1]));
            }
            else
            {
                sb.Append(Pascal(seg));
            }
        }
        return sb.ToString();
    }

    private static string Pascal(string s)
    {
        var sb = new StringBuilder(s.Length);
        var capitalizeNext = true;
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c))
            {
                capitalizeNext = true;
                continue;
            }
            sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
            capitalizeNext = false;
        }
        return sb.ToString();
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()!;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
