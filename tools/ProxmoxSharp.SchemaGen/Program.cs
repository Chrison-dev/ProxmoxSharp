using System.Text.Json;
using ProxmoxSharp.SchemaGen;

// ProxmoxSharp.SchemaGen — converts Proxmox's apidoc.js into an OpenAPI 3.0
// document that Kiota can turn into a C# client. See the BL-009 codegen plan.
//
// Usage:
//   dotnet run --project tools/ProxmoxSharp.SchemaGen -- \
//       --in schema/apidoc.9.2.2.js --out schema/openapi.json \
//       [--include /version,/nodes] [--methods GET]
//
// --include limits emitted paths by prefix (default: all). --methods limits HTTP
// methods (default: GET — the read path). Both let us grow coverage incrementally.

var options = CliOptions.Parse(args);
Console.WriteLine($"Reading  {options.InputPath}");

var schemaJs = File.ReadAllText(options.InputPath);
using var doc = ApiSchemaLoader.Load(schemaJs);

var converter = new OpenApiConverter(options.IncludePrefixes, options.Methods, options.Version);
var openApi = converter.Convert(doc.RootElement);

var json = JsonSerializer.Serialize(openApi, new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
});
File.WriteAllText(options.OutputPath, json);

Console.WriteLine($"Wrote    {options.OutputPath}");
Console.WriteLine($"Paths    {converter.PathCount}  Operations {converter.OperationCount}  (methods: {string.Join(",", options.Methods)})");
if (options.IncludePrefixes.Count > 0)
{
    Console.WriteLine($"Included {string.Join(", ", options.IncludePrefixes)}");
}
