#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Create a dedicated read-only Proxmox API token for ProxmoxSharp dev.

.DESCRIPTION
    SSHes to a Proxmox node and creates a privilege-separated API token, then
    grants it a read-only role (PVEAuditor) at the root path so it can read the
    whole cluster but mutate nothing. Prints the one-time secret and the exact
    secrets.env lines to paste.

    This is the "correct token" helper — prefer a dedicated read-only token over
    reusing a broader token (e.g. the MCP's) for ProxmoxSharp dev/tests.

.PARAMETER Node
    SSH host (alias or IP) of a Proxmox node. Default: hpe-01.

.PARAMETER User
    PVE user that owns the token. Default: root@pam.

.PARAMETER TokenName
    Token id (the part after '!'). Default: proxmoxsharp-dev.

.PARAMETER Role
    Role granted at '/'. Default: PVEAuditor (read-only).

.EXAMPLE
    pwsh scripts/new-dev-token.ps1 -Node hpe-01
#>
[CmdletBinding()]
param(
    [string]$Node = "hpe-01",
    [string]$User = "root@pam",
    [string]$TokenName = "proxmoxsharp-dev",
    [string]$Role = "PVEAuditor"
)

$ErrorActionPreference = "Stop"
$tokenId = "$User!$TokenName"

Write-Host "Creating read-only token '$tokenId' on $Node (role $Role at /)..." -ForegroundColor Cyan

# Privilege-separated token: it has NO permissions until we grant them explicitly,
# so the ACL below is what scopes it to read-only.
$createJson = ssh $Node "pveum user token add $User $TokenName --privsep 1 --output-format json"
if ($LASTEXITCODE -ne 0) { throw "Token creation failed (does '$tokenId' already exist? remove with: pveum user token remove $User $TokenName)." }

ssh $Node "pveum acl modify / --tokens '$tokenId' --roles $Role"
if ($LASTEXITCODE -ne 0) { throw "Granting $Role to '$tokenId' failed." }

$secret = ($createJson | ConvertFrom-Json).value
if (-not $secret) { throw "Token created but could not parse the secret from pveum output." }

Write-Host "Token created and scoped read-only." -ForegroundColor Green
Write-Host ""
Write-Host "Paste these into secrets.env (gitignored):" -ForegroundColor Yellow
Write-Host "PROXMOX_TOKEN_ID=`"$tokenId`""
Write-Host "PROXMOX_TOKEN_SECRET=`"$secret`""
Write-Host ""
Write-Host "(The secret is shown only once — if you lose it, remove and recreate the token.)"
