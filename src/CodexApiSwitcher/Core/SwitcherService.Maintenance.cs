namespace CodexApiSwitcher.Core;

internal sealed partial class SwitcherService
{
    internal BackupCleanupResult CleanCasBackups()
    {
        AssertConfig();
        AssertCodexIsStopped();
        var targets = new[]
        {
            backupDirectory,
            Path.Combine(root, "history_sync_backups")
        };
        var deletedDirectories = 0;
        var deletedFiles = 0;
        long reclaimedBytes = 0;
        var deletedPaths = new List<string>();
        foreach (var target in targets.Select(Path.GetFullPath).Distinct(PathComparer))
        {
            if (!IsInsideRoot(target)) throw new InvalidOperationException("备份目录不在 Codex 根目录内：" + target);
            if (!Directory.Exists(target)) continue;
            var files = Directory.GetFiles(target, "*", SearchOption.AllDirectories);
            deletedFiles += files.Length;
            foreach (var file in files)
            {
                try { reclaimedBytes += new FileInfo(file).Length; } catch { }
            }
            deletedDirectories += Directory.GetDirectories(target, "*", SearchOption.AllDirectories).Length + 1;
            Directory.Delete(target, true);
            deletedPaths.Add(target);
        }
        return new BackupCleanupResult(deletedDirectories, deletedFiles, reclaimedBytes, deletedPaths);
    }
}
