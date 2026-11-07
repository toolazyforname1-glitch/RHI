using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RenoDXCommander.Cli.Commands;

/// <summary>
/// Minimal MCP (Model Context Protocol) server over stdio.
/// Each tool shells out to this same executable with --json, so the tool surface
/// can never drift from the CLI surface.
/// </summary>
internal static class McpCommand
{
    private const string ProtocolVersion = "2024-11-05";

    public static async Task<int> RunAsync(Args args)
    {
        if (args.Verb != "serve")
            return CliOutput.Fail("mcp", "UNKNOWN_VERB", "Usage: rhictl mcp serve", ExitCodes.Usage);

        await ServeAsync();
        return ExitCodes.Ok;
    }

    private static async Task ServeAsync()
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? request;
            try { request = JsonNode.Parse(line); }
            catch { await WriteErrorAsync(null, -32700, "parse error"); continue; }

            if (request is not JsonObject message) { await WriteErrorAsync(null, -32600, "invalid request"); continue; }

            var id = message["id"];
            var method = message["method"]?.GetValue<string>() ?? "";
            var parameters = message["params"] as JsonObject ?? new JsonObject();

            switch (method)
            {
                case "initialize":
                    await WriteResultAsync(id, new
                    {
                        protocolVersion = ProtocolVersion,
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "rhictl", version = "0.2.0" },
                    });
                    break;

                case "notifications/initialized":
                case "notifications/cancelled":
                    break;

                case "ping":
                    await WriteResultAsync(id, new { });
                    break;

                case "tools/list":
                    await WriteResultAsync(id, new { tools = ToolDefinitions() });
                    break;

                case "tools/call":
                    await HandleToolCallAsync(id, parameters);
                    break;

                case "shutdown":
                    await WriteResultAsync(id, null);
                    return;

                default:
                    await WriteErrorAsync(id, -32601, $"method not found: {method}");
                    break;
            }
        }
    }

    private static async Task HandleToolCallAsync(JsonNode? id, JsonObject parameters)
    {
        var name = parameters["name"]?.GetValue<string>() ?? "";
        var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();

        var argv = BuildArgv(name, arguments);
        if (argv == null)
        {
            await WriteResultAsync(id, ToolResult($"Unknown tool '{name}'.", isError: true));
            return;
        }

        var (exitCode, stdout, stderr) = await RunCliAsync(argv);
        var text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        await WriteResultAsync(id, ToolResult(text.Trim(), exitCode != 0));
    }

    private static List<string>? BuildArgv(string tool, JsonObject a)
    {
        var argv = new List<string>();
        var dry = Bool(a, "dryRun");
        if (dry) argv.Add("--dry-run");
        else if (Mutating(tool)) argv.Add("--yes");
        if (dry) { /* --yes intentionally omitted */ }

        void Add(string flag, string key)
        {
            var value = a[key]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(value)) { argv.Add(flag); argv.Add(value); }
        }

        void AddTarget()
        {
            if (a["path"]?.GetValue<string>() is { Length: > 0 } path) { argv.Add("--path"); argv.Add(path); }
            else Add("--game", "game");
        }

        switch (tool)
        {
            case "rhi_doctor": argv.AddRange(new[] { "doctor" }); break;
            case "rhi_library_list": argv.AddRange(new[] { "library", "list" }); break;
            case "rhi_status": argv.AddRange(new[] { "status" }); AddTarget(); break;

            case "rhi_game_add":
                argv.AddRange(new[] { "game", "add" });
                Add("--path", "path");
                Add("--name", "name");
                break;

            case "rhi_reshade":
                argv.AddRange(new[] { "reshade", a["action"]?.GetValue<string>() ?? "status" });
                AddTarget();
                Add("--channel", "channel");
                Add("--dll", "dll");
                Add("--packs", "packs");
                if (Bool(a, "noAddons")) argv.Add("--no-addons");
                break;

            case "rhi_renodx":
                argv.AddRange(new[] { "renodx", a["action"]?.GetValue<string>() ?? "install" });
                AddTarget();
                break;

            case "rhi_luma":
                argv.AddRange(new[] { "luma", a["action"]?.GetValue<string>() ?? "list" });
                AddTarget();
                break;

            case "rhi_dlss":
                argv.AddRange(new[] { "dlss", a["action"]?.GetValue<string>() ?? "status" });
                AddTarget();
                Add("--component", "component");
                Add("--version", "version");
                break;

            case "rhi_component":
            {
                var component = a["component"]?.GetValue<string>();
                var action = a["action"]?.GetValue<string>() ?? "install";
                if (string.IsNullOrWhiteSpace(component)) return null;
                argv.AddRange(new[] { component, action });
                AddTarget();
                Add("--dll", "dll");
                Add("--variant", "variant");
                Add("--gpu", "gpu");
                break;
            }

            case "rhi_run":
            {
                if (a["args"] is not JsonArray rawArgs) return null;
                foreach (var item in rawArgs)
                {
                    var value = item?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(value)) argv.Add(value);
                }
                break;
            }

            default:
                return null;
        }

        argv.Add("--json");
        return argv;
    }

    private static bool Mutating(string tool) => tool is not ("rhi_doctor" or "rhi_library_list" or "rhi_status" or "rhi_run");

    private static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(List<string> argv)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in argv) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(900_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (124, "", "tool call timed out after 15 minutes");
            }

            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return (7, "", ex.Message);
        }
    }

    private static object ToolResult(string text, bool isError) => new
    {
        content = new[] { new { type = "text", text } },
        isError,
    };

    private static object[] ToolDefinitions() => new object[]
    {
        Tool("rhi_doctor", "Check the RHI environment (7-Zip, state folder, RHI install, admin, GPU).", new { }),
        Tool("rhi_library_list", "List the saved game library.", new { }),
        Tool("rhi_status", "Installed components for one game.", new
        {
            game = Prop("string", "Game name from the library"),
            path = Prop("string", "Game folder (instead of game)"),
            dryRun = Prop("boolean", "Preview only"),
        }),
        Tool("rhi_game_add", "Add a manual game folder to the library.", new
        {
            path = Prop("string", "Game folder"),
            name = Prop("string", "Override the detected name"),
            dryRun = Prop("boolean", "Preview only"),
        }),
        Tool("rhi_reshade", "Install, update or uninstall ReShade for a game.", new
        {
            action = Prop("string", "install | update | uninstall | status", new[] { "install", "update", "uninstall", "status" }),
            game = Prop("string", "Game name"),
            path = Prop("string", "Game folder"),
            channel = Prop("string", "stable | nightly | custom | <version>"),
            dll = Prop("string", "dxgi.dll | d3d9.dll | d3d8.dll | opengl32.dll | custom"),
            packs = Prop("string", "Comma-separated shader pack ids (or 'off')"),
            noAddons = Prop("boolean", "Use the No-Addons ReShade build"),
            dryRun = Prop("boolean", "Preview only"),
        }),
        Tool("rhi_renodx", "Install, update or uninstall RenoDX for a game.", new
        {
            action = Prop("string", "install | update | uninstall", new[] { "install", "update", "uninstall" }),
            game = Prop("string", "Game name"),
            path = Prop("string", "Game folder"),
            dryRun = Prop("boolean", "Preview only"),
        }),
        Tool("rhi_luma", "List or install/uninstall Luma for a game.", new
        {
            action = Prop("string", "list | install | uninstall", new[] { "list", "install", "uninstall" }),
            game = Prop("string", "Game name"),
            path = Prop("string", "Game folder"),
            dryRun = Prop("boolean", "Preview only"),
        }),
        Tool("rhi_dlss", "DLSS / Streamline versions, swap or restore.", new
        {
            action = Prop("string", "status | versions | set | restore", new[] { "status", "versions", "set", "restore" }),
            game = Prop("string", "Game name"),
            path = Prop("string", "Game folder"),
            component = Prop("string", "sr | rr | fg | nr | streamline"),
            version = Prop("string", "Target version (for set)"),
            dryRun = Prop("boolean", "Preview only"),
        }),
        Tool("rhi_component", "Manage an extras component for a game.", new
        {
            component = Prop("string", "reframework | dofix | dxvk | optiscaler | asi | dlss-enabler | fg2030 | rtx-encore | mfg40 | dgvoodoo | relimiter | display-commander"),
            action = Prop("string", "install | uninstall | update", new[] { "install", "uninstall", "update" }),
            game = Prop("string", "Game name"),
            path = Prop("string", "Game folder"),
            dll = Prop("string", "Proxy DLL name"),
            variant = Prop("string", "Component variant"),
            gpu = Prop("string", "NVIDIA | AMD | Intel (OptiScaler)"),
            dryRun = Prop("boolean", "Preview only"),
        }),
        Tool("rhi_run", "Escape hatch: run any rhictl arguments.", new
        {
            args = new
            {
                type = "array",
                items = new { type = "string" },
                description = "Raw rhictl arguments, e.g. [\"status\",\"--game\",\"Cyberpunk 2077\"]",
            },
        }),
    };

    private static object Tool(string name, string description, object properties) => new
    {
        name,
        description,
        inputSchema = new
        {
            type = "object",
            properties,
            additionalProperties = true,
        },
    };

    private static object Prop(string type, string description, string[]? @enum = null) => @enum == null
        ? new { type, description }
        : new { type, description, @enum };

    private static async Task WriteResultAsync(JsonNode? id, object? result)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = result == null ? null : JsonSerializer.SerializeToNode(result),
        };
        await Console.Out.WriteLineAsync(payload.ToJsonString());
        await Console.Out.FlushAsync();
    }

    private static async Task WriteErrorAsync(JsonNode? id, int code, string message)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
        await Console.Out.WriteLineAsync(payload.ToJsonString());
        await Console.Out.FlushAsync();
    }
}
