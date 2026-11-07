using System.Diagnostics;
using System.Text.Json;

namespace RenoDXCommander.Cli;

internal sealed class CliOutput
{
    public static bool Json { get; set; }
    public static bool Verbose { get; set; }
    public static readonly List<string> Warnings = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Warn(string message)
    {
        Warnings.Add(message);
        if (!Json) Console.Error.WriteLine($"warning: {message}");
    }

    public static void Detail(string message)
    {
        if (Verbose) Console.Error.WriteLine(message);
    }

    /// <summary>Runs human-readable output only when not in --json mode.</summary>
    public static void Human(Action render)
    {
        if (!Json) render();
    }

    public static void Table(string[] headers, IEnumerable<string[]> rows)
    {
        var materialised = rows.Select(r => r.ToArray()).ToList();
        var widths = new int[headers.Length];
        for (var i = 0; i < headers.Length; i++)
            widths[i] = Math.Max(headers[i].Length, materialised.Count == 0 ? 0 : materialised.Max(r => r[i]?.Length ?? 0));

        Console.WriteLine(string.Join("  ", headers.Select((h, i) => h.PadRight(widths[i]))));
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in materialised)
            Console.WriteLine(string.Join("  ", row.Select((c, i) => (c ?? "").PadRight(widths[i]))));
    }

    public static int Ok(string command, object? data)
    {
        if (Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = true,
                command,
                data,
                warnings = Warnings.ToArray(),
                elapsedMs = Clock.ElapsedMilliseconds,
            }, JsonOpts));
        }
        return 0;
    }

    public static int Fail(string command, string code, string message, int exitCode = ExitCodes.Failed)
    {
        if (Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = false,
                command,
                error = new { code, message },
                warnings = Warnings.ToArray(),
                elapsedMs = Clock.ElapsedMilliseconds,
            }, JsonOpts));
        }
        else
        {
            Console.Error.WriteLine($"error [{code}]: {message}");
        }
        return exitCode;
    }
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Usage = 2;
    public const int NotFound = 3;
    public const int GameRunning = 4;
    public const int Permission = 5;
    public const int Network = 6;
    public const int Failed = 7;
}
