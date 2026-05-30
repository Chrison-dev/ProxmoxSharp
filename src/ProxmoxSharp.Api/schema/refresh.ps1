#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Refresh the pinned Proxmox API schema (apidoc.js) from a live node.

.DESCRIPTION
    Pulls /usr/share/pve-docs/api-viewer/apidoc.js off a Proxmox node over SSH,
    auto-detects the node's PVE version, and writes a version-stamped copy next
    to this script (e.g. apidoc.9.2.2.js). Keeping the schema version-matched to
    our own cluster is the whole point — the public copy at pve.proxmox.com
    tracks the latest release, not what we run.

.PARAMETER Node
    SSH host (alias or IP) of a Proxmox node. Default: hpe-01.

.EXAMPLE
    ./refresh.ps1 -Node hpe-01
#>
[CmdletBinding()]
param(
    [string]$Node = "hpe-01"
)

$ErrorActionPreference = "Stop"

$raw = ssh $Node "pveversion"
if ($LASTEXITCODE -ne 0) { throw "Could not read pveversion from $Node." }
if ($raw -notmatch 'pve-manager/([0-9]+\.[0-9]+\.[0-9]+)') {
    throw "Unexpected pveversion output: $raw"
}
$version = $Matches[1]

$dest = Join-Path $PSScriptRoot "apidoc.$version.js"
Write-Host "Pulling apidoc.js from $Node (PVE $version) -> $dest"
ssh $Node "cat /usr/share/pve-docs/api-viewer/apidoc.js" | Set-Content -Path $dest -NoNewline
Write-Host "Done. $((Get-Item $dest).Length) bytes."
