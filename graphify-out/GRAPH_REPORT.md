# Graph Report - Agent  (2026-08-28)

## Corpus Check
- Corpus is ~31,973 words - fits in a single context window. You may not need a graph.

## Summary
- 719 nodes · 1326 edges · 39 communities (37 shown, 2 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 98 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Service Infrastructure
- Memory & Hardware Info
- HTTP Transport Layer
- Remote Inventory (SSH/WinRM)
- ESX/vCenter Inventory
- Namespace Organization
- Deploy Task
- Collect Task Implementation
- Agent Configuration
- Network Discovery Task
- Device Content Models
- OS Detection & Linux Collector
- Windows WMI Collector
- NuGet Dependencies
- Wake-On-LAN Task
- SNMP Network Collector
- macOS Collector
- Audio Device Models
- Collect Result Models
- Net Inventory Task
- Agent Task Interface
- Collect Task Orchestration
- Service Launch Settings
- OAuth Token Provider
- Printer Info Models
- Remote Entry Models
- Computer System Info
- Desktop Info Models
- Process Info Models
- User Account Info
- Unit Tests
- Local Inventory Task
- BIOS Info Models
- Hotfix Info Models
- Video Controller Info
- Battery Info Models
- Group Info Models

## God Nodes (most connected - your core abstractions)
1. `DeviceContent` - 95 edges
2. `AgentOptions` - 59 edges
3. `MemoryInfo` - 55 edges
4. `Daraban.Agent.Core.Models` - 30 edges
5. `LocalWindowsCollector` - 25 edges
6. `DarabanClient` - 23 edges
7. `CollectJob` - 22 edges
8. `LocalLinuxCollector` - 20 edges
9. `LocalMacCollector` - 19 edges
10. `Daraban.Agent.Core.Collectors` - 18 edges

## Surprising Connections (you probably didn't know these)
- `AgentRunner` --references--> `IAgentTask`  [EXTRACTED]
  src/Daraban.Agent.Core/Agents/AgentRunner.cs → src/Daraban.Agent.Core/Agents/IAgentTask.cs
- `CollectTask` --implements--> `IAgentTask`  [EXTRACTED]
  src/Daraban.Agent.Core/Agents/CollectTask.cs → src/Daraban.Agent.Core/Agents/IAgentTask.cs
- `DeployTask` --implements--> `IAgentTask`  [EXTRACTED]
  src/Daraban.Agent.Core/Agents/DeployTask.cs → src/Daraban.Agent.Core/Agents/IAgentTask.cs
- `EsxInventoryTask` --implements--> `IAgentTask`  [EXTRACTED]
  src/Daraban.Agent.Core/Agents/EsxInventoryTask.cs → src/Daraban.Agent.Core/Agents/IAgentTask.cs
- `LocalInventoryTask` --implements--> `IAgentTask`  [EXTRACTED]
  src/Daraban.Agent.Core/Agents/LocalInventoryTask.cs → src/Daraban.Agent.Core/Agents/IAgentTask.cs

## Import Cycles
- None detected.

## Communities (39 total, 2 thin omitted)

### Community 0 - "Service Infrastructure"
Cohesion: 0.07
Nodes (30): BackgroundService, Command, Func, ILogger, IOptions, LastRunUtc, Message, Option (+22 more)

### Community 1 - "Memory & Hardware Info"
Cohesion: 0.04
Nodes (48): MemoryInfo, Attributes, BankLabel, Capacity, Caption, Class, ConfiguredClockSpeed, ConfiguredVoltage (+40 more)

### Community 2 - "HTTP Transport Layer"
Cohesion: 0.11
Nodes (20): ByteArrayContent, HttpMethod, HttpRequestMessage, CancellationToken, HttpClient, IEnumerable, IList, JsonSerializerOptions (+12 more)

### Community 3 - "Remote Inventory (SSH/WinRM)"
Cohesion: 0.07
Nodes (33): RemoteHostSpec, WinrmHttps, CancellationToken, Task, RemoteInventoryTask, Name, CancellationToken, Task (+25 more)

### Community 4 - "ESX/vCenter Inventory"
Cohesion: 0.07
Nodes (33): IAsyncDisposable, CancellationToken, List, Task, EsxInventoryTask, Name, CancellationToken, HttpClient (+25 more)

### Community 5 - "Namespace Organization"
Cohesion: 0.12
Nodes (10): Daraban.Agent.Core.Config, Daraban.Agent.Core.Tools, Daraban.Agent.Core.Http, Daraban.Agent.Service, Daraban.Agent.Core.Transport, Daraban.Agent.Cli, Daraban.Agent.Core.Models, Daraban.Agent.Core.Collectors (+2 more)

### Community 6 - "Deploy Task"
Cohesion: 0.07
Nodes (31): Output, CancellationToken, DeployJob, Task, DeployTask, Name, DateTime, List (+23 more)

### Community 7 - "Collect Task Implementation"
Cohesion: 0.09
Nodes (26): JsonConverter, PropertyData, CancellationToken, Task, CollectCollector, JsonSerializerOptions, CollectJob, Arguments (+18 more)

### Community 8 - "Agent Configuration"
Cohesion: 0.06
Nodes (33): List, AgentOptions, AgentId, ApiKey, DelayTimeSeconds, DeployWorkDir, DiscoveryThreads, EsxHost (+25 more)

### Community 9 - "Network Discovery Task"
Cohesion: 0.13
Nodes (18): CancellationToken, Dictionary, IEnumerable, List, Task, NetDiscoveryTask, Name, IPAddress (+10 more)

### Community 10 - "Device Content Models"
Cohesion: 0.07
Nodes (29): List, DeviceContent, AudioDevices, Batteries, Bios, ComputerName, ComputerSystem, Cpus (+21 more)

### Community 11 - "OS Detection & Linux Collector"
Cohesion: 0.18
Nodes (6): LocalCollectorFactory, LocalLinuxCollector, DeviceInventory, Action, Content, DeviceId

### Community 13 - "NuGet Dependencies"
Cohesion: 0.10
Nodes (17): coverlet.collector (6.0.4), Lextm.SharpSnmpLib (12.5.7), Microsoft.Extensions.Hosting (10.0.9), Microsoft.NET.Test.Sdk (17.14.1), SSH.NET (2025.1.0), System.CommandLine (2.0.9), System.Management (10.0.9), xunit (2.9.3) (+9 more)

### Community 14 - "Wake-On-LAN Task"
Cohesion: 0.13
Nodes (14): CancellationToken, List, Task, WakeOnLanTask, Name, WakeOnLanSender, WakeOnLanResult, Error (+6 more)

### Community 15 - "SNMP Network Collector"
Cohesion: 0.21
Nodes (14): IPEndPoint, CancellationToken, List, Task, SnmpFingerprint, SysDescr, SysName, SysObjectId (+6 more)

### Community 17 - "Audio Device Models"
Cohesion: 0.11
Nodes (18): AudioDevice, Name, Status, Derivation, CimChip, CimPhysicalComponent, CimPhysicalElement, CimPhysicalMemory (+10 more)

### Community 18 - "Collect Result Models"
Cohesion: 0.13
Nodes (13): DateTime, CollectResult, CollectedAt, Error, JobId, Success, Value, DateTime (+5 more)

### Community 19 - "Net Inventory Task"
Cohesion: 0.20
Nodes (11): CancellationToken, List, Task, NetInventoryTask, Name, NetworkDeviceInventory, Community, Error (+3 more)

### Community 20 - "Agent Task Interface"
Cohesion: 0.22
Nodes (7): IReadOnlyList, CancellationToken, Task, IAgentTask, Name, TaskRegistry, All

### Community 21 - "Collect Task Orchestration"
Cohesion: 0.38
Nodes (6): CancellationToken, List, Task, CollectTask, Name, DarabanClientFactory

### Community 22 - "Service Launch Settings"
Cohesion: 0.25
Nodes (7): commandName, dotnetRunMessages, environmentVariables, DOTNET_ENVIRONMENT, profiles, Daraban.Agent.Service, $schema

### Community 23 - "OAuth Token Provider"
Cohesion: 0.29
Nodes (7): DateTimeOffset, SemaphoreSlim, HttpClient, OAuthTokenProvider, TokenResponse, AccessToken, ExpiresIn

### Community 24 - "Printer Info Models"
Cohesion: 0.29
Nodes (7): PrinterInfo, Default, DriverName, Name, PortName, PrinterStatus, Shared

### Community 25 - "Remote Entry Models"
Cohesion: 0.29
Nodes (6): DateTime, RemoteEntry, DeviceId, NextRunUtc, TargetAlias, Url

### Community 26 - "Computer System Info"
Cohesion: 0.33
Nodes (6): ComputerSystemInfo, Manufacturer, Model, PCSystemType, SystemType, TotalPhysicalMemory

### Community 27 - "Desktop Info Models"
Cohesion: 0.33
Nodes (6): DesktopInfo, Name, ScreenSaverActive, ScreenSaverSecure, ScreenSaverTimeout, Wallpaper

### Community 28 - "Process Info Models"
Cohesion: 0.33
Nodes (6): ProcessInfo, CommandLine, Name, ProcessId, ThreadCount, WorkingSetSize

### Community 29 - "User Account Info"
Cohesion: 0.33
Nodes (6): UserAccountInfo, Disabled, FullName, Lockout, Name, SID

### Community 30 - "Unit Tests"
Cohesion: 0.40
Nodes (3): Daraban.Agent.Tests, Fact, UnitTest1

### Community 31 - "Local Inventory Task"
Cohesion: 0.40
Nodes (4): CancellationToken, Task, LocalInventoryTask, Name

### Community 32 - "BIOS Info Models"
Cohesion: 0.40
Nodes (5): BiosInfo, Manufacturer, ReleaseDate, SerialNumber, Version

### Community 33 - "Hotfix Info Models"
Cohesion: 0.40
Nodes (5): HotfixInfo, Description, HotFixID, InstalledBy, InstalledOn

### Community 34 - "Video Controller Info"
Cohesion: 0.40
Nodes (5): VideoControllerInfo, AdapterRAM, DriverVersion, Name, VideoProcessor

### Community 35 - "Battery Info Models"
Cohesion: 0.50
Nodes (4): BatteryInfo, BatteryStatus, EstimatedChargeRemaining, Name

### Community 36 - "Group Info Models"
Cohesion: 0.50
Nodes (4): GroupInfo, Description, Name, SID

## Knowledge Gaps
- **304 isolated node(s):** `net10.0`, `Microsoft.NET.Sdk`, `Daraban.Agent.Cli`, `Name`, `Name` (+299 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **2 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Daraban.Agent.Core.Models` connect `Namespace Organization` to `ESX/vCenter Inventory`, `Deploy Task`, `Collect Task Implementation`, `Network Discovery Task`, `Wake-On-LAN Task`, `Audio Device Models`, `Collect Result Models`, `Net Inventory Task`, `Remote Entry Models`?**
  _High betweenness centrality (0.304) - this node is a cross-community bridge._
- **Why does `AgentOptions` connect `Agent Configuration` to `Service Infrastructure`, `HTTP Transport Layer`, `Remote Inventory (SSH/WinRM)`, `ESX/vCenter Inventory`, `Deploy Task`, `Network Discovery Task`, `Wake-On-LAN Task`, `Net Inventory Task`, `Agent Task Interface`, `Collect Task Orchestration`, `OAuth Token Provider`, `Local Inventory Task`?**
  _High betweenness centrality (0.227) - this node is a cross-community bridge._
- **Why does `DeviceContent` connect `Device Content Models` to `BIOS Info Models`, `Hotfix Info Models`, `Memory & Hardware Info`, `Remote Inventory (SSH/WinRM)`, `Battery Info Models`, `Group Info Models`, `Video Controller Info`, `OS Detection & Linux Collector`, `Windows WMI Collector`, `SNMP Network Collector`, `macOS Collector`, `Audio Device Models`, `Printer Info Models`, `Computer System Info`, `Desktop Info Models`, `Process Info Models`, `User Account Info`?**
  _High betweenness centrality (0.207) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `DeviceContent` (e.g. with `.CollectAsync()` and `.CollectAsync()`) actually correct?**
  _`DeviceContent` has 2 INFERRED edges - model-reasoned connections that need verification._
- **Are the 5 inferred relationships involving `MemoryInfo` (e.g. with `.CollectMemory()` and `.CollectMemory()`) actually correct?**
  _`MemoryInfo` has 5 INFERRED edges - model-reasoned connections that need verification._
- **What connects `net10.0`, `Microsoft.NET.Sdk`, `Daraban.Agent.Cli` to the rest of the system?**
  _304 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Service Infrastructure` be split into smaller, more focused modules?**
  _Cohesion score 0.070578231292517 - nodes in this community are weakly interconnected._