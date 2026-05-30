#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Regenerate the ProxmoxSharp client: apidoc.js -> OpenAPI -> Kiota C#.

.DESCRIPTION
    Runs the SchemaGen converter to produce schema/openapi.json, then Kiota to
    (re)generate src/ProxmoxSharp/Generated. Both outputs are committed, so this
    is only run when the schema or converter changes; the diff is then reviewable.

.PARAMETER Include
    Comma-separated path prefixes to include. Default: /version,/nodes (the read
    slice landed in M3). Widen as coverage grows.

.PARAMETER Methods
    Comma-separated HTTP methods. Default: GET (read path).

.EXAMPLE
    pwsh scripts/generate.ps1
    pwsh scripts/generate.ps1 -Include /version,/nodes,/cluster,/storage
#>
[CmdletBinding()]
param(
    [string]$Schema = "schema/apidoc.9.2.2.js",
    [string]$OpenApi = "schema/openapi.json",
    [string]$Include = "/version,/nodes",
    [string]$Methods = "GET"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try
{
    Write-Host "1/2  apidoc.js -> OpenAPI" -ForegroundColor Cyan
    dotnet run --project tools/ProxmoxSharp.SchemaGen -- --in $Schema --out $OpenApi --include $Include --methods $Methods

    Write-Host "2/2  OpenAPI -> Kiota C#" -ForegroundColor Cyan
    dotnet tool restore | Out-Null
    dotnet kiota generate -l CSharp -d $OpenApi -c ProxmoxApiClient -n ProxmoxSharp.Generated -o src/ProxmoxSharp/Generated --clean-output

    Write-Host "Done. Build with: dotnet build" -ForegroundColor Green
}
finally
{
    Pop-Location
}
