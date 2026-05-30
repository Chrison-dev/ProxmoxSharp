# ProxmoxSharp

A C# API client for Proxmox VE — **mostly code-generated from Proxmox's own
published API schema**, with a thin hand-written runtime for auth and transport.

Built to be dogfooded by the [Homelab](https://github.com/ChrisonSimtian/Homelab)
hub's C#-native IaC (the Discover → Converge path). Design + roadmap live in the
hub at `docs/plans/BL-009-proxmoxsharp-codegen.md`.

## Approach

```
apidoc.js (version-matched, pulled from our node)
   │  ProxmoxSharp.SchemaGen   → OpenAPI 3.0 (openapi.json, committed)
   ▼
Kiota (pinned dotnet tool)     → generated C# request builders + models
   ▼
ProxmoxSharp                   = generated client + hand-written runtime (PVEAPIToken auth, {data:…} envelope)
```

- **Read-only first** — token auth, the read path (nodes / LXC / VM / storage /
  network) before any write/lifecycle.
- **Schema is version-matched** to our cluster (PVE 9.2.2) and pinned under
  `schema/`. Regeneration is explicit and diffable.

## Layout

| Path | What |
| --- | --- |
| `src/ProxmoxSharp/` | The client library (net10.0) — runtime + (soon) generated code. |
| `tools/ProxmoxSharp.SchemaGen/` | Converter: `apidoc.js` → OpenAPI 3.0. |
| `tests/ProxmoxSharp.Tests/` | Unit + (thin) read-only integration tests. |
| `schema/` | Pinned `apidoc.<pve-version>.js` + `refresh.ps1`. |
| `.config/dotnet-tools.json` | Pins Kiota. |

## Build

```bash
dotnet tool restore     # restore Kiota
dotnet build
dotnet test
```

## Refresh the schema

```bash
pwsh schema/refresh.ps1 -Node hpe-01    # auto-detects PVE version, writes apidoc.<ver>.js
```

## Dev token & secrets

Integration tests read a gitignored `secrets.env` (copy `secrets.env.example`).
Use a **dedicated read-only** token, not broad creds. The helper creates one
(privilege-separated, role `PVEAuditor` at `/`) and prints the `secrets.env` lines:

```bash
pwsh scripts/new-dev-token.ps1 -Node hpe-01
```

## Status

Early — M1 scaffold. See the hub plan for milestones (M1 scaffold → M2 first
authed read → M3 generator → M4 discover → M5 package).
