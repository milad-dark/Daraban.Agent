# Daraban.Agent — full drop-in package

Copy each top-level folder here over the matching project folder in your solution.
Everything below is NEW or REPLACES an existing file of the same name — nothing here
needs merging by hand, just overwrite.

## Daraban.Agent.Core/

**Collectors/**
- `LocalLinuxCollector.cs` — NEW. Local inventory for Linux (was 100% missing; you only had LocalWindowsCollector.cs).
- `LocalMacCollector.cs` — NEW. Local inventory for macOS (also 100% missing).
- `LocalCollectorFactory.cs` — NEW. Picks Windows/Linux/macOS collector at runtime via `RuntimeInformation.IsOSPlatform`.
- `WakeOnLanSender.cs` — NEW. Magic-packet builder/sender.
- `EsxRestCollector.cs` — NEW. vCenter/ESXi host+VM inventory via the vSphere REST API.
- *(SnmpNetworkCollector.cs / SshRemoteCollector.cs / WinrmRemoteCollector.cs — you already have these, unchanged, not included here)*

**Models/**
- `NetworkModels.cs` — NEW. Shapes for NetDiscovery/NetInventory/WakeOnLan/Deploy/ESX (`DiscoveredHost`, `NetworkDeviceInventory`, `WakeOnLanResult`, `DeployJob`, `EsxHostInfo`, etc.)

**Config/**
- `AgentOptions.cs` — REPLACES your existing one. Adds `IpRange`, `SnmpCommunity`, `WakeOnLanMacs`, `EsxHost/User/Password`, `DeployWorkDir`, `ApiKey`, `RunOnce`, etc.

**Agents/**
- `LocalInventoryTask.cs` — REPLACES the stub that never called any collector.
- `NetDiscoveryTask.cs` — REPLACES the hardcoded-range version; now parses any `--ip-range` CIDR.
- `NetInventoryTask.cs` — NEW. Didn't exist as a task before (SnmpNetworkCollector had no caller).
- `WakeOnLanTask.cs` — NEW.
- `DeployTask.cs` — NEW. Downloads, SHA-256 verifies, installs, reports back.
- `EsxInventoryTask.cs` — NEW.
- `TaskRegistry.cs` — REPLACES. Registers all 7 tasks (was 3).
- `AgentRunner.cs` — NEW. The scheduler — prolog + selected tasks, once or looped on `--delay`. Used by both CLI and Service.
- `AgentStatusTracker.cs` — NEW. Backs `/status` with real task state instead of a hardcoded string.

**Transport/**
- `darabanClient.cs` — REPLACES the 27-line stub. Talks to *your own* `Daraban.Agent.Server` (`/api/agent/*` routes), not stock daraban.
- `IdarabanClient.cs` — REPLACES. One method per task type.
- `darabanClientFactory.cs` — NEW. Single place that builds a `darabanClient` (base URL + `X-Api-Key` header), used by every task instead of each one repeating the wiring.

**Http/**
- `StatusEndpoint.cs` — REPLACES. Reports live task status via `AgentStatusTracker`.

## Daraban.Agent.Cli/
- `Program.cs` — REPLACES. Adds switches: `--server --local --tag --api-key --tasks --no-task --delay --lazy --once --http-port --http-trust --no-httpd --ip-range --snmp-community --snmp-timeout --discovery-threads --wol-mac --wol-broadcast --deploy-workdir --esx-host --esx-user --esx-password`, plus a `list-tasks` subcommand. The old `--method local|ssh|snmp|winrm` one-off tester still works.

## Daraban.Agent.Service/
- `Worker.cs` — REPLACES. **This was the critical bug**: it used to log a heartbeat every second and never call any task. Now delegates to `AgentRunner`.
- `Program.cs` — REPLACES. Registers all 7 tasks (was 3) and — the actual fix — calls `services.AddHostedService<Worker>()`, which was missing, so the service ran but did nothing.
- `appsettings.json` — REPLACES. All new config keys under `"Agent"`.

## Daraban.Agent.Server/ (whole new project — you had none)
Add this as a new project in your solution (`dotnet sln add Daraban.Agent.Server`).
- ASP.NET Core Web API + Blazor Server, SQLite storage (auto-created on first run).
- API routes: `GET /api/agent/prolog`, `POST /api/agent/{inventory,discovery,netinventory,wakeonlan,esx}`, `GET /api/agent/deploy/jobs`, `POST /api/agent/deploy/result`.
- Blazor pages: `/` (device list), `/device/{id}` (raw JSON per submission), `/deploy` (queue jobs, watch results).
- Needs two NuGet packages added: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`.

## Quick start after copying everything in
```bash
# 1. Server
cd Daraban.Agent.Server
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet run

# 2. Agent (in another terminal)
dotnet run --project Daraban.Agent.Cli -- --tasks local --server http://localhost:5000 --tag my-first-pc --once
```
Open the server's URL — the device should appear within seconds with its inventory JSON viewable.

## What's still genuinely missing (not in this package, not started)
- **Collect task** — daraban-agent's ad-hoc "run this command / read this file / read this registry key / WMI query" task. Zero code exists for this anywhere in Daraban.Agent yet.
- **Deep per-OS inventory depth** — daraban-agent has 76 shared + 47 Linux-specific + 25 macOS-specific inventory submodules (RAID controllers, per-CPU-architecture detection, antivirus-vendor detection, LVM, etc.). `LocalLinuxCollector.cs`/`LocalMacCollector.cs` here cover the core categories in one file each, not that full breadth.
- **Solaris/AIX/HP-UX/BSD** local inventory — not implemented.
- **API-key validation on the server** — `AgentOptions.ApiKey`/`X-Api-Key` header exist on the agent side, but `AgentController` doesn't check it yet, so auth isn't enforced.
- **Deploy manifest signing** — current version trusts a SHA-256 checksum only; daraban-agent signs job manifests.
- **ESX SOAP-only hardware fields** — BIOS version/exact CPU string need the legacy SOAP API; the REST collector leaves these `null`.
