namespace RenoDXCommander.Cli;

/// <summary>
/// Minimal argument parser: "group verb" followed by --flags.
/// Supports "--key value" and "--key=value". Boolean flags are listed in
/// <see cref="BoolFlags"/>; everything else expects a value.
/// </summary>
internal sealed class Args
{
    private static readonly HashSet<string> BoolFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "dry-run", "yes", "force", "verbose", "no-color", "help",
    };

    public string Group { get; private init; } = "";
    public string Verb { get; private init; } = "";
    public Dictionary<string, string> Options { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Positional { get; } = new();

    public bool Has(string flag) => Flags.Contains(flag);
    public bool Json => Has("json");
    public bool DryRun => Has("dry-run");
    public bool Yes => Has("yes");
    public bool Force => Has("force");

    public string? Get(string key) => Options.TryGetValue(key, out var v) ? v : null;
    public string Get(string key, string fallback) => Options.TryGetValue(key, out var v) ? v : fallback;
    public int? GetInt(string key) => int.TryParse(Get(key), out var v) ? v : null;

    public static Args Parse(string[] raw)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < raw.Length; i++)
        {
            var token = raw[i];
            if (token == "--")
            {
                for (var j = i + 1; j < raw.Length; j++) positionals.Add(raw[j]);
                break;
            }

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var body = token[2..];
                string key;
                string? inlineValue = null;
                var eq = body.IndexOf('=');
                if (eq >= 0) { key = body[..eq]; inlineValue = body[(eq + 1)..]; }
                else key = body;

                if (inlineValue != null) { options[key] = inlineValue; continue; }
                if (BoolFlags.Contains(key)) { flags.Add(key); continue; }
                if (i + 1 < raw.Length && !raw[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options[key] = raw[++i];
                    continue;
                }
                // Unknown flag with no value: treat as boolean so the user gets a clear error later.
                flags.Add(key);
                continue;
            }

            positionals.Add(token);
        }

        var args = new Args
        {
            Group = positionals.Count > 0 ? positionals[0].ToLowerInvariant() : "",
            Verb = positionals.Count > 1 ? positionals[1].ToLowerInvariant() : "",
        };
        args.Options = options;
        args.Flags = flags;
        foreach (var p in positionals.Skip(2)) args.Positional.Add(p);
        return args;
    }
}
