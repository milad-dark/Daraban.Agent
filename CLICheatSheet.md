# Daraban.Agent — CLI Cheat Sheet

All commands assume you're running from the repo root:
```bash
dotnet run --project Daraban.Agent.Cli -- <args>
```
(or `cd Daraban.Agent.Cli && dotnet run -- <args>`)

Build once first:
```bash
dotnet build
```

---

## 1. Quick one-off collector tests (`--method`)
No server, no task pipeline — runs one collector directly and dumps JSON to console + a file. Use these first to sanity-check a collector works at all.

```bash
# Local machine (auto-picks Windows/Linux/macOS collector)
dotnet run --project Daraban.Agent.Cli -- --method local --file local-test.json

# SSH remote collector
dotnet run --project Daraban.Agent.Cli -- --method ssh --host 192.168.1.50 --user root --password mypassword --file ssh-test.json

# WinRM remote collector
dotnet run --project Daraban.Agent.Cli -- --method winrm --host 192.168.1.51 --user Administrator --password mypassword --file winrm-test.json

# SNMP (--password doubles as the SNMP community string here)
dotnet run --project Daraban.Agent.Cli -- --method snmp --host 192.168.1.1 --password public --file snmp-test.json
```

---

## 2. List registered tasks
```bash
dotnet run --project Daraban.Agent.Cli -- list-tasks
```
Expected: `local`, `netdiscovery`, `netinventory`, `remote`, `wakeonlan`, `deploy`, `esx`

---

## 3. Each task individually, once, to a local folder (no server needed)
```bash
# Local inventory
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --once

# NetDiscovery (ping/ARP sweep)
dotnet run --project Daraban.Agent.Cli -- --tasks netdiscovery --ip-range 192.168.1.0/24 --local ./out --once

# NetInventory (SNMP sweep)
dotnet run --project Daraban.Agent.Cli -- --tasks netinventory --ip-range 192.168.1.0/24 --snmp-community public --snmp-timeout 2000 --local ./out --once

# WakeOnLan
dotnet run --project Daraban.Agent.Cli -- --tasks wakeonlan --wol-mac AA:BB:CC:DD:EE:FF,11:22:33:44:55:66 --local ./out --once

# ESX / vCenter
dotnet run --project Daraban.Agent.Cli -- --tasks esx --esx-host vcenter.local --esx-user administrator@vsphere.local --esx-password mypassword --local ./out --once
```
> `remote` (SSH/WinRM) has no per-target CLI flags in the task pipeline yet — use `--method ssh`/`--method winrm` above. `deploy` needs `--server` since jobs live server-side (see §5).

---

## 4. Everything at once, to a local folder
```bash
dotnet run --project Daraban.Agent.Cli -- \
  --tasks local,netdiscovery,netinventory,wakeonlan,esx \
  --ip-range 192.168.1.0/24 \
  --wol-mac AA:BB:CC:DD:EE:FF \
  --esx-host vcenter.local --esx-user administrator@vsphere.local --esx-password mypassword \
  --local ./out --once
```
Check `./out/` — one timestamped JSON file per task.

---

## 5. Against your server (`Daraban.Agent.Server`)
```bash
# terminal 1
cd Daraban.Agent.Server && dotnet run

# terminal 2
dotnet run --project Daraban.Agent.Cli -- --tasks local --server http://localhost:5000 --tag my-first-pc --once

dotnet run --project Daraban.Agent.Cli -- --tasks local,netdiscovery,netinventory --ip-range 192.168.1.0/24 --server http://localhost:5000 --tag office-pc-01 --once

# deploy: queue a job first via the server's /deploy Blazor page, then
dotnet run --project Daraban.Agent.Cli -- --tasks deploy --server http://localhost:5000 --tag my-first-pc --once

# with an API key (once server-side validation is added)
dotnet run --project Daraban.Agent.Cli -- --tasks local --server http://localhost:5000 --api-key supersecret123 --tag my-first-pc --once
```
Open `http://localhost:5000` — the device and its inventory JSON should appear within seconds.

---

## 6. Daemon / loop mode (what the service runs internally)
Omit `--once` → runs forever on `--delay` seconds.
```bash
# every 5 minutes, foreground, Ctrl+C to stop
dotnet run --project Daraban.Agent.Cli -- --tasks local,netinventory --server http://localhost:5000 --tag my-pc --delay 300

# with jitter, so a whole fleet doesn't hit the server at the same second
dotnet run --project Daraban.Agent.Cli -- --tasks local --server http://localhost:5000 --tag my-pc --delay 3600 --lazy
```

---

## 7. HTTP status interface
```bash
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --delay 60
# in another terminal:
curl http://localhost:62354/status

# custom port
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --delay 60 --http-port 8080

# disable it
dotnet run --project Daraban.Agent.Cli -- --tasks local --local ./out --once --no-httpd
```

---

## 8. Excluding specific tasks
```bash
dotnet run --project Daraban.Agent.Cli -- --tasks local,netdiscovery,netinventory,wakeonlan --no-task netdiscovery,wakeonlan --local ./out --once
```

---

## 9. Windows Service / systemd
```bash
# Windows
sc create DarabanAgent binPath= "C:\path\to\Daraban.Agent.Service.exe"
sc start DarabanAgent

# Linux (systemd), config comes from appsettings.json "Agent" section
sudo systemctl daemon-reload
sudo systemctl start daraban-agent
sudo systemctl status daraban-agent
journalctl -u daraban-agent -f
```

---

## Full switch reference

| Switch | Default | Applies to |
|---|---|---|
| `--server <url>` | — | target server |
| `--local <dir>` | — | write to disk instead of server |
| `--tag <id>` | machine name | device id reported |
| `--api-key <key>` | — | `X-Api-Key` header |
| `--tasks <list>` | `local` | comma-separated task names |
| `--no-task <list>` | — | comma-separated tasks to skip |
| `--delay <seconds>` | `3600` | loop interval (ignored with `--once`) |
| `--lazy` | `false` | random jitter before each run |
| `--once` | `false` | run once and exit instead of looping |
| `--http-port <n>` | `62354` | status endpoint port |
| `--http-trust <cidr>` | — | restrict status endpoint (not yet enforced) |
| `--no-httpd` | `false` | disable status endpoint |
| `--ip-range <cidr>` | — | netdiscovery/netinventory sweep range |
| `--snmp-community <s>` | `public` | netinventory |
| `--snmp-timeout <ms>` | `2000` | netinventory |
| `--discovery-threads <n>` | `32` | netdiscovery/netinventory parallelism |
| `--wol-mac <list>` | — | comma-separated MACs to wake |
| `--wol-broadcast <ip>` | `255.255.255.255` | wakeonlan |
| `--deploy-workdir <dir>` | temp | deploy staging directory |
| `--esx-host / --esx-user / --esx-password` | — | ESX task |
| `--method local\|ssh\|snmp\|winrm` | — | one-off collector, bypasses tasks |
| `--host / --user / --password / --file` | see above | used with `--method` |

`list-tasks` subcommand: prints every registered task name.