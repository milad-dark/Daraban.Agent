using Daraban.Agent.Core.Agents;
using Daraban.Agent.Core.Collectors;
using Daraban.Agent.Core.Config;
using Daraban.Agent.Core.Http;
using Daraban.Agent.Core.Models;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.Text.Json;

namespace Daraban.Agent.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Define options at root level
        var serverOpt = new Option<string?>("--server")
        {
            Description = "GLPI server base URL (e.g. https://glpi.example.com)"
        };

        var localOpt = new Option<string?>("--local")
        {
            Description = "Local directory to write results instead of sending to server"
        };

        var tasksOpt = new Option<string?>("--tasks")
        {
            Description = "Comma-separated list of tasks to run (local,netdiscovery,remote)"
        };

        var noTaskOpt = new Option<string?>("--no-task")
        {
            Description = "Comma-separated list of tasks to skip"
        };

        var httpPortOpt = new Option<int>("--http-port")
        {
            Description = "Agent HTTP interface port",
            DefaultValueFactory = _ => 62354
        };

        var noHttpdOpt = new Option<bool>("--no-httpd")
        {
            Description = "Disable the HTTP status interface",
            DefaultValueFactory = _ => false
        };
        // Options to control the collector
        var methodOpt = new Option<string>("--method")
        {
            Description = "Inventory method: local, ssh, snmp, winrm",
            Required = true,
            DefaultValueFactory = _ => "local"
        };
        var hostOpt = new Option<string>("--host")
        {
            Description = "Target host (for ssh, snmp, winrm)",
            DefaultValueFactory = _ => "192.168.1.1"
        };
        var userOpt = new Option<string>("--user")
        {
            Description = "Username (for ssh/winrm)",
            DefaultValueFactory = _ => "root"
        };
        var passOpt = new Option<string>("--password")
        {
            Description = "Password (for ssh/winrm)",
            DefaultValueFactory = _ => "password"
        };
        var fileOpt = new Option<string>("--file")
        {
            Description = "Output JSON file path",
            DefaultValueFactory = _ => "inventory.json"
        };

        var rootCommand = new RootCommand("Daraban Agent CLI")
        {
            serverOpt,
            localOpt,
            tasksOpt,
            noTaskOpt,
            httpPortOpt,
            noHttpdOpt,
            methodOpt,
            hostOpt,
            userOpt,
            passOpt,
            fileOpt
        };

        // Set action directly on root command
        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var method = parseResult.GetValue(methodOpt);

            // If method is specified, run inventory collection mode
            if (!string.IsNullOrWhiteSpace(method))
            {
                return await RunInventoryCollectionAsync(parseResult, methodOpt, hostOpt, userOpt, passOpt, fileOpt, cancellationToken);
            }

            // Otherwise, run as agent with tasks
            return await RunAgentTasksAsync(parseResult, serverOpt, localOpt, tasksOpt, noTaskOpt, httpPortOpt, noHttpdOpt, cancellationToken);
        });

        // Add netdiscovery and remote subcommands
        rootCommand.Subcommands.Add(CreateNetdiscoveryCommand());
        rootCommand.Subcommands.Add(CreateRemoteCommand());

        //return await rootCommand.Parse(args).InvokeAsync();

        var result = await rootCommand.Parse(args).InvokeAsync();

#if DEBUG
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
#endif

        return result;
    }

    static Command CreateNetdiscoveryCommand()
    {
        var cmd = new Command("netdiscovery", "Run network discovery (like glpi-netdiscovery)");

        cmd.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var options = new AgentOptions();
            options.Tasks.Add("netdiscovery");

            var task = TaskRegistry.Find("netdiscovery");
            if (task != null)
            {
                Console.WriteLine("[agent] Running task: netdiscovery");
                await task.RunAsync(options, cancellationToken);
            }
            else
            {
                Console.Error.WriteLine("Unknown task: netdiscovery");
                return 1;
            }

            return 0;
        });

        return cmd;
    }

    static Command CreateRemoteCommand()
    {
        var cmd = new Command("remote", "Run remote (agentless) inventory (like glpi-remote)");

        cmd.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var options = new AgentOptions();
            options.Tasks.Add("remote");

            var task = TaskRegistry.Find("remote");
            if (task != null)
            {
                Console.WriteLine("[agent] Running task: remote");
                await task.RunAsync(options, cancellationToken);
            }
            else
            {
                Console.Error.WriteLine("Unknown task: remote");
                return 1;
            }

            return 0;
        });

        return cmd;
    }

    static async Task<int> RunInventoryCollectionAsync(ParseResult parseResult, Option<string?> methodOpt, Option<string> hostOpt, Option<string> userOpt, Option<string> passOpt, Option<string> fileOpt, CancellationToken ct)
    {
        var method = parseResult.GetValue(methodOpt);
        var host = parseResult.GetValue(hostOpt);
        var user = parseResult.GetValue(userOpt);
        var password = parseResult.GetValue(passOpt);
        var file = parseResult.GetValue(fileOpt);

        DeviceInventory? inventory = null;

        Console.WriteLine($"[agent] Running {method} inventory...");

        try
        {
            switch (method?.ToLower())
            {
                case "local":
                    var localCollector = new LocalWindowsCollector();
                    inventory = localCollector.CollectLocal();
                    break;
                case "ssh":
                    var sshCollector = new SshRemoteCollector();
                    inventory = await sshCollector.CollectAsync(host, user, password, ct);
                    break;
                case "winrm":
                    var winrmCollector = new WinrmRemoteCollector(host, user, password);
                    inventory = await winrmCollector.CollectAsync(ct);
                    break;
                case "snmp":
                    var snmpCollector = new SnmpNetworkCollector();
                    inventory = await snmpCollector.DiscoverAsync(host, password, timeoutMs: 3000, ct: ct);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown method: {method}");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[agent] Error: {ex.Message}");
            return 1;
        }

        if (inventory != null)
        {
            // Pretty print the JSON to console for checking
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            string prettyJson = JsonSerializer.Serialize(inventory, jsonOptions);

            Console.WriteLine("--- INVENTORY JSON START ---");
            Console.WriteLine(prettyJson);
            Console.WriteLine("--- INVENTORY JSON END ---");

            // Save to file
            File.WriteAllText(file, prettyJson);
            Console.WriteLine($"\n[agent] Inventory successfully saved to: {Path.GetFullPath(file)}");
        }

        return 0;
    }

    static async Task<int> RunAgentTasksAsync(ParseResult parseResult, Option<string?> serverOpt, Option<string?> localOpt, Option<string?> tasksOpt, Option<string?> noTaskOpt, Option<int> httpPortOpt, Option<bool> noHttpdOpt, CancellationToken cancellationToken)
    {
        var options = new AgentOptions
        {
            Server = parseResult.GetValue(serverOpt),
            Local = parseResult.GetValue(localOpt),
            HttpPort = parseResult.GetValue(httpPortOpt),
            NoHttpd = parseResult.GetValue(noHttpdOpt),
        };

        // Parse --tasks and --no-task
        var tasksRaw = parseResult.GetValue(tasksOpt);
        if (!string.IsNullOrWhiteSpace(tasksRaw))
            options.Tasks.AddRange(tasksRaw.Split(',', StringSplitOptions.RemoveEmptyEntries));

        var noTaskRaw = parseResult.GetValue(noTaskOpt);
        if (!string.IsNullOrWhiteSpace(noTaskRaw))
            options.NoTasks.AddRange(noTaskRaw.Split(',', StringSplitOptions.RemoveEmptyEntries));

        // Determine effective task list
        var effective = TaskRegistry.All.Keys
            .Where(t => options.Tasks.Contains(t) || (options.Tasks.Count == 0 && t == "local"))
            .Where(t => !options.NoTasks.Contains(t))
            .ToList();

        // Start HTTP status endpoint (optional)
        if (!options.NoHttpd)
        {
            _ = RunStatusServerAsync(options.HttpPort, cancellationToken);
        }

        // Run tasks
        foreach (var taskName in effective)
        {
            var task = TaskRegistry.Find(taskName);
            if (task == null)
            {
                Console.Error.WriteLine($"Unknown task: {taskName}");
                continue;
            }
            Console.WriteLine($"[agent] Running task: {taskName}");
            await task.RunAsync(options, cancellationToken);
        }

        return 0;
    }

    static async Task RunStatusServerAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            var app = StatusEndpoint.BuildMinimalWeb(port);
            Console.WriteLine($"[agent] HTTP status interface listening on port {port} (/status).");
            await app.RunAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[agent] Failed to start HTTP interface: {ex.Message}");
        }
    }
}