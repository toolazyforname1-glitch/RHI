namespace RenoDXCommander.Cli;

/// <summary>
/// Small INI reader/writer that preserves the file's line endings and all
/// untouched lines. Used by the per-component `ini-get`/`ini-set` commands.
/// Every write keeps a `<file>.cli-backup` copy and is applied via a temp file.
/// </summary>
internal static class IniEditor
{
    public static string? Get(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        var keyIndex = FindKey(lines, section, key);
        return keyIndex < 0 ? null : ParseValue(lines[keyIndex]);
    }

    public static List<(string Section, string Key, string Value)> List(string path, string? sectionFilter = null)
    {
        var result = new List<(string, string, string)>();
        if (!File.Exists(path)) return result;

        var section = "";
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) continue;

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed[1..^1].Trim();
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;

            if (sectionFilter != null && !section.Equals(sectionFilter, StringComparison.OrdinalIgnoreCase)) continue;

            result.Add((section, trimmed[..eq].Trim(), trimmed[(eq + 1)..].Trim()));
        }

        return result;
    }

    public static List<string> ListSections(string path)
    {
        var sections = new List<string>();
        if (!File.Exists(path)) return sections;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']')) continue;
            var name = trimmed[1..^1].Trim();
            if (!sections.Contains(name, StringComparer.OrdinalIgnoreCase)) sections.Add(name);
        }

        return sections;
    }

    public static bool Set(string path, string section, string key, string value, out string? error)
    {
        error = null;
        try
        {
            var newline = DetectNewline(path);
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();

            var sectionIndex = FindSection(lines, section);
            if (sectionIndex < 0)
            {
                if (lines.Count > 0 && lines[^1].Trim().Length != 0) lines.Add("");
                lines.Add($"[{section}]");
                lines.Add($"{key}={value}");
            }
            else
            {
                var keyIndex = FindKey(lines, section, key);
                if (keyIndex >= 0)
                {
                    lines[keyIndex] = $"{key}={value}";
                }
                else
                {
                    var insertAt = sectionIndex + 1;
                    for (var i = sectionIndex + 1; i < lines.Count; i++)
                    {
                        var trimmed = lines[i].Trim();
                        if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) break;
                        if (trimmed.Length > 0) insertAt = i + 1;
                    }
                    lines.Insert(insertAt, $"{key}={value}");
                }
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            if (File.Exists(path))
                File.Copy(path, path + ".cli-backup", overwrite: true);

            var temp = path + ".cli.tmp";
            File.WriteAllText(temp, string.Join(newline, lines) + newline);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static int FindSection(List<string> lines, string section)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') &&
                trimmed[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static int FindKey(string[] lines, string section, string key)
    {
        var inSection = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                inSection = trimmed[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection) continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;
            if (trimmed[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static int FindKey(List<string> lines, string section, string key) =>
        FindKey(lines.ToArray(), section, key);

    private static string ParseValue(string line)
    {
        var eq = line.IndexOf('=');
        return eq < 0 ? "" : line[(eq + 1)..].Trim();
    }

    private static string DetectNewline(string path)
    {
        try
        {
            if (!File.Exists(path)) return Environment.NewLine;
            var content = File.ReadAllText(path);
            return content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        }
        catch
        {
            return Environment.NewLine;
        }
    }
}
