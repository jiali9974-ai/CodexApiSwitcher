using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexApiSwitcher.Core;

internal sealed partial class SwitcherService
{
    internal string RepairConversationIndex()
    {
        AssertConfig();
        AssertCodexIsStopped();
        var statePaths = GetStateDatabasePaths();
        if (statePaths.Count == 0) throw new FileNotFoundException("state_5.sqlite was not found in the Codex root or sqlite subdirectory.");

        var restored = 0;
        var backups = new List<string>();
        foreach (var statePath in statePaths)
        {
            backups.Add(CreateStateBackup(statePath, "pre-sidebar-repair"));
            using var database = SqliteDatabase.Open(statePath);
            EnsureThreadColumns(database);
            var before = database.ScalarInt("select count(*) from threads where has_user_event = 1");
            database.Execute("update threads set has_user_event = 1 where has_user_event = 0 and first_user_message <> '' and source in ('vscode','cli')");
            database.EnsureIntegrity();
            restored += database.ScalarInt("select count(*) from threads where has_user_event = 1") - before;
        }
        return $"已检查 {statePaths.Count} 套状态数据库，恢复 {restored} 条会话的侧栏可见标记。备份：{string.Join("；", backups)}";
    }

    private HistorySyncOutcome TrySynchronizeConversationProvider(string targetProvider)
    {
        var skippedMissing = 0;
        try
        {
            var summary = SynchronizeConversationProvider(targetProvider, out skippedMissing);
            var outcome = HistorySyncOutcome.Success(skippedMissing, summary);
            if (outcome.HasNotice) AppendHistorySyncWarning(outcome.ToCommandLineNotice());
            return outcome;
        }
        catch (Exception ex)
        {
            var outcome = HistorySyncOutcome.Warning(skippedMissing, ex.Message);
            AppendHistorySyncWarning(outcome.ToCommandLineNotice());
            return outcome;
        }
    }

    private string SynchronizeConversationProvider(string targetProvider, out int skippedMissing)
    {
        skippedMissing = 0;
        var statePaths = GetStateDatabasePaths();
        if (statePaths.Count == 0) return "No state database exists yet; history synchronization was skipped.";
        if (targetProvider is not ("openai" or ProviderId)) throw new InvalidOperationException("Unsupported provider: " + targetProvider);

        const string where = "first_user_message <> '' and source in ('vscode','cli')";
        var rolloutPaths = new List<string>();
        foreach (var statePath in statePaths)
        {
            using var database = SqliteDatabase.Open(statePath);
            EnsureThreadColumns(database);
            rolloutPaths.AddRange(database.QueryTextColumn("select rollout_path from threads where " + where + " order by rollout_path"));
        }

        var metadata = SynchronizeSessionMetadata(rolloutPaths.Distinct(PathComparer).ToList(), targetProvider);
        skippedMissing = metadata.SkippedMissingCount;
        var backups = new List<string>();
        var total = 0;
        var providerChanges = 0;
        var visibilityChanges = 0;
        try
        {
            foreach (var statePath in statePaths)
            {
                backups.Add(CreateStateBackup(statePath, "pre-provider-sync"));
                using var database = SqliteDatabase.Open(statePath);
                EnsureThreadColumns(database);
                database.Execute("begin immediate");
                try
                {
                    total += database.ScalarInt("select count(*) from threads where " + where);
                    providerChanges += database.ScalarInt("select count(*) from threads where " + where + " and model_provider <> '" + targetProvider + "'");
                    visibilityChanges += database.ScalarInt("select count(*) from threads where " + where + " and has_user_event = 0");
                    database.Execute("update threads set model_provider = '" + targetProvider + "', has_user_event = 1 where " + where);
                    database.EnsureIntegrity();
                    var remaining = database.ScalarInt("select count(*) from threads where " + where + " and model_provider <> '" + targetProvider + "'");
                    if (remaining != 0) throw new InvalidOperationException($"Provider synchronization incomplete in {statePath}: {remaining} rows remain.");
                    database.Execute("commit");
                }
                catch
                {
                    try { database.Execute("rollback"); } catch { }
                    throw;
                }
            }
            return $"已同步 {statePaths.Count} 套状态数据库中的 {total} 条用户会话到 {targetProvider}（数据库 provider 变更 {providerChanges} 条，JSONL 元数据变更 {metadata.ChangedCount} 条，可见标记恢复 {visibilityChanges} 条，缺失 JSONL 跳过 {metadata.SkippedMissingCount} 条）。数据库备份：{string.Join("；", backups)}；会话备份：{metadata.BackupDirectory}";
        }
        catch
        {
            metadata.Rollback();
            throw;
        }
    }

    private SessionMetadataSyncResult SynchronizeSessionMetadata(List<string> rolloutPaths, string targetProvider)
    {
        var backupRoot = Path.Combine(root, "history_sync_backups", "session-meta-provider-sync-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        var changes = new List<SessionMetadataChange>();
        var resolved = ResolveConversationJsonlPaths(rolloutPaths, out var skippedMissing);
        foreach (var fullPath in resolved)
        {
            byte[] original;
            try { original = File.ReadAllBytes(fullPath); }
            catch (FileNotFoundException) { skippedMissing++; continue; }
            catch (DirectoryNotFoundException) { skippedMissing++; continue; }

            var lineEnd = Array.IndexOf(original, (byte)'\n');
            var firstLineLength = lineEnd >= 0 ? lineEnd : original.Length;
            if (firstLineLength > 0 && original[firstLineLength - 1] == '\r') firstLineLength--;
            var firstLine = Encoding.UTF8.GetString(original, 0, firstLineLength);
            var envelope = JsonNode.Parse(firstLine)?.AsObject() ?? throw new InvalidOperationException("Conversation JSONL first line is invalid: " + fullPath);
            var payload = envelope["payload"]?.AsObject() ?? throw new InvalidOperationException("Conversation JSONL has no payload object: " + fullPath);
            if (string.Equals(payload["model_provider"]?.GetValue<string>(), targetProvider, StringComparison.Ordinal)) continue;
            payload["model_provider"] = targetProvider;
            var replacement = Encoding.UTF8.GetBytes(envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            var remainderOffset = lineEnd >= 0 ? lineEnd : original.Length;
            var remainderLength = original.Length - remainderOffset;
            var updated = new byte[replacement.Length + remainderLength];
            Buffer.BlockCopy(replacement, 0, updated, 0, replacement.Length);
            if (remainderLength > 0) Buffer.BlockCopy(original, remainderOffset, updated, replacement.Length, remainderLength);
            var backupPath = Path.Combine(backupRoot, GetPathToken(fullPath) + "-" + Path.GetFileName(fullPath));
            changes.Add(new SessionMetadataChange(fullPath, backupPath, original, updated));
        }

        if (changes.Count == 0) return new SessionMetadataSyncResult(0, "无需创建（JSONL 已一致）", changes, skippedMissing);
        var applied = new List<SessionMetadataChange>();
        try
        {
            foreach (var change in changes)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(change.BackupPath)!);
                File.Copy(change.Path, change.BackupPath, false);
                AtomicFile.WriteBytes(change.Path, change.UpdatedBytes);
                applied.Add(change);
            }
        }
        catch
        {
            foreach (var change in applied) AtomicFile.WriteBytes(change.Path, change.OriginalBytes);
            throw;
        }
        return new SessionMetadataSyncResult(changes.Count, backupRoot, changes, skippedMissing);
    }

    private List<string> ResolveConversationJsonlPaths(IEnumerable<string> rolloutPaths, out int skippedMissing)
    {
        skippedMissing = 0;
        var paths = new List<string>();
        foreach (var rolloutPath in rolloutPaths)
        {
            if (string.IsNullOrWhiteSpace(rolloutPath)) { skippedMissing++; continue; }
            try
            {
                var path = ResolveConversationJsonlPath(rolloutPath);
                if (!paths.Contains(path, PathComparer)) paths.Add(path);
            }
            catch (FileNotFoundException) { skippedMissing++; }
        }
        return paths;
    }

    private string ResolveConversationJsonlPath(string rolloutPath)
    {
        var fullPath = Path.GetFullPath(RemoveExtendedPathPrefix(rolloutPath));
        if (IsInsideRoot(fullPath) && File.Exists(fullPath)) return fullPath;
        var relocated = FindRelocatedConversationJsonl(fullPath);
        if (relocated.Length > 0) return relocated;
        throw new FileNotFoundException("Conversation JSONL was not found in the current Codex root: " + fullPath, fullPath);
    }

    private string FindRelocatedConversationJsonl(string missingFullPath)
    {
        var fileName = Path.GetFileName(missingFullPath);
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
        var candidates = new List<string> { Path.Combine(root, "archived_sessions", fileName) };
        AddConversationCandidates(candidates, Path.Combine(root, "archived_sessions"), fileName);
        AddConversationCandidates(candidates, Path.Combine(root, "sessions"), fileName);
        return candidates.Select(Path.GetFullPath).FirstOrDefault(candidate => IsInsideRoot(candidate) && File.Exists(candidate)) ?? string.Empty;
    }

    private static void AddConversationCandidates(List<string> candidates, string directory, string fileName)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.GetFiles(directory, fileName, SearchOption.AllDirectories))
        {
            if (!candidates.Contains(path, PathComparer)) candidates.Add(path);
        }
    }

    private bool IsInsideRoot(string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private List<string> GetStateDatabasePaths() => new[]
        {
            Path.Combine(root, "sqlite", "state_5.sqlite"),
            Path.Combine(root, "state_5.sqlite")
        }
        .Where(File.Exists)
        .Distinct(PathComparer)
        .ToList();

    private string CreateStateBackup(string statePath, string purpose)
    {
        var directory = Path.Combine(root, "history_sync_backups");
        Directory.CreateDirectory(directory);
        var location = PathEquals(Path.GetDirectoryName(statePath)!, root) ? "root" : "sqlite";
        var backupPath = Path.Combine(directory, $"state_5.sqlite.{location}.{purpose}.{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak");
        SqliteDatabase.Backup(statePath, backupPath);
        return backupPath;
    }

    private static void EnsureThreadColumns(SqliteDatabase database)
    {
        foreach (var column in new[] { "source", "first_user_message", "has_user_event", "model_provider" })
        {
            if (database.ScalarInt("select count(*) from pragma_table_info('threads') where name = '" + column + "'") == 0)
            {
                throw new InvalidOperationException("threads table is missing required column: " + column);
            }
        }
    }

    private void AppendHistorySyncWarning(string message)
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.AppendAllText(Path.Combine(dataDirectory, "history-sync-warnings.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { }
    }

    private static string RemoveExtendedPathPrefix(string path) =>
        OperatingSystem.IsWindows() && path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
