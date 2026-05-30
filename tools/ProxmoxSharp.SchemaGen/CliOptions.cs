namespace ProxmoxSharp.SchemaGen;

/// <summary>Parsed command-line options for the converter.</summary>
internal sealed class CliOptions
{
    public string InputPath { get; private init; } = "schema/apidoc.9.2.2.js";
    public string OutputPath { get; private init; } = "schema/openapi.json";
    public string Version { get; private init; } = "9.2.2";

    /// <summary>Path prefixes to include (empty = all).</summary>
    public IReadOnlyList<string> IncludePrefixes { get; private init; } = [];

    /// <summary>HTTP methods to emit (uppercase). Default: GET (read path).</summary>
    public IReadOnlySet<string> Methods { get; private init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GET" };

    public static CliOptions Parse(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            map[args[i].TrimStart('-')] = args[i + 1];
        }

        var include = map.TryGetValue("include", out var inc) && inc.Length > 0
            ? inc.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        var methods = map.TryGetValue("methods", out var m) && m.Length > 0
            ? m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["GET"];

        return new CliOptions
        {
            InputPath = map.GetValueOrDefault("in", "schema/apidoc.9.2.2.js"),
            OutputPath = map.GetValueOrDefault("out", "schema/openapi.json"),
            Version = map.GetValueOrDefault("version", "9.2.2"),
            IncludePrefixes = include,
            Methods = new HashSet<string>(methods, StringComparer.OrdinalIgnoreCase),
        };
    }
}
