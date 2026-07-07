using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CodexApiSwitcher.Platform;

namespace CodexApiSwitcher.Core;

internal sealed partial class SwitcherService
{
    private const int SkillPackageVersion = 1;
    private const string SkillPackageManifestName = "manifest.json";

    internal List<SkillSummary> ListSkills(string query)
    {
        var skillsRoot = GetSkillsRoot();
        if (!Directory.Exists(skillsRoot)) return new List<SkillSummary>();
        var normalizedQuery = (query ?? string.Empty).Trim();
        var result = new List<SkillSummary>();
        foreach (var directory in Directory.GetDirectories(skillsRoot).OrderBy(path => path, PathComparer))
        {
            var id = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith(".", StringComparison.Ordinal))
            {
                if (!string.Equals(id, ".system", StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (string.Equals(id, ".system", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var systemSkill in Directory.GetDirectories(directory).OrderBy(path => path, PathComparer))
                {
                    var summary = CreateSkillSummary(systemSkill, true);
                    if (SkillMatchesQuery(summary, normalizedQuery)) result.Add(summary);
                }
                continue;
            }
            var item = CreateSkillSummary(directory, false);
            if (SkillMatchesQuery(item, normalizedQuery)) result.Add(item);
        }
        return result
            .OrderBy(item => item.IsSystem)
            .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal SkillExportResult ExportSkills(IEnumerable<string> ids, string outputPath)
    {
        var selectedIds = NormalizeSkillIds(ids);
        if (selectedIds.Count == 0) throw new InvalidOperationException("请选择要导出的 Skill。");
        var outputFullPath = Path.GetFullPath(outputPath);
        if (!outputFullPath.EndsWith(".casskills.zip", StringComparison.OrdinalIgnoreCase)) outputFullPath += ".casskills.zip";
        var skills = LoadSkillsById(selectedIds, allowMissing: true);
        var manifest = new SkillPackageManifest
        {
            Version = SkillPackageVersion,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            SourcePlatform = CodexPlatform.Description
        };
        var skippedSystem = 0;
        var skippedMissing = 0;
        var temporary = outputFullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath)!);
            if (File.Exists(temporary)) File.Delete(temporary);
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var id in selectedIds)
                {
                    if (!skills.TryGetValue(id, out var skill) || !Directory.Exists(skill.FullPath))
                    {
                        skippedMissing++;
                        continue;
                    }
                    if (skill.IsSystem)
                    {
                        skippedSystem++;
                        continue;
                    }
                    var packagePath = "skills/" + SanitizePackageSegment(skill.Id) + "/";
                    AddDirectoryToArchive(archive, skill.FullPath, packagePath);
                    manifest.Skills.Add(new SkillPackageEntry
                    {
                        Id = skill.Id,
                        Name = skill.Name,
                        Description = skill.Description,
                        RelativePath = skill.RelativePath,
                        PackagePath = packagePath,
                        Source = skill.Source,
                        UpdatedAtMs = skill.UpdatedAt == DateTimeOffset.MinValue ? 0 : skill.UpdatedAt.ToUnixTimeMilliseconds(),
                        SizeBytes = skill.SizeBytes
                    });
                }
                var manifestEntry = archive.CreateEntry(SkillPackageManifestName, CompressionLevel.Optimal);
                using var stream = manifestEntry.Open();
                System.Text.Json.JsonSerializer.Serialize(stream, manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            }
            File.Move(temporary, outputFullPath, true);
            return new SkillExportResult(selectedIds.Count, manifest.Skills.Count, skippedSystem, skippedMissing, outputFullPath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    internal SkillImportResult ImportSkills(string packagePath)
    {
        AssertCodexIsStopped();
        var packageFullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(packageFullPath)) throw new FileNotFoundException("Skill 包不存在。", packageFullPath);
        var skillsRoot = GetSkillsRoot();
        Directory.CreateDirectory(skillsRoot);
        using var archive = ZipFile.OpenRead(packageFullPath);
        var manifestEntry = archive.GetEntry(SkillPackageManifestName) ?? throw new InvalidOperationException("Skill 包缺少 manifest.json。");
        SkillPackageManifest manifest;
        using (var stream = manifestEntry.Open())
        {
            manifest = System.Text.Json.JsonSerializer.Deserialize<SkillPackageManifest>(stream) ?? throw new InvalidOperationException("manifest.json 无法读取。");
        }
        if (manifest.Version != SkillPackageVersion) throw new InvalidOperationException("不支持的 Skill 包版本：" + manifest.Version);
        var imported = 0;
        var skippedExisting = 0;
        var createdDirectories = new List<string>();
        try
        {
            foreach (var entry in manifest.Skills)
            {
                var id = NormalizeSkillId(entry.Id);
                if (id.Length == 0 || id.StartsWith(".", StringComparison.Ordinal)) continue;
                var destination = Path.Combine(skillsRoot, id);
                if (Directory.Exists(destination))
                {
                    skippedExisting++;
                    continue;
                }
                Directory.CreateDirectory(destination);
                createdDirectories.Add(destination);
                ExtractSkillDirectory(archive, ValidateSkillPackageDirectory(entry.PackagePath), destination);
                imported++;
            }
        }
        catch
        {
            foreach (var directory in createdDirectories)
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
            }
            throw;
        }
        return new SkillImportResult(imported, skippedExisting, packageFullPath);
    }

    internal SkillDeleteResult DeleteSkills(IEnumerable<string> ids)
    {
        AssertCodexIsStopped();
        var selectedIds = NormalizeSkillIds(ids);
        if (selectedIds.Count == 0) throw new InvalidOperationException("请选择要删除的 Skill。");
        var skills = LoadSkillsById(selectedIds, allowMissing: true);
        var deleted = 0;
        var skippedSystem = 0;
        var missing = 0;
        foreach (var id in selectedIds)
        {
            if (!skills.TryGetValue(id, out var skill) || !Directory.Exists(skill.FullPath))
            {
                missing++;
                continue;
            }
            if (skill.IsSystem)
            {
                skippedSystem++;
                continue;
            }
            if (!IsInsideRoot(skill.FullPath)) throw new InvalidOperationException("Skill 路径不在 Codex 根目录内：" + skill.FullPath);
            Directory.Delete(skill.FullPath, true);
            deleted++;
        }
        return new SkillDeleteResult(selectedIds.Count, deleted, skippedSystem, missing);
    }

    private string GetSkillsRoot() => Path.Combine(root, "skills");

    private SkillSummary CreateSkillSummary(string directory, bool isSystem)
    {
        var fullPath = Path.GetFullPath(directory);
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var id = isSystem ? ".system/" + Path.GetFileName(fullPath) : Path.GetFileName(fullPath);
        var (name, description) = ReadSkillMetadata(fullPath, id);
        var updatedAt = GetDirectoryUpdatedAt(fullPath);
        var size = GetDirectorySize(fullPath);
        return new SkillSummary(
            id,
            name,
            description,
            relative,
            fullPath,
            isSystem ? "系统内置" : "用户 Skill",
            isSystem,
            Directory.Exists(fullPath),
            updatedAt,
            size);
    }

    private static bool SkillMatchesQuery(SkillSummary summary, string query) =>
        query.Length == 0 ||
        summary.Id.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        summary.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        summary.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        summary.RelativePath.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private Dictionary<string, SkillSummary> LoadSkillsById(IReadOnlyCollection<string> ids, bool allowMissing)
    {
        var all = ListSkills(string.Empty).ToDictionary(skill => skill.Id, StringComparer.OrdinalIgnoreCase);
        var missing = ids.Where(id => !all.ContainsKey(id)).ToList();
        if (!allowMissing && missing.Count > 0) throw new InvalidOperationException("未找到 Skill：" + string.Join(", ", missing));
        return all;
    }

    private static List<string> NormalizeSkillIds(IEnumerable<string> ids) => ids
        .Select(NormalizeSkillId)
        .Where(id => id.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string NormalizeSkillId(string? id) => (id ?? string.Empty).Trim().Replace('\\', '/').Trim('/');

    private static (string Name, string Description) ReadSkillMetadata(string directory, string fallbackName)
    {
        var skillFile = Path.Combine(directory, "SKILL.md");
        if (!File.Exists(skillFile)) return (fallbackName, string.Empty);
        try
        {
            var lines = File.ReadLines(skillFile, Encoding.UTF8).Take(120).ToList();
            var title = lines.FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim();
            var frontmatterName = string.Empty;
            var description = string.Empty;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase) && frontmatterName.Length == 0)
                {
                    frontmatterName = line["name:".Length..].Trim().Trim('"', '\'');
                    continue;
                }
                if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase) && description.Length == 0)
                {
                    description = line["description:".Length..].Trim().Trim('"', '\'');
                    continue;
                }
                if (description.Length == 0 && line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal) && !line.Contains(':', StringComparison.Ordinal))
                {
                    description = line.Trim('"', '\'');
                }
            }
            var name = !string.IsNullOrWhiteSpace(frontmatterName) ? frontmatterName : title;
            return (string.IsNullOrWhiteSpace(name) ? fallbackName : name, description);
        }
        catch
        {
            return (fallbackName, string.Empty);
        }
    }

    private static DateTimeOffset GetDirectoryUpdatedAt(string directory)
    {
        try
        {
            var latest = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(File.GetLastWriteTimeUtc)
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(directory))
                .Max();
            return new DateTimeOffset(latest, TimeSpan.Zero);
        }
        catch { return DateTimeOffset.MinValue; }
    }

    private static long GetDirectorySize(string directory)
    {
        try { return Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length); }
        catch { return 0; }
    }

    private static void AddDirectoryToArchive(ZipArchive archive, string directory, string packagePrefix)
    {
        var rootFull = Path.GetFullPath(directory);
        foreach (var file in Directory.GetFiles(rootFull, "*", SearchOption.AllDirectories))
        {
            if (ShouldSkipSkillFile(file)) continue;
            var relative = Path.GetRelativePath(rootFull, file).Replace('\\', '/');
            if (!IsSafeRelativePath(relative)) continue;
            archive.CreateEntryFromFile(file, packagePrefix + relative, CompressionLevel.Optimal);
        }
    }

    private static bool ShouldSkipSkillFile(string file)
    {
        var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is ".git" or "node_modules" or ".DS_Store" or "__pycache__") ||
               file.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase);
    }

    private static string ValidateSkillPackageDirectory(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/').Trim('/');
        if (normalized.Length == 0 || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("../", StringComparison.Ordinal) || !normalized.StartsWith("skills/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Skill 包包含不安全路径：" + path);
        }
        return normalized + "/";
    }

    private static void ExtractSkillDirectory(ZipArchive archive, string packagePrefix, string destination)
    {
        var destinationFull = Path.GetFullPath(destination);
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.Replace('\\', '/').StartsWith(packagePrefix, StringComparison.Ordinal)))
        {
            var relative = entry.FullName.Replace('\\', '/')[packagePrefix.Length..];
            if (relative.Length == 0 || relative.EndsWith("/", StringComparison.Ordinal)) continue;
            if (!IsSafeRelativePath(relative)) throw new InvalidOperationException("Skill 包包含不安全文件路径：" + entry.FullName);
            var target = Path.GetFullPath(Path.Combine(destinationFull, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideDirectory(destinationFull, target)) throw new InvalidOperationException("Skill 包文件路径越界：" + entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = File.Create(target);
            input.CopyTo(output);
        }
    }

    private static bool IsSafeRelativePath(string relative)
    {
        var normalized = (relative ?? string.Empty).Replace('\\', '/');
        return normalized.Length > 0 &&
               !normalized.StartsWith("/", StringComparison.Ordinal) &&
               !normalized.Split('/').Any(segment => segment.Length == 0 || segment == "." || segment == "..");
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
