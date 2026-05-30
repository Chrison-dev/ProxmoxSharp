# ProxmoxSharp

A C# API client for Proxmox VE — **mostly code-generated from Proxmox's own
published API schema**, with a thin hand-written runtime for auth and transport.

Built to be dogfooded by the [Homelab](https://github.com/ChrisonSimtian/Homelab)
hub's C#-native IaC (the Discover → Converge path). Design + roadmap live in the
hub at `docs/plans/BL-009-proxmoxsharp-codegen.md`.

## Approach

```
apidoc.js (version-matched, pulled from our node)
   │  ProxmoxSharp.SchemaGen   → OpenAPI 3.0
   ▼
Kiota (pinned dotnet tool)     → generated C# request builders + models  (ProxmoxSharp.Api)
   ▼
ProxmoxSharp                   = hand-written runtime over it (PVEAPIToken auth, {data:…} envelope)
```

The generated client is **regenerated on build** (incrementally — only when the
schema changes) and **not committed**. Day-to-day work on the hand-written
library doesn't regenerate.

- **Read-only first** — token auth, the read path (nodes / LXC / VM / storage /
  network) before any write/lifecycle.
- **Schema is version-matched** to our cluster (PVE 9.2.2) and pinned under
  `src/ProxmoxSharp.Api/schema/`.

## Projects & versioning

| Project | What | Version |
| --- | --- | --- |
| `src/ProxmoxSharp.Api/` | The Kiota-generated client. `Generated/` is produced on build (gitignored). | **Tracks the Proxmox API release** (e.g. `9.2.2`) — breaking changes are Proxmox's. |
| `src/ProxmoxSharp/` | Hand-written runtime (`ProxmoxApi`, auth, options) over `.Api`. | **Independent SemVer** (`0.1.0`) — our surface's breaking changes drive the major. |
| `tools/ProxmoxSharp.SchemaGen/` | Converter: `apidoc.js` → OpenAPI 3.0. | — |
| `tests/ProxmoxSharp.Tests/` | Unit + (thin) read-only integration tests. | — |

The two packages move independently: bump the library for a new feature without
touching the API version; bump the API when you regenerate from a new Proxmox release.

## Build

```bash
dotnet tool restore     # restore Kiota (the build invokes it to regenerate)
dotnet build            # regenerates ProxmoxSharp.Api from the schema if it changed, then compiles
dotnet test
```

CI (`.github/workflows/ci.yml`) does the same on push/PR — a clean checkout
regenerates the client fresh.

## Use it

```csharp
var client  = ProxmoxApi.Create(options);   // token auth + base URL + TLS handling
var version = await client.Version.GetAsVersionGetResponseAsync();
var nodes   = await client.Nodes.GetAsNodesGetResponseAsync();
```

## Refresh the schema (new Proxmox release)

```bash
pwsh src/ProxmoxSharp.Api/schema/refresh.ps1 -Node hpe-01   # writes apidoc.<ver>.js
# then update <Version> + the schema filename in ProxmoxSharp.Api.csproj, and rebuild
```

## Dev token & secrets

Integration tests read a gitignored `secrets.env` (copy `secrets.env.example`).
Use a **dedicated read-only** token, not broad creds. The helper creates one
(privilege-separated, role `PVEAuditor` at `/`) and prints the `secrets.env` lines:

```bash
pwsh scripts/new-dev-token.ps1 -Node hpe-01
```

## Packages

Published to **GitHub Packages** by `.github/workflows/publish.yml` on a `v*` tag:

| Package | Version |
| --- | --- |
| `ProxmoxSharp.Api` | the Proxmox API release (e.g. `9.2.2`) |
| `ProxmoxSharp` | independent SemVer (e.g. `0.1.0`); depends on `ProxmoxSharp.Api` |

Consuming needs the GitHub feed (`nuget.config`) + a PAT with `read:packages`.

## Status

M1–M5 done. Generated client reads the live cluster; `ProxmoxDiscovery` produces
a structured `ClusterSnapshot` (nodes → LXC/QEMU/storage/network); both packages
publish to GitHub Packages. Coverage: `/version,/nodes,/cluster,/storage,/access`
(338 GET ops). Next: the hub consumes the package; BL-014 CLI; write path (BL-010).
