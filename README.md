# Daraban.Agent

Cross-platform IT inventory and management agent (Windows / Linux / macOS) written in C#/.NET, modeled on [glpi-agent](https://github.com/glpi-project/glpi-agent). It inventories the local machine and network, wakes machines, runs agentless remote inventories over SSH/WinRM, inventories VMware vCenter/ESXi, pulls software-deploy jobs and ad-hoc "collect" jobs from a server, and can run as a CLI, a foreground daemon, or an installed system service.

| Project | What it is |
|---|---|
| `src/Daraban.Agent.Core` | Collectors, tasks, transport, config — shared by everything else |
| `src/Daraban.Agent.Cli` | Interactive CLI — run any task once, loop it, or test a single collector |
| `src/Daraban.Agent.Service` | Long-running worker service (Windows Service / systemd) driven by `appsettings.json` |
| `tests/Daraban.Agent.Tests` | xUnit test suite |

---

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (projects target `net10.0`)
- Admin/root rights only for: Wake-on-LAN on some NICs, the `test-dhcp-sniff` command, and installing the service
- A running server to report to (your `Daraban.Agent.Server` / GLPI-compatible endpoint) — optional; everything can also write JSON files to disk

## Build

```bash
dotnet build
```

## Run the test suite

```bash
dotnet test
```

---

## Quick start (2 terminals)

```bash
# Terminal 1 — the agent, local inventory written to ./out, no server needed
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --once

# Or against a server (terminal 2 would be the server):
dotnet run --project Daraban.Agent.Cli -- --tasks local --server http://localhost:5000 --tag my-first-pc --once
```

Results: one timestamped JSON file per task in `./out`, or a device entry in the server's UI within seconds.

List every task this build knows about:

```bash
dotnet run --project Daraban.Agent.Cli -- list-tasks
# local, netdiscovery, netinventory, remote, wakeonlan, deploy, esx, collect
```

> All CLI examples below assume you run from the repo root: `dotnet run --project Daraban.Agent.Cli -- <args>` (or `cd src/Daraban.Agent.Cli && dotnet run -- <args>`).

---

## Testing every feature from the CLI

### 0. Sanity-check a single collector (`--method`)

No task pipeline, no server — runs one collector directly and dumps JSON to console and a file. Start here when something doesn't work.

```bash
dotnet run --project Daraban.Agent.Cli -- --method local --file local-test.json
dotnet run --project Daraban.Agent.Cli -- --method ssh   --host 192.168.1.50 --user root        --password mypassword --file ssh-test.json
dotnet run --project Daraban.Agent.Cli -- --method winrm --host 192.168.1.51 --user Administrator --password mypassword --file winrm-test.json
dotnet run --project Daraban.Agent.Cli -- --method snmp  --host 192.168.1.1  --password public  --file snmp-test.json
```

### 1. `local` — local machine inventory

Auto-picks the Windows / Linux / macOS collector via `LocalCollectorFactory`.

```bash
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --once
```

Useful extras (mirror glpi-agent):

```bash
# Exclude expensive sections from the inventory
dotnet run --project Daraban.Agent.Cli -- --tasks local --no-category process,software --local ./out --once

# Scan home dirs for VMs/licenses and per-user installed software (slower, off by default)
dotnet run --project Daraban.Agent.Cli -- --tasks local --scan-homedirs --scan-profiles --local ./out --once

# Merge extra content into the inventory before sending
dotnet run --project Daraban.Agent.Cli -- --tasks local --additional-content extra.json --local ./out --once
```

### 2. `netdiscovery` — ICMP/ARP network sweep

```bash
dotnet run --project Daraban.Agent.Cli -- --tasks netdiscovery --ip-range 192.168.1.0/24 --local ./out --once

# Tuned: more threads, longer SNMP timeout
dotnet run --project Daraban.Agent.Cli -- --tasks netdiscovery --ip-range 192.168.1.0/24 \
  --snmp-community public --snmp-timeout 2000 --discovery-threads 32 --local ./out --once
```

### 3. `netinventory` — SNMP inventory of network devices

```bash
dotnet run --project Daraban.Agent.Cli -- --tasks netinventory --ip-range 192.168.1.0/24 \
  --snmp-community public --snmp-timeout 2000 --local ./out --once

# SNMPv3 instead of v2c
dotnet run --project Daraban.Agent.Cli -- --tasks netinventory --ip-range 192.168.1.0/24 \
  --snmp-version v3 --snmp-v3-user snmpadmin --snmp-v3-auth-pass xxx --snmp-v3-auth-protocol SHA \
  --snmp-v3-priv-pass xxx --snmp-v3-priv-protocol AES --local ./out --once
```

### 4. `remote` — agentless inventory of other machines (SSH/WinRM)

This task has no per-target CLI flags; targets come from config (`AgentOptions.RemoteHosts`), e.g. in the service's `appsettings.json`:

```json
"RemoteHosts": [
  "ssh://root:mypassword@10.0.0.5",
  "winrm://Administrator:mypassword@10.0.0.6:5985",
  "winrm://Administrator:mypassword@10.0.0.7:5986"
]
```

Port `5986` on a `winrm://` entry automatically switches to HTTPS. (For a quick one-host test with CLI flags, use `--method ssh` / `--method winrm` from section 0.)

### 5. `wakeonlan` — wake machines by MAC

```bash
dotnet run --project Daraban.Agent.Cli -- --tasks wakeonlan \
  --wol-mac AA:BB:CC:DD:EE:FF,11:22:33:44:55:66 --local ./out --once

# Directed broadcast to a specific subnet instead of 255.255.255.255
dotnet run --project Daraban.Agent.Cli -- --tasks wakeonlan \
  --wol-mac AA:BB:CC:DD:EE:FF --wol-broadcast 192.168.1.255 --local ./out --once
```

### 6. `esx` — vCenter/ESXi host + VM inventory

```bash
dotnet run --project Daraban.Agent.Cli -- --tasks esx \
  --esx-host vcenter.local --esx-user administrator@vsphere.local --esx-password mypassword \
  --local ./out --once
```

### 7. `deploy` — pull and run software-deploy jobs (needs `--server`)

Deploy jobs are queued server-side. The agent pulls pending jobs for its device id (`--tag`), downloads each file, verifies SHA-256 checksums (refuses to run anything that fails the checksum gate), runs the install command in the staged folder, and reports the result back.

```bash
# 1. Queue a job on the server's deploy page
# 2. Then:
dotnet run --project Daraban.Agent.Cli -- --tasks deploy --server http://localhost:5000 --tag my-first-pc --once

# Stage downloads somewhere specific instead of %TEMP% / /tmp
dotnet run --project Daraban.Agent.Cli -- --tasks deploy --server http://localhost:5000 \
  --deploy-workdir C:\daraban-staging --once
```

### 8. `collect` — ad-hoc registry / WMI / file / command jobs

Reads `collect-jobs.json` from the `--local` directory (local mode) or pulls jobs from the server. Job types: `RegistryKey` (Windows), `WmiQuery` (Windows), `FileContent`, `Command`.

```bash
# Local mode: put a collect-jobs.json in ./out first, then
dotnet run --project Daraban.Agent.Cli -- --tasks collect --local ./out --once
```

Example `collect-jobs.json`:

```json
[
  { "JobId": "os-caption", "Type": "WmiQuery", "WmiNamespace": "root\\cimv2", "WmiQuery": "SELECT Caption FROM Win32_OperatingSystem", "WmiProperty": "Caption" },
  { "JobId": "env-path",    "Type": "RegistryKey", "RegistryHive": "HKLM", "RegistryPath": "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment", "RegistryValue": "Path" },
  { "JobId": "host-file",   "Type": "FileContent", "FilePath": "/etc/hostname" },
  { "JobId": "uptime",      "Type": "Command", "Command": "uptime" }
]
```

### 9. `test-dhcp-sniff` — DHCP hostname sniffer (diagnostic, needs admin/root)

Listens on UDP/67 for DHCP Host Name (option 12) broadcasts and prints MAC → hostname mappings.

```bash
dotnet run --project Daraban.Agent.Cli -- test-dhcp-sniff --seconds 60
```

### 10. Run several tasks at once

```bash
dotnet run --project Daraban.Agent.Cli -- \
  --tasks local,netdiscovery,netinventory,wakeonlan,esx \
  --ip-range 192.168.1.0/24 --wol-mac AA:BB:CC:DD:EE:FF \
  --esx-host vcenter.local --esx-user administrator@vsphere.local --esx-password mypassword \
  --local ./out --once

# Exclude specific tasks from the set
dotnet run --project Daraban.Agent.Cli -- \
  --tasks local,netdiscovery,netinventory,wakeonlan --no-task netdiscovery,wakeonlan --local ./out --once
```

### 11. Partial vs. full inventory (loop mode)

```bash
# Every run sends a full inventory; after 14 runs the agent may send only changed
# categories. --required-category keeps listed categories in every partial inventory.
dotnet run --project Daraban.Agent.Cli -- --tasks local --server http://localhost:5000 \
  --full-inventory-postpone 14 --required-category network --tag my-pc --delay 300
```

---

## Daemon / loop mode (what the service runs internally)

Omit `--once` and the CLI loops forever on `--delay` seconds — same `AgentRunner` code path the installed service uses. Ctrl+C stops it cleanly.

```bash
# Every 5 minutes, foreground
dotnet run --project Daraban.Agent.Cli -- --tasks local,netinventory \
  --server http://localhost:5000 --tag my-pc --delay 300

# With random jitter, so a whole fleet doesn't hit the server at the same second
dotnet run --project Daraban.Agent.Cli -- --tasks local \
  --server http://localhost:5000 --tag my-pc --delay 3600 --lazy
```

### HTTP status interface

While looping, the agent exposes `GET /status` with live task state:

```bash
curl http://localhost:62354/status

# Custom port / disabled
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --delay 60 --http-port 8080
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --once --no-httpd
```

---

## Sending to multiple servers

`--server` accepts a comma-separated list; every target gets a copy of the results:

```bash
dotnet run --project Daraban.Agent.Cli -- --tasks local \
  --server http://server-a:5000,http://server-b:5000 --tag my-pc --once
```

---

## Configuration

Configuration can come from three places: CLI flags (CLI), `appsettings.json` → `"Agent"` section (service), and environment variables.

### 1. Service configuration — `src/Daraban.Agent.Service/appsettings.json`

The Windows service / systemd unit reads everything from the `"Agent"` section — it maps 1:1 to `AgentOptions`:

```jsonc
{
  "Agent": {
    "Servers": [ "http://localhost:5000" ],   // one or more target servers
    "Local": null,                            // or a directory to write JSON files instead
    "Tag": null,                              // device id (defaults to machine name)
    "AgentId": null,                          // defaults to hostname; env DARABAN_AGENT_ID also works
    "ApiKey": null,                           // sent as X-Api-Key header

    "Tasks": [ "local", "netdiscovery", "netinventory", "remote", "deploy" ],
    "NoTasks": [],
    "DelayTimeSeconds": 3600,                 // seconds between runs
    "Lazy": false,                            // random jitter before each run"Threads": 4,                              // parallelism for collect jobs (service config only, no CLI flag)

    "HttpPort": 62354,
    "HttpTrust": null,                        // CIDR allowed to query /status
    "NoHttpd": false,

    "IpRange": "192.168.1.0/24",
    "SnmpCommunity": "public",
    "SnmpTimeoutMs": 2000,
    "SnmpRetries": 0,
    "SnmpVersion": "v2c",                     // v1 | v2c | v3
    "SnmpV3User": null, "SnmpV3AuthPass": null, "SnmpV3AuthProtocol": "MD5",
    "SnmpV3PrivPass": null, "SnmpV3PrivProtocol": "AES",
    "DiscoveryThreads": 32,

    "WakeOnLanMacs": [],                      // e.g. [ "AA:BB:CC:DD:EE:FF" ]
    "WakeOnLanBroadcast": null,               // defaults to 255.255.255.255

    "DeployWorkDir": null,                    // defaults to the temp directory

    "EsxHost": null, "EsxUser": null, "EsxPassword": null,
    "EsxIgnoreSslErrors": true,

    "RemoteHosts": [],                        // e.g. [ "ssh://user:pass@10.0.0.5" ]

    "Proxy": null,                            // "none" disables env-var proxy; or "http://host:port"
    "SslKeystore": null,                      // e.g. "My,CA" (Windows cert stores)
    "SslFingerprint": null,                   // SHA-256 of the server TLS cert to pin
    "FusionInventoryCompat": false,           // legacy FusionInventory XML protocol (GLPI 9.5)
    "UseGzip": false,                         // gzip POST payloads

    "FullInventoryPostpone": 14,              // runs between full inventories (0 = always full)
    "RequiredCategories": [],
    "NoCategories": [],                       // e.g. [ "process", "software" ]
    "AdditionalContent": null,
    "Itemtype": null,                         // GLPI 11+ custom asset type
    "EsxItemtype": null,
    "ScanHomeDirs": false,
    "ScanProfiles": false,
    "AssetNameSupport": 1                     // 1 short name, 2 as-found, 3 always FQDN
  }
}
```

> Secrets (`EsxPassword`, `SnmpV3*Pass`, `ApiKey`, `OAuthClientSecret`) should not be committed. Use environment variables, `dotnet user-secrets` (the Service project has a UserSecretsId), or your platform's secret store. OAuth2 client-credentials (`OAuthTokenEndpoint`, `OAuthClientId`, `OAuthClientSecret`, `OAuthScope`) activate automatically when `OAuthTokenEndpoint` is set.

### 2. Environment variables

| Variable | Effect |
|---|---|
| `DARABAN_AGENT_ID` | Overrides the default agent id (hostname) |
| `HTTP_PROXY` / `HTTPS_PROXY` | Used for server traffic unless `Proxy`/`--proxy` says otherwise (`none` disables) |
| `NO_PROXY` | Standard proxy exclusions |

### 3. Full CLI switch reference

| Switch | Default | Description |
|---|---|---|
| `--server <urls>` | — | Target server base URL(s), comma-separated |
| `--local <dir>` | — | Write results to this directory instead of a server |
| `--tag <id>` | machine name | Device id reported to the server |
| `--api-key <key>` | — | Sent as the `X-Api-Key` header |
| `--agent-id <id>` | hostname | Unique agent instance id |
| `--tasks <list>` | `local` | Comma-separated task names (see `list-tasks`) |
| `--no-task <list>` | — | Comma-separated tasks to skip |
| `--delay <seconds>` | `3600` | Seconds between scheduled runs (loop mode) |
| `--lazy` | off | Random jitter before each run |
| `--once` | off | Run once and exit instead of looping |
| `--http-port <n>` | `62354` | Status endpoint port |
| `--http-trust <cidr>` | — | CIDR allowed to query the status endpoint |
| `--no-httpd` | off | Disable the status endpoint |
| `--ip-range <cidr>` | — | Sweep range for netdiscovery/netinventory |
| `--snmp-community <s>` | `public` | SNMP community string |
| `--snmp-timeout <ms>` | `2000` | SNMP timeout per request |
| `--snmp-retries <n>` | `0` | SNMP retries per unresponsive device |
| `--snmp-version <v>` | `v2c` | `v1`, `v2c`, or `v3` |
| `--snmp-v3-user/--snmp-v3-auth-pass/--snmp-v3-auth-protocol/--snmp-v3-priv-pass/--snmp-v3-priv-protocol` | — | SNMPv3 USM settings (auth: MD5/SHA/SHA256, priv: DES/AES) |
| `--discovery-threads <n>` | `32` | Parallel probes for netdiscovery/netinventory |
| `--wol-mac <list>` | — | Comma-separated MACs to wake |
| `--wol-broadcast <ip>` | `255.255.255.255` | Broadcast address for WoL packets |
| `--deploy-workdir <dir>` | temp | Staging directory for deploy downloads |
| `--esx-host/--esx-user/--esx-password` | — | vCenter/ESXi credentials |
| `--full-inventory-postpone <n>` | `14` | Runs between full inventories (0 = always full) |
| `--required-category <list>` | — | Categories always kept in partial inventories |
| `--no-category <list>` | — | Categories to exclude from inventory |
| `--additional-content <file>` | — | XML/JSON file merged into the inventory |
| `--itemtype <t>` | `Computer` | GLPI 11+ custom asset type |
| `--esx-itemtype <t>` | `EsxHost` | Itemtype for ESX inventories |
| `--scan-homedirs` | off | Scan home dirs for VMs/licenses |
| `--scan-profiles` | off | Scan per-user installed software (Windows) |
| `--assetname-support <n>` | `1` | Name normalization: 1 short, 2 as-found, 3 FQDN |
| `--proxy <url\|none>` | inherit env | HTTP proxy for server traffic |
| `--ssl-keystore <s>` | — | Client certificate source (e.g. `My,CA`) |
| `--ssl-fingerprint <hex>` | — | SHA-256 of server TLS cert to trust |
| `--fusioninventory-compat` | off | Legacy FusionInventory XML protocol (GLPI 9.5) |
| `--gzip` | off | Gzip-compress POST payloads |
| `--method local\|ssh\|snmp\|winrm` | — | One-off collector test, bypasses tasks |
| `--host/--user/--password/--file` | — | Used with `--method` |

Subcommands: `list-tasks`, `test-dhcp-sniff [--seconds <n>]`.

---

## Running on a client computer (production)

### 1. Publish a self-contained build

```bash
dotnet publish src/Daraban.Agent.Service -c Release -r win-x64 --self-contained true \
  -o publish/windows/Daraban.Agent.Service
dotnet publish src/Daraban.Agent.Service -c Release -r linux-x64 --self-contained true \
  -o publish/linux/Daraban.Agent.Service
dotnet publish src/Daraban.Agent.Service -c Release -r osx-x64  --self-contained true \
  -o publish/macos/Daraban.Agent.Service
```

(Use `win-arm64` / `linux-arm64` / `osx-arm64` as needed. Drop `--self-contained true` if the client machine has the .NET runtime installed.)

Then configure the client: edit `appsettings.json` next to the published binary (see [Configuration](#configuration)) — at minimum set `Servers`, `Tag`/`AgentId`, `Tasks`, and the credentials any enabled task needs.

### 2. Install as a Windows service

Run an elevated PowerShell from the publish output folder:

```powershell
.\Install-Service.ps1
# Custom name/path:
.\Install-Service.ps1 -Name Daraban.Agent -BinPath "C:\Program Files\Daraban.Agent\Daraban.Agent.Service.exe"
```

The script stops and removes any existing `Daraban.Agent` service, installs the new one (LocalSystem, Automatic startup) and starts it.

```powershell
Get-Service Daraban.Agent           # status
sc.exe query Daraban.Agent
Stop-Service Daraban.Agent          # stop / start
Start-Service Daraban.Agent
sc.exe delete Daraban.Agent         # uninstall
```

### 3. Install as a systemd service (Linux)

The repo ships `src/Daraban.Agent.Service/Installer/daraban-agent.service`. It expects the published files at `/opt/daraban-agent`, a `daraban-agent` user, and `dotnet` on `/usr/bin/dotnet`:

```bash
sudo mkdir -p /opt/daraban-agent
sudo cp -r publish/linux/Daraban.Agent.Service/* /opt/daraban-agent/
sudo useradd -r -s /usr/sbin/nologin daraban-agent   # create the run-as user
sudo cp src/Daraban.Agent.Service/Installer/daraban-agent.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now daraban-agent
```

```bash
sudo systemctl status daraban-agent    # status
sudo systemctl restart daraban-agent   # restart
journalctl -u daraban-agent -f         # logs
```

### 4. Run as a launchd daemon (macOS)

There is no packaged plist; either run the published binary in a terminal (`./Daraban.Agent.Service`), or add a `LaunchDaemon` pointing at it (`RunAtLoad` + `KeepAlive`) — the same `appsettings.json` applies.

### 5. Firewall / ports on the client

| Direction | Port | Purpose |
|---|---|---|
| Outbound TCP | server port (e.g. 5000/443) | inventory/deploy/collect reporting |
| Inbound TCP | `HttpPort` (default 62354) | local `/status` endpoint (disable with `NoHttpd: true`) |
| Outbound UDP | 161 | SNMP (netinventory) |
| Outbound UDP | 9 | Wake-on-LAN magic packets |
| Outbound TCP | 22 / 5985 / 5986 | SSH / WinRM (remote task, `--method` tests) |
| Outbound TCP | 443 | vCenter/ESXi REST API (esx task) |

---

## Security notes

- **Deploy integrity gate**: deploy files are SHA-256-verified against the job manifest before the install command runs; a mismatch aborts the job with `ChecksumFailed`.
- **API key**: the agent sends `X-Api-Key` when `ApiKey`/`--api-key` is set. Verify your server actually validates it before relying on it.
- **Credentials in config**: ESX/SNMP/SSH/WinRM credentials and API keys sit in plaintext config — restrict file permissions, prefer secrets stores, and never commit them.
- **`collect` runs commands**: collect jobs of type `Command` execute arbitrary commands on the client. Only enable the `collect` task against servers you trust.
- **Status endpoint** binds on `HttpPort` (default 62354) and exposes task status; use `HttpTrust`/`--http-trust` to restrict, or `NoHttpd` to disable.

## Known limitations

- BIOS version / exact CPU string for ESX hosts need the legacy SOAP API and are left `null` by the REST collector.
- `remote` targets come from config only — no per-target CLI flags yet (use `--method ssh|winrm` for one-offs).
- `HttpTrust` CIDR filtering is not fully enforced; treat `/status` as local-only.
- RegistryKey/WmiQuery collect job types are Windows-only (the agent runs fine elsewhere; those job types just fail).

## Repository layout

```
src/Daraban.Agent.Core/          # collectors, tasks (Agents/), transport, config, models
src/Daraban.Agent.Cli/           # CLI entry point (Program.cs)
src/Daraban.Agent.Service/       # worker service + Installer/ (PS1 + systemd unit)
tests/Daraban.Agent.Tests/       # xUnit unit tests
docs/decisions/                  # Architecture Decision Records (ADRs)
CLICheatSheet.md                 # condensed command cheat sheet
Install-Service.ps1              # top-level copy of the Windows service installer
```
