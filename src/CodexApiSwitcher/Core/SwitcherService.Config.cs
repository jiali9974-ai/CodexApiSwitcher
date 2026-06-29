using System.Text;
using System.Text.RegularExpressions;

namespace CodexApiSwitcher.Core;

internal sealed partial class SwitcherService
{
    private List<string> ReadConfig()
    {
        AssertConfig();
        return File.ReadAllLines(configPath, Encoding.UTF8).ToList();
    }

    private void AssertConfig()
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Codex root does not exist: " + root);
        if (!File.Exists(configPath)) throw new FileNotFoundException("config.toml was not found in the selected Codex root.", configPath);
    }

    private string BackupConfig()
    {
        Directory.CreateDirectory(backupDirectory);
        var destination = Path.Combine(backupDirectory, "config.toml." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak");
        File.Copy(configPath, destination, false);
        return destination;
    }

    private void WriteConfigAtomically(List<string> lines) => AtomicFile.WriteText(configPath, lines);

    private static string GetTopLevelValue(List<string> lines, string key)
    {
        var expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=\s*""([^""]*)""\s*$", RegexOptions.CultureInvariant);
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\s*\[")) break;
            var match = expression.Match(line);
            if (match.Success) return match.Groups[1].Value;
        }
        return string.Empty;
    }

    private static string GetSectionValue(List<string> lines, string section, string key)
    {
        var inside = false;
        var keyExpression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=\s*(.+?)\s*$", RegexOptions.CultureInvariant);
        foreach (var line in lines)
        {
            var sectionMatch = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
            if (sectionMatch.Success)
            {
                inside = sectionMatch.Groups[1].Value == section;
                continue;
            }
            if (inside && keyExpression.Match(line) is { Success: true } keyMatch) return keyMatch.Groups[1].Value.Trim().Trim('"');
        }
        return string.Empty;
    }

    private static bool SectionExists(List<string> lines, string section) => lines.Any(line =>
    {
        var match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
        return match.Success && match.Groups[1].Value == section;
    });

    private static void SetTopLevelValue(List<string> lines, string key, string value) =>
        SetTopLevelLiteral(lines, key, "\"" + EscapeToml(value) + "\"");

    private static void SetTopLevelBoolean(List<string> lines, string key, bool value) =>
        SetTopLevelLiteral(lines, key, value ? "true" : "false");

    private static void SetTopLevelLiteral(List<string> lines, string key, string literal)
    {
        var firstSection = lines.FindIndex(line => Regex.IsMatch(line, @"^\s*\["));
        if (firstSection < 0) firstSection = lines.Count;
        var expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
        var replacement = key + " = " + literal;
        var found = false;
        for (var index = 0; index < firstSection; index++)
        {
            if (!expression.IsMatch(lines[index])) continue;
            if (!found)
            {
                lines[index] = replacement;
                found = true;
            }
            else
            {
                lines.RemoveAt(index--);
                firstSection--;
            }
        }
        if (!found) lines.Insert(firstSection, replacement);
    }

    private static bool TryGetTopLevelLine(List<string> lines, string key, out int index, out string line)
    {
        var firstSection = lines.FindIndex(candidate => Regex.IsMatch(candidate, @"^\s*\["));
        if (firstSection < 0) firstSection = lines.Count;
        var expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
        for (var current = 0; current < firstSection; current++)
        {
            if (!expression.IsMatch(lines[current])) continue;
            index = current;
            line = lines[current];
            return true;
        }
        index = -1;
        line = string.Empty;
        return false;
    }

    private static void RemoveTopLevelValue(List<string> lines, string key)
    {
        while (TryGetTopLevelLine(lines, key, out var index, out _)) lines.RemoveAt(index);
    }

    private static bool TryGetSectionKeyLine(List<string> lines, string section, string key, out int index, out string line)
    {
        var inside = false;
        var expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
        for (var current = 0; current < lines.Count; current++)
        {
            var sectionMatch = Regex.Match(lines[current], @"^\s*\[([^\]]+)\]\s*$");
            if (sectionMatch.Success)
            {
                inside = sectionMatch.Groups[1].Value == section;
                continue;
            }
            if (!inside || !expression.IsMatch(lines[current])) continue;
            index = current;
            line = lines[current];
            return true;
        }
        index = -1;
        line = string.Empty;
        return false;
    }

    private static void RemoveSectionKey(List<string> lines, string section, string key)
    {
        while (TryGetSectionKeyLine(lines, section, key, out var index, out _)) lines.RemoveAt(index);
    }

    private static void SetSectionBoolean(List<string> lines, string section, string key, bool value) =>
        SetSectionLiteral(lines, section, key, value ? "true" : "false");

    private static void SetSectionLiteral(List<string> lines, string section, string key, string literal)
    {
        EnsureSectionExists(lines, section);
        var sectionIndex = FindSectionIndex(lines, section);
        var nextSection = FindNextSectionIndex(lines, sectionIndex + 1);
        var expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
        var replacement = key + " = " + literal;
        var found = false;
        for (var index = sectionIndex + 1; index < nextSection; index++)
        {
            if (!expression.IsMatch(lines[index])) continue;
            if (!found)
            {
                lines[index] = replacement;
                found = true;
            }
            else
            {
                lines.RemoveAt(index--);
                nextSection--;
            }
        }
        if (!found) lines.Insert(nextSection, replacement);
    }

    private static int FindSectionIndex(List<string> lines, string section)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var match = Regex.Match(lines[index], @"^\s*\[([^\]]+)\]\s*$");
            if (match.Success && match.Groups[1].Value == section) return index;
        }
        return -1;
    }

    private static int FindNextSectionIndex(List<string> lines, int start)
    {
        for (var index = Math.Max(0, start); index < lines.Count; index++)
        {
            if (Regex.IsMatch(lines[index], @"^\s*\[")) return index;
        }
        return lines.Count;
    }

    private static void EnsureSectionExists(List<string> lines, string section)
    {
        if (FindSectionIndex(lines, section) >= 0) return;
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add(string.Empty);
        lines.Add("[" + section + "]");
    }

    private static void RestoreTopLevelLine(List<string> lines, string key, string originalLine)
    {
        RemoveTopLevelValue(lines, key);
        var firstSection = lines.FindIndex(candidate => Regex.IsMatch(candidate, @"^\s*\["));
        if (firstSection < 0) firstSection = lines.Count;
        lines.Insert(firstSection, originalLine);
    }

    private static void RestoreSectionLine(List<string> lines, string section, string key, string originalLine)
    {
        RemoveSectionKey(lines, section, key);
        EnsureSectionExists(lines, section);
        var sectionIndex = FindSectionIndex(lines, section);
        lines.Insert(FindNextSectionIndex(lines, sectionIndex + 1), originalLine);
    }

    private string ApplyThirdPartyCompatibilityMode(List<string> lines)
    {
        if (!File.Exists(compatibilityBackupPath)) SaveCompatibilityBackup(CaptureCompatibilityBackup(lines));
        SetTopLevelLiteral(lines, "web_search", "\"disabled\"");
        SetTopLevelBoolean(lines, "tools_view_image", false);
        foreach (var key in new[] { "image_generation", "imagegen", "imagegenext", "browser_use", "computer_use", "in_app_browser" })
        {
            SetSectionBoolean(lines, "features", key, false);
        }
        SetSectionBoolean(lines, "tools", "view_image", false);
        foreach (var section in GetCompatibilityPluginSections())
        {
            if (SectionExists(lines, section)) SetSectionBoolean(lines, section, "enabled", false);
        }
        return "已启用第三方兼容模式：关闭 Codex 生图、图片查看、Web 搜索，并临时禁用浏览器/文档/表格/PPT/PDF 插件，减少中转站误报 Image generation 权限错误。";
    }

    private string RestoreThirdPartyCompatibilityMode(List<string> lines)
    {
        if (!File.Exists(compatibilityBackupPath)) return string.Empty;
        foreach (var entry in LoadCompatibilityBackup())
        {
            if (entry.Scope == "top")
            {
                if (entry.Existed) RestoreTopLevelLine(lines, entry.Key, entry.Line);
                else RemoveTopLevelValue(lines, entry.Key);
            }
            else if (entry.Scope == "section")
            {
                if (entry.Existed) RestoreSectionLine(lines, entry.Section, entry.Key, entry.Line);
                else RemoveSectionKey(lines, entry.Section, entry.Key);
            }
        }
        return "已恢复第三方兼容模式之前的 Codex 工具和插件配置。";
    }

    private static string[] GetCompatibilityPluginSections() =>
    [
        "plugins.\"browser-use@openai-bundled\"",
        "plugins.\"documents@openai-primary-runtime\"",
        "plugins.\"spreadsheets@openai-primary-runtime\"",
        "plugins.\"presentations@openai-primary-runtime\"",
        "plugins.\"pdf@openai-primary-runtime\""
    ];

    private static List<CompatibilityBackupEntry> CaptureCompatibilityBackup(List<string> lines)
    {
        var entries = new List<CompatibilityBackupEntry>();
        AddTopLevelCompatibilityBackup(entries, lines, "web_search");
        AddTopLevelCompatibilityBackup(entries, lines, "tools_view_image");
        foreach (var key in new[] { "image_generation", "imagegen", "imagegenext", "browser_use", "computer_use", "in_app_browser" })
        {
            AddSectionCompatibilityBackup(entries, lines, "features", key);
        }
        AddSectionCompatibilityBackup(entries, lines, "tools", "view_image");
        foreach (var section in GetCompatibilityPluginSections()) AddSectionCompatibilityBackup(entries, lines, section, "enabled");
        return entries;
    }

    private static void AddTopLevelCompatibilityBackup(List<CompatibilityBackupEntry> entries, List<string> lines, string key)
    {
        var existed = TryGetTopLevelLine(lines, key, out _, out var line);
        entries.Add(new CompatibilityBackupEntry("top", string.Empty, key, existed, line));
    }

    private static void AddSectionCompatibilityBackup(List<CompatibilityBackupEntry> entries, List<string> lines, string section, string key)
    {
        var existed = TryGetSectionKeyLine(lines, section, key, out _, out var line);
        entries.Add(new CompatibilityBackupEntry("section", section, key, existed, line));
    }

    private void SaveCompatibilityBackup(IEnumerable<CompatibilityBackupEntry> entries)
    {
        var lines = new List<string> { "version=" + EncodeSetting("1") };
        lines.AddRange(entries.Select(entry => "entry=" + EncodeSetting(entry.Scope) + "|" + EncodeSetting(entry.Section) + "|" + EncodeSetting(entry.Key) + "|" + EncodeSetting(entry.Existed ? "1" : "0") + "|" + EncodeSetting(entry.Line)));
        AtomicFile.WriteText(compatibilityBackupPath, lines);
    }

    private List<CompatibilityBackupEntry> LoadCompatibilityBackup()
    {
        var entries = new List<CompatibilityBackupEntry>();
        foreach (var rawLine in File.ReadAllLines(compatibilityBackupPath, Encoding.UTF8))
        {
            if (!rawLine.StartsWith("entry=", StringComparison.Ordinal)) continue;
            var parts = rawLine[6..].Split('|');
            if (parts.Length != 5) continue;
            entries.Add(new CompatibilityBackupEntry(
                DecodeSetting(parts[0]), DecodeSetting(parts[1]), DecodeSetting(parts[2]), DecodeSetting(parts[3]) == "1", DecodeSetting(parts[4])));
        }
        return entries;
    }

    private void DeleteCompatibilityBackup()
    {
        try { File.Delete(compatibilityBackupPath); }
        catch { }
    }

    private void RemoveProviderSections(List<string> lines)
    {
        var result = new List<string>();
        var skip = false;
        foreach (var line in lines)
        {
            var match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
            if (match.Success)
            {
                var section = match.Groups[1].Value;
                skip = section == "model_providers." + ProviderId || section.StartsWith("model_providers." + ProviderId + ".", StringComparison.Ordinal);
            }
            if (!skip) result.Add(line);
        }
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1])) result.RemoveAt(result.Count - 1);
        lines.Clear();
        lines.AddRange(result);
    }

    private void AddProviderSections(List<string> lines, string url)
    {
        lines.Add(string.Empty);
        lines.Add("[model_providers." + ProviderId + "]");
        lines.Add("name = \"custom\"");
        lines.Add("wire_api = \"responses\"");
        lines.Add("base_url = \"" + EscapeToml(url) + "\"");
        lines.Add(string.Empty);
        lines.Add("[model_providers." + ProviderId + ".auth]");
        lines.Add("command = \"" + EscapeToml(stableHelperPath) + "\"");
        lines.Add("args = [\"--emit-token\", \"--root\", \"" + EscapeToml(root) + "\"]");
        lines.Add("timeout_ms = 5000");
        lines.Add("refresh_interval_ms = 0");
    }

    private static string EscapeToml(string? value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
