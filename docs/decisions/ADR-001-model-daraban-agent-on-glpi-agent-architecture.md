# ADR-001: Model Daraban.Agent on glpi-agent's task/collector architecture

## Status

Accepted

## Date

2026-09-26

## Context

Daraban.Agent had to become a cross-platform (Windows/Linux/macOS) IT asset-management agent for fleet deployments, covering:

- **Local inventory** of the machine it runs on (hardware, OS, software, network)
- **Network discovery** (ICMP/ARP sweep of a CIDR range) and **network inventory** (SNMP polling of discovered devices)
- **Agentless remote inventory** of other machines over SSH and WinRM
- **Wake-on-LAN** for powered-off machines
- **vCenter/ESXi** host and VM inventory
- **Software deployment** (pull jobs from a server, verify, install, report)
- **Collect jobs** (server-pushed ad-hoc registry/WMI/file/command queries)
- Three execution modes that must share identical behavior: one-shot CLI, looping foreground daemon, and installed system service (Windows Service / systemd)

Hard constraints:

- The team is a .NET shop; the agent runs as LocalSystem/root on client machines, so the runtime must be strong-typed, servable, and produce a single self-contained binary per OS.
- It must integrate with both **GLPI** (including legacy FusionInventory deployments on GLPI 9.5) and our **own server backend** (native JSON API with API-key/OAuth auth).
- Results must go to multiple targets (several servers, or a local directory for testing/offline).

The question was not *which features* to build — the feature list was fixed by the requirements above. The question was *what shape the codebase should take*: how tasks are decomposed, selected, scheduled, and configured, and whether to adopt an existing agent as the reference model.

## Decision

Adopt **glpi-agent** (the Perl reference agent of the GLPI project, successor of FusionInventory) as the architectural model, translated idiomatically into C#/.NET:

1. **Task decomposition.** Every capability is a small, independently selectable task implementing `IAgentTask` (a `RunAsync(AgentOptions, CancellationToken)` contract), registered in a central compile-time `TaskRegistry`. Task names deliberately match glpi-agent's: `local`, `netdiscovery`, `netinventory`, `remote`, `wakeonlan`, `deploy`, `esx`, `collect`.

2. **One scheduler, all entry points.** A single `AgentRunner` implements prolog → selected tasks → delivery, once (`--once`) or looped on `--delay` with optional `--lazy` jitter. The CLI daemon mode and the Windows/systemd service (`Worker`) both delegate to this same code path, so the two modes cannot drift apart — the service exists purely as a hosted wrapper around the runner.

3. **Collector abstraction per transport.** OS collectors behind a factory (`LocalCollectorFactory` → `LocalWindowsCollector` / `LocalLinuxCollector` / `LocalMacCollector`), plus dedicated collectors per protocol (`SshRemoteCollector`, `WinrmRemoteCollector`, `SnmpNetworkCollector`, `EsxRestCollector`, `WakeOnLanSender`). Tasks orchestrate collectors; collectors never talk to the network delivery layer.

4. **Target abstraction with multi-delivery.** `AgentOptions` holds a list of servers *or* a local directory; every task delivers its result to all configured targets (comma-separated `--server`, mirroring glpi-agent's multi-target support). `--local` turns any task into a "write timestamped JSON files" mode, which is also the offline testing path.

5. **Option-name compatibility.** CLI switches and config keys mirror glpi-agent's names and defaults where they exist: `--ip-range`, `--snmp-community`, `--snmp-timeout`, `--lazy`, `--delay`, `--http-port` (default **62354**, same as glpi-agent's httpd), `--http-trust`, `--no-category`, `--full-inventory-postpone` (default 14), `--scan-homedirs`, `--scan-profiles`, `--assetname-support`, `--additional-content`, `--itemtype`, `--esx-itemtype`, `--ssl-fingerprint`, `--ssl-keystore`, `--proxy` (with `none` to disable env-var proxying), `--gzip`, plus `--fusioninventory-compat` for the legacy GLPI 9.5 XML protocol.

6. **Status endpoint parity.** A minimal HTTP interface on port 62354 exposing live task state (`/status`), mirroring glpi-agent's local httpd, backed by an `AgentStatusTracker`.

## Alternatives Considered

### A. Design our own task taxonomy from scratch

- **Pros:** No prior-art mapping burden; naming free to fit our own domain.
- **Cons:** Re-invents problems glpi-agent has already solved and field-hardened over ~15 years of FusionInventory lineage: CIDR sweep semantics, SNMP retry/timeout behavior, partial-inventory diffing (`full-inventory-postpone`), WoL directed-broadcast subtleties, deploy checksum gating, status-port conventions. Every one of these has operational edge cases that only surface in real fleets.
- **Rejected:** The task decomposition is not the expensive part of this domain — the edge cases are, and they are already encoded in glpi-agent. Copying the shape gets us the edge cases for free.

### B. Reuse glpi-agent itself (embed or shell out to the Perl agent)

- **Pros:** Full feature parity on day one (glpi-agent ships ~76 shared + 47 Linux-specific + 25 macOS-specific inventory submodules, RAID/LVM/antivirus detection, Solaris/AIX/BSD support).
- **Cons:** Requires a Perl runtime and CPAN dependency management on every managed client — painful on Windows, hostile to self-contained deployment. A GPLv2 Perl codebase cannot be embedded in our .NET service or extended in-house. Our own server protocol (API key, OAuth enrollment, native JSON) would still need glue code anyway.
- **Rejected:** Distribution and ownership costs dominate. We consciously accept lower inventory depth in exchange for a single self-contained .NET binary per OS (see Consequences).

### C. Adopt a commercial/proprietary .NET asset-management agent SDK

- **Pros:** Less code to write; vendor support.
- **Cons:** No credible open-source .NET equivalent with comparable breadth exists. Per-node commercial licensing is prohibitive for a fleet-wide agent, and the extension model is closed — adding our `collect`/`deploy` semantics would be impossible.
- **Rejected:** No viable candidate; licensing and lock-in.

### D. A monolithic "inventory everything" command instead of a task pipeline

- **Pros:** Simpler CLI; one code path.
- **Cons:** Destroys scheduling independence: netdiscovery (seconds, sweep a /24) and local inventory (deep scan) cannot run on different cadences; the service cannot poll deploy/collect jobs separately; an expensive optional section (e.g. `--scan-homedirs`) would slow every run; there is no way to exclude tasks per deployment (`--no-task`).
- **Rejected:** Scheduling independence is a core requirement, and this is the single most valuable property of glpi-agent's design.

### E. Dynamic plugin architecture (load task assemblies at runtime)

- **Pros:** Third parties (or ops) could add tasks without recompiling.
- **Cons:** Version-skew and security surface on software that runs as LocalSystem/root on every client: an unvalidated plugin drop directory is a privilege-escalation vector. Task implementations here are tiny (one file each), so compile-time registration costs almost nothing.
- **Rejected:** Compile-time `TaskRegistry` is simpler and safer. Revisit only if external task authorship becomes a real requirement.

## Consequences

### Positive

- **Familiarity and documentation leverage.** GLPI administrators already know these task names, flags, and defaults; our docs can reference glpi-agent's man pages for semantics. Onboarding IT staff requires learning almost no new vocabulary.
- **GLPI integration is achievable** rather than aspirational: `--fusioninventory-compat` speaks the legacy XML protocol, and `itemtype`/`esx-itemtype` map to GLPI 11+ custom asset types.
- **One scheduler, three modes.** CLI one-shot, CLI daemon, and installed service share `AgentRunner`; a fix to scheduling behavior lands everywhere at once. (This directly fixed an earlier failure mode: the service previously ran a heartbeat loop that never invoked any task.)
- **Cheap extension recipe.** A new capability = one `IAgentTask` implementation + one `TaskRegistry` line + config keys. The `collect` task was added exactly this way with no scheduler changes.
- **Offline-first testability.** Because every task accepts `--local`, the whole pipeline can be exercised without a server, and CI tests deserialize real output files.

### Negative / accepted trade-offs

- **Inventory depth is far below glpi-agent.** One collector file per OS versus ~148 specialized submodules. RAID controllers, LVM, per-architecture CPU detection, antivirus-vendor detection, and Solaris/AIX/BSD support are missing or shallow. Accepted per alternative B: the .NET single-binary deployment wins outweigh full parity.
- **Semantic-drift risk.** Reusing glpi-agent's option names creates an expectation of identical behavior that we only partially meet. Known gaps must be documented (e.g. `--http-trust` CIDR filtering is not yet enforced; `remote` has no per-target CLI flags). Every mirrored option we half-implement is a support liability until closed.
- **Coupling to GLPI vocabulary.** Internal names (`EsxHost`, `CollectJob`, `FusionInventoryCompat`) carry GLPI's domain language even where our own server uses different terms. Accepted: the cost of a translation layer exceeds the cost of the borrowed vocabulary.
- **Two protocol implementations to maintain.** Native JSON and FusionInventory XML compat both need test coverage as endpoints evolve.

### Follow-ups

- ADR-002 should record the transport decision (native JSON API vs. FusionInventory XML, and when each is used).
- ADR-003 should record the authentication path (API-key header today, OAuth2 client-credentials enrollment planned), including the server-side enforcement gap.
- Track and close semantic gaps against glpi-agent (`http-trust` enforcement, per-target flags for `remote`) or explicitly document them as unsupported.

## References

- glpi-agent project: <https://github.com/glpi-project/glpi-agent> (Perl, GPLv2)
- glpi-agent configuration reference (option names mirrored above): <https://glpi-agent.readthedocs.io/en/stable/configuration.html>
- glpi-agent task documentation (netdiscovery, netinventory, deploy, collect, esx, remoteinventory): <https://glpi-agent.readthedocs.io/en/stable/tasks/>
- Codebase: `src/Daraban.Agent.Core/Agents/TaskRegistry.cs` (task list), `src/Daraban.Agent.Core/Agents/AgentRunner.cs` (scheduler), `src/Daraban.Agent.Core/Config/AgentOptions.cs` (mirrored options), `src/Daraban.Agent.Cli/Program.cs` (CLI surface), `src/Daraban.Agent.Service/Worker.cs` (service delegation)
