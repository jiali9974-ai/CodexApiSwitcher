using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexApiSwitcher.Platform;

namespace CodexApiSwitcher.Core;

internal sealed partial class SwitcherService
{
    private const int ConversationPackageVersion = 1;
    private const string ConversationPackageManifestName = "manifest.json";

    internal List<ConversationSummary> ListConversations(string query)
    {
        var statePaths = GetStateDatabasePaths();
        if (statePaths.Count == 0) return new List<ConversationSummary>();
        var normalizedQuery = (query ?? string.Empty).Trim();
        var byId = new Dictionary<string, ConversationSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var statePath in statePaths)
        {
            using var database = SqliteDatabase.Open(statePath);
            EnsureThreadColumns(database);
            var rows = database.QueryRows("select * from threads where first_user_message <> '' and source in ('vscode','cli')");
            foreach (var row in rows)
            {
                var summary = CreateConversationSummary(row, statePath);
                if (normalizedQuery.Length > 0 &&
                    !summary.Title.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase) &&
                    !summary.FirstUserMessage.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }
                if (!byId.TryGetValue(summary.Id, out var existing) || summary.UpdatedAtMs > existing.UpdatedAtMs)
                {
                    byId[summary.Id] = summary;
                }
            }
        }
        return byId.Values
            .OrderByDescending(item => item.UpdatedAtMs > 0 ? item.UpdatedAtMs : item.UpdatedAtUnix * 1000)
            .ThenBy(item => item.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal ConversationExportResult ExportConversations(IEnumerable<string> ids, string outputPath)
    {
        AssertConfig();
        var selectedIds = NormalizeConversationIds(ids);
        if (selectedIds.Count == 0) throw new InvalidOperationException("请选择要导出的对话。");
        var outputFullPath = Path.GetFullPath(outputPath);
        if (!outputFullPath.EndsWith(".casconv.zip", StringComparison.OrdinalIgnoreCase)) outputFullPath += ".casconv.zip";
        var conversations = LoadConversationRowsById(selectedIds);
        var manifest = new ConversationPackageManifest
        {
            Version = ConversationPackageVersion,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            SourcePlatform = CodexPlatform.Description
        };
        var skippedMissing = 0;
        var temporary = outputFullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath)!);
            if (File.Exists(temporary)) File.Delete(temporary);
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var (summary, row) in conversations)
                {
                    if (!summary.RolloutExists)
                    {
                        skippedMissing++;
                        continue;
                    }
                    var packagePath = GetConversationPackageJsonlPath(summary);
                    archive.CreateEntryFromFile(summary.RolloutPath, packagePath, CompressionLevel.Optimal);
                    manifest.Conversations.Add(new ConversationPackageEntry
                    {
                        Id = summary.Id,
                        Title = summary.Title,
                        FirstUserMessage = summary.FirstUserMessage,
                        Preview = summary.Preview,
                        Model = summary.Model,
                        ModelProvider = summary.ModelProvider,
                        Source = summary.Source,
                        OriginalRolloutPath = summary.RolloutPath,
                        PackagePath = packagePath,
                        UpdatedAt = summary.UpdatedAtUnix,
                        UpdatedAtMs = summary.UpdatedAtMs,
                        ThreadValues = row.ToDictionary(pair => pair.Key, pair => ToManifestString(pair.Value), StringComparer.OrdinalIgnoreCase)
                    });
                }
                var manifestEntry = archive.CreateEntry(ConversationPackageManifestName, CompressionLevel.Optimal);
                using var stream = manifestEntry.Open();
                JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
            }
            File.Move(temporary, outputFullPath, true);
            return new ConversationExportResult(selectedIds.Count, manifest.Conversations.Count, skippedMissing, outputFullPath);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    internal ConversationImportResult ImportConversations(string packagePath)
    {
        AssertConfig();
        AssertCodexIsStopped();
        var packageFullPath = Path.GetFullPath(packagePath);
        if (!File.Exists(packageFullPath)) throw new FileNotFoundException("对话包不存在。", packageFullPath);
        var statePaths = GetStateDatabasePaths();
        if (statePaths.Count == 0) throw new FileNotFoundException("state_5.sqlite was not found in the Codex root or sqlite subdirectory. 请先启动一次 Codex，让它初始化历史数据库。");
        var statePath = GetPreferredStateDatabasePath(statePaths);
        using var archive = ZipFile.OpenRead(packageFullPath);
        var targetStatus = GetStatus();
        var targetProvider = targetStatus.IsThirdParty ? ProviderId : "openai";
        var manifestEntry = archive.GetEntry(ConversationPackageManifestName) ?? throw new InvalidOperationException("对话包缺少 manifest.json。");
        ConversationPackageManifest manifest;
        using (var stream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<ConversationPackageManifest>(stream) ?? throw new InvalidOperationException("manifest.json 无法读取。");
        }
        if (manifest.Version != ConversationPackageVersion) throw new InvalidOperationException("不支持的对话包版本：" + manifest.Version);

        using var database = SqliteDatabase.Open(statePath);
        EnsureThreadColumns(database);
        var columns = database.GetTableColumns("threads");
        var imported = 0;
        var skippedExisting = 0;
        var copiedFiles = new List<string>();
        var sessionIndexPath = Path.Combine(root, "session_index.jsonl");
        var originalSessionIndex = File.Exists(sessionIndexPath) ? File.ReadAllBytes(sessionIndexPath) : null;
        database.Execute("begin immediate");
        try
        {
            foreach (var entry in manifest.Conversations)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)) continue;
                if (database.ScalarInt("select count(*) from threads where id = @id", new Dictionary<string, object?> { ["id"] = entry.Id }) > 0)
                {
                    skippedExisting++;
                    continue;
                }
                var packageEntry = archive.GetEntry(ValidatePackageEntryPath(entry.PackagePath)) ?? throw new InvalidOperationException("对话包缺少会话文件：" + entry.PackagePath);
                var destinationPath = GetImportDestinationPath(entry, packageEntry);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                var alreadyHadSameFile = File.Exists(destinationPath) && PackageEntryMatchesFile(packageEntry, destinationPath);
                if (!alreadyHadSameFile)
                {
                    using (var input = packageEntry.Open())
                    WriteImportedConversationFile(input, destinationPath);
                    if (!copiedFiles.Contains(destinationPath, PathComparer)) copiedFiles.Add(destinationPath);
                }
                RewriteSessionMeta(destinationPath, targetProvider);
                InsertConversationRow(database, columns, entry, destinationPath, targetProvider, targetStatus.Model);
                UpsertSessionIndex(entry);
                imported++;
            }
            database.EnsureIntegrity();
            database.Execute("commit");
        }
        catch
        {
            try { database.Execute("rollback"); } catch { }
            foreach (var copiedFile in copiedFiles)
            {
                try { if (File.Exists(copiedFile)) File.Delete(copiedFile); } catch { }
            }
            try
            {
                if (originalSessionIndex is null) File.Delete(sessionIndexPath);
                else AtomicFile.WriteBytes(sessionIndexPath, originalSessionIndex);
            }
            catch { }
            throw;
        }
        return new ConversationImportResult(imported, skippedExisting, packageFullPath);
    }

    internal ConversationDeleteResult DeleteConversations(IEnumerable<string> ids)
    {
        AssertConfig();
        AssertCodexIsStopped();
        var selectedIds = NormalizeConversationIds(ids);
        if (selectedIds.Count == 0) throw new InvalidOperationException("请选择要删除的对话。");
        var summaries = LoadConversationRowsById(selectedIds).Select(item => item.Summary).ToList();
        var rolloutPaths = summaries.Select(item => item.RolloutPath).Where(path => path.Length > 0).Distinct(PathComparer).ToList();
        var deletedRows = 0;
        foreach (var statePath in GetStateDatabasePaths())
        {
            using var database = SqliteDatabase.Open(statePath);
            database.Execute("begin immediate");
            try
            {
                foreach (var id in selectedIds)
                {
                    DeleteRelatedConversationRows(database, id);
                    deletedRows += database.Execute("delete from threads where id = @id", new Dictionary<string, object?> { ["id"] = id });
                }
                database.EnsureIntegrity();
                database.Execute("commit");
            }
            catch
            {
                try { database.Execute("rollback"); } catch { }
                throw;
            }
        }

        var deletedFiles = 0;
        foreach (var path in rolloutPaths)
        {
            try
            {
                if (IsInsideRoot(path) && File.Exists(path))
                {
                    File.Delete(path);
                    deletedFiles++;
                }
            }
            catch { }
        }
        var removedIndex = RemoveSessionIndexEntries(selectedIds);
        return new ConversationDeleteResult(selectedIds.Count, deletedRows, deletedFiles, removedIndex);
    }

    private string GetPreferredStateDatabasePath(List<string> statePaths)
    {
        var sqlitePath = Path.Combine(root, "sqlite", "state_5.sqlite");
        return statePaths.FirstOrDefault(path => PathEquals(path, sqlitePath)) ?? statePaths[0];
    }

    private List<(ConversationSummary Summary, Dictionary<string, object?> Row)> LoadConversationRowsById(IReadOnlyCollection<string> ids)
    {
        var found = new Dictionary<string, (ConversationSummary Summary, Dictionary<string, object?> Row)>(StringComparer.OrdinalIgnoreCase);
        foreach (var statePath in GetStateDatabasePaths())
        {
            using var database = SqliteDatabase.Open(statePath);
            EnsureThreadColumns(database);
            foreach (var id in ids)
            {
                if (found.ContainsKey(id)) continue;
                var rows = database.QueryRows("select * from threads where id = @id", new Dictionary<string, object?> { ["id"] = id });
                if (rows.Count == 0) continue;
                found[id] = (CreateConversationSummary(rows[0], statePath), rows[0]);
            }
        }
        var missing = ids.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException("未找到对话：" + string.Join(", ", missing));
        return ids.Select(id => found[id]).ToList();
    }

    private ConversationSummary CreateConversationSummary(Dictionary<string, object?> row, string statePath)
    {
        var id = GetRowString(row, "id");
        var rollout = GetRowString(row, "rollout_path");
        var resolved = string.Empty;
        var exists = false;
        if (!string.IsNullOrWhiteSpace(rollout))
        {
            try
            {
                resolved = ResolveConversationJsonlPath(rollout);
                exists = File.Exists(resolved);
            }
            catch
            {
                resolved = Path.GetFullPath(RemoveExtendedPathPrefix(rollout));
                exists = File.Exists(resolved);
            }
        }
        var updatedAt = GetRowLong(row, "updated_at");
        var updatedAtMs = GetRowLong(row, "updated_at_ms");
        if (updatedAtMs <= 0 && updatedAt > 0) updatedAtMs = updatedAt * 1000;
        var timestamp = updatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(updatedAtMs) : DateTimeOffset.MinValue;
        return new ConversationSummary(
            id,
            GetRowString(row, "title"),
            GetRowString(row, "first_user_message"),
            GetRowString(row, "preview"),
            GetRowString(row, "model"),
            GetRowString(row, "model_provider"),
            GetRowString(row, "source"),
            resolved,
            exists,
            timestamp,
            updatedAt,
            updatedAtMs,
            statePath);
    }

    private static List<string> NormalizeConversationIds(IEnumerable<string> ids) => ids
        .Select(id => (id ?? string.Empty).Trim())
        .Where(id => id.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string GetConversationPackageJsonlPath(ConversationSummary summary)
    {
        var safeDate = summary.UpdatedAt == DateTimeOffset.MinValue ? "unknown" : summary.UpdatedAt.ToString("yyyy/MM/dd");
        return "sessions/" + safeDate + "/" + SanitizePackageSegment(summary.Id) + "-" + SanitizePackageSegment(Path.GetFileName(summary.RolloutPath));
    }

    private string GetImportDestinationPath(ConversationPackageEntry entry, ZipArchiveEntry packageEntry)
    {
        var fileName = SanitizeFileName(Path.GetFileName(entry.PackagePath));
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "rollout-" + entry.Id + ".jsonl";
        var date = entry.UpdatedAtMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(entry.UpdatedAtMs)
            : entry.UpdatedAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(entry.UpdatedAt) : DateTimeOffset.Now;
        var directory = Path.Combine(root, "sessions", date.ToString("yyyy"), date.ToString("MM"), date.ToString("dd"));
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate)) return candidate;
        using var input = packageEntry.Open();
        var incomingHash = ComputeHash(input);
        if (string.Equals(ComputeHash(candidate), incomingHash, StringComparison.OrdinalIgnoreCase)) return candidate;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return Path.Combine(directory, stem + "-imported-" + entry.Id[..Math.Min(8, entry.Id.Length)] + extension);
    }

    private void InsertConversationRow(SqliteDatabase database, List<string> columns, ConversationPackageEntry entry, string rolloutPath, string provider, string fallbackModel)
    {
        var now = DateTimeOffset.UtcNow;
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (entry.ThreadValues.TryGetValue(column, out var value)) values[column] = value;
        }
        values["id"] = entry.Id;
        values["rollout_path"] = rolloutPath;
        values["source"] = string.IsNullOrWhiteSpace(entry.Source) ? "vscode" : entry.Source;
        values["first_user_message"] = entry.FirstUserMessage;
        values["model_provider"] = provider;
        values["has_user_event"] = 1;
        values["title"] = entry.Title;
        values["preview"] = string.IsNullOrWhiteSpace(entry.Preview) ? entry.FirstUserMessage : entry.Preview;
        values["model"] = string.IsNullOrWhiteSpace(entry.Model) ? fallbackModel : entry.Model;
        values["cwd"] = NormalizeImportedCwd(values.TryGetValue("cwd", out var cwd) ? Convert.ToString(cwd) ?? string.Empty : string.Empty);
        if (columns.Contains("updated_at", StringComparer.OrdinalIgnoreCase) && !values.ContainsKey("updated_at")) values["updated_at"] = now.ToUnixTimeSeconds();
        if (columns.Contains("created_at", StringComparer.OrdinalIgnoreCase) && !values.ContainsKey("created_at")) values["created_at"] = now.ToUnixTimeSeconds();
        if (columns.Contains("updated_at_ms", StringComparer.OrdinalIgnoreCase) && !values.ContainsKey("updated_at_ms")) values["updated_at_ms"] = now.ToUnixTimeMilliseconds();
        if (columns.Contains("created_at_ms", StringComparer.OrdinalIgnoreCase) && !values.ContainsKey("created_at_ms")) values["created_at_ms"] = now.ToUnixTimeMilliseconds();
        if (columns.Contains("recency_at", StringComparer.OrdinalIgnoreCase) && !values.ContainsKey("recency_at")) values["recency_at"] = now.ToUnixTimeSeconds();
        if (columns.Contains("recency_at_ms", StringComparer.OrdinalIgnoreCase) && !values.ContainsKey("recency_at_ms")) values["recency_at_ms"] = now.ToUnixTimeMilliseconds();
        foreach (var column in columns)
        {
            if (!values.ContainsKey(column)) values[column] = GetSafeDefaultForThreadColumn(column);
        }
        var insertColumns = columns.Where(column => values.ContainsKey(column)).ToList();
        var sql = "insert into threads (" + string.Join(", ", insertColumns.Select(QuoteIdentifier)) + ") values (" + string.Join(", ", insertColumns.Select(column => "@" + column)) + ")";
        database.Execute(sql, insertColumns.ToDictionary(column => column, column => NormalizeSqliteParameter(values[column]), StringComparer.OrdinalIgnoreCase));
    }

    private void DeleteRelatedConversationRows(SqliteDatabase database, string id)
    {
        foreach (var (table, column) in new[]
        {
            ("thread_dynamic_tools", "thread_id"),
            ("thread_spawn_edges", "parent_thread_id"),
            ("thread_spawn_edges", "child_thread_id"),
            ("agent_job_items", "assigned_thread_id")
        })
        {
            if (database.ScalarInt("select count(*) from sqlite_master where type = 'table' and name = @table", new Dictionary<string, object?> { ["table"] = table }) == 0) continue;
            if (!database.GetTableColumns(table).Contains(column, StringComparer.OrdinalIgnoreCase)) continue;
            database.Execute("delete from " + QuoteIdentifier(table) + " where " + QuoteIdentifier(column) + " = @id", new Dictionary<string, object?> { ["id"] = id });
        }
    }

    private void UpsertSessionIndex(ConversationPackageEntry entry)
    {
        var path = Path.Combine(root, "session_index.jsonl");
        var lines = File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8).ToList() : new List<string>();
        if (lines.Any(line => SessionIndexLineHasId(line, entry.Id))) return;
        var node = new JsonObject
        {
            ["id"] = entry.Id,
            ["thread_name"] = string.IsNullOrWhiteSpace(entry.Title) ? entry.FirstUserMessage : entry.Title,
            ["updated_at"] = entry.UpdatedAtMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(entry.UpdatedAtMs).UtcDateTime.ToString("O") : DateTimeOffset.UtcNow.ToString("O")
        };
        lines.Add(node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        AtomicFile.WriteText(path, lines);
    }

    private int RemoveSessionIndexEntries(IReadOnlyCollection<string> ids)
    {
        var path = Path.Combine(root, "session_index.jsonl");
        if (!File.Exists(path)) return 0;
        var original = File.ReadAllLines(path, Encoding.UTF8).ToList();
        var kept = original.Where(line => !ids.Any(id => SessionIndexLineHasId(line, id))).ToList();
        AtomicFile.WriteText(path, kept);
        return original.Count - kept.Count;
    }

    private static bool SessionIndexLineHasId(string line, string id)
    {
        try
        {
            var node = JsonNode.Parse(line)?.AsObject();
            return string.Equals(node?["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void RewriteSessionMeta(string path, string provider)
    {
        var original = File.ReadAllBytes(path);
        var lineEnd = Array.IndexOf(original, (byte)'\n');
        var firstLineLength = lineEnd >= 0 ? lineEnd : original.Length;
        if (firstLineLength > 0 && original[firstLineLength - 1] == '\r') firstLineLength--;
        var firstLine = Encoding.UTF8.GetString(original, 0, firstLineLength);
        var envelope = JsonNode.Parse(firstLine)?.AsObject();
        var payload = envelope?["payload"]?.AsObject();
        if (envelope is null || payload is null) return;
        payload["model_provider"] = provider;
        var replacement = Encoding.UTF8.GetBytes(envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        var remainderOffset = lineEnd >= 0 ? lineEnd : original.Length;
        var remainderLength = original.Length - remainderOffset;
        var updated = new byte[replacement.Length + remainderLength];
        Buffer.BlockCopy(replacement, 0, updated, 0, replacement.Length);
        if (remainderLength > 0) Buffer.BlockCopy(original, remainderOffset, updated, replacement.Length, remainderLength);
        AtomicFile.WriteBytes(path, updated);
    }

    private static string ValidatePackageEntryPath(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        if (normalized.Length == 0 || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("../", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("对话包包含不安全路径：" + path);
        }
        return normalized;
    }

    private static bool PackageEntryMatchesFile(ZipArchiveEntry entry, string path)
    {
        if (!File.Exists(path)) return false;
        using var input = entry.Open();
        return string.Equals(ComputeHash(input), ComputeHash(path), StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteImportedConversationFile(Stream input, string destinationPath)
    {
        var temporary = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) input.CopyTo(output);
            File.Move(temporary, destinationPath, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    private string NormalizeImportedCwd(string cwd)
    {
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            try { if (Directory.Exists(cwd)) return cwd; } catch { }
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static object? NormalizeSqliteParameter(object? value)
    {
        if (value is null) return null;
        if (value is long or int or short or byte or double or float or decimal) return value;
        var text = Convert.ToString(value) ?? string.Empty;
        if (long.TryParse(text, out var number)) return number;
        return text;
    }

    private static object? GetSafeDefaultForThreadColumn(string column) => column switch
    {
        "tokens_used" or "has_user_event" or "archived" or "created_at" or "updated_at" or "created_at_ms" or "updated_at_ms" or "recency_at" or "recency_at_ms" => 0,
        "memory_mode" => "enabled",
        "source" => "vscode",
        "thread_source" => "user",
        "approval_mode" => "on-request",
        "sandbox_policy" => "",
        _ => ""
    };

    private static string GetRowString(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
    private static long GetRowLong(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) && long.TryParse(Convert.ToString(value), out var number) ? number : 0;
    private static string? ToManifestString(object? value) => value is null ? null : Convert.ToString(value);
    private static string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private static string SanitizePackageSegment(string value) => SanitizeFileName(value).Replace('\\', '-').Replace('/', '-');
    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToHashSet();
        var clean = new string((value ?? string.Empty).Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return clean.Length == 0 ? "conversation.jsonl" : clean;
    }
    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return ComputeHash(stream);
    }
    private static string ComputeHash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
}
