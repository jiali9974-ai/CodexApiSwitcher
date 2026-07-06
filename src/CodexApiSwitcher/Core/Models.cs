using System.Diagnostics;
using System.Runtime.Versioning;

namespace CodexApiSwitcher.Core;

internal sealed record ThirdPartyProfile(
    string Name,
    string BaseUrl,
    string Model,
    string CredentialFileName);

internal sealed class StoredSettings
{
    internal string BaseUrl { get; set; } = string.Empty;
    internal string ThirdPartyModel { get; set; } = string.Empty;
    internal string OfficialModel { get; set; } = string.Empty;
    internal string OpenHotkey { get; set; } = string.Empty;
    internal string OpenMouseButton { get; set; } = string.Empty;
    internal bool ThirdPartyCompatibilityMode { get; set; }
    internal bool ArmorThirdPartyReminderShown { get; set; }

    internal string GetOpenHotkey() => HotkeySetting.ParseOrDefault(OpenHotkey).ToDisplayString();
    internal string GetOpenMouseButton() => MouseButtonSetting.ParseOrDefault(OpenMouseButton).ToDisplayString();
}

internal sealed record ProviderStatus(
    string Provider,
    string Model,
    string BaseUrl,
    bool UsesCredentialHelper,
    bool ReusesOpenAiLogin,
    bool HasCodexApiKeyOverride)
{
    internal bool IsThirdParty => string.Equals(Provider, "custom", StringComparison.OrdinalIgnoreCase);

    internal string ToDisplayString()
    {
        if (!IsThirdParty)
        {
            var warning = HasCodexApiKeyOverride
                ? " | 警告：CODEX_API_KEY 正在覆盖 ChatGPT 登录"
                : string.Empty;
            return $"官方 OpenAI 登录 | 模型 {Model}{warning}";
        }

        var auth = UsesCredentialHelper
            ? "独立加密 Key"
            : ReusesOpenAiLogin ? "复用 OpenAI 登录" : "未检测到认证";
        return $"第三方 Responses API | {BaseUrl} | 模型 {Model} | {auth}";
    }
}


internal sealed record ReconnectingRepairResult(
    string EnvPath,
    string ProxyUrl,
    string AllProxyUrl,
    bool ExistingFileBackedUp,
    string BackupPath,
    bool ProbeSucceeded,
    string ProbeMessage)
{
    internal string ToDisplayString()
    {
        var backup = ExistingFileBackedUp ? $"\n原 .env 已备份到：{BackupPath}" : string.Empty;
        var probe = ProbeSucceeded ? "代理端口连通性检测通过。" : "未完成代理端口连通性检测：" + ProbeMessage;
        return $"已写入 Codex 代理配置：{EnvPath}\nHTTP/HTTPS_PROXY = {ProxyUrl}\nALL_PROXY = {AllProxyUrl}{backup}\n{probe}";
    }
}

internal sealed record CompatibilityBackupEntry(
    string Scope,
    string Section,
    string Key,
    bool Existed,
    string Line);

internal sealed record ArmorStatus(
    bool IsEnabled,
    string ConfiguredValue,
    bool InstructionFileExists)
{
    internal string ToDisplayString()
    {
        if (IsEnabled) return "破甲状态：已启用；重启 Codex 后生效。";
        if (!string.IsNullOrWhiteSpace(ConfiguredValue))
        {
            return $"破甲状态：未启用；当前指令文件为 {ConfiguredValue}。";
        }
        return "破甲状态：未启用。";
    }
}

internal sealed class HistorySyncOutcome
{
    private HistorySyncOutcome(int skippedMissingCount, string warningMessage, string summary)
    {
        SkippedMissingCount = skippedMissingCount;
        WarningMessage = warningMessage ?? string.Empty;
        Summary = summary ?? string.Empty;
    }

    internal int SkippedMissingCount { get; }
    internal string WarningMessage { get; }
    internal string Summary { get; }
    internal bool HasNotice => SkippedMissingCount > 0 || WarningMessage.Length > 0;

    internal static HistorySyncOutcome Success(int skippedMissingCount, string summary) =>
        new(skippedMissingCount, string.Empty, summary);

    internal static HistorySyncOutcome Warning(int skippedMissingCount, string warningMessage) =>
        new(skippedMissingCount, warningMessage, string.Empty);

    internal string ToUserNotice()
    {
        var notices = new List<string>();
        if (SkippedMissingCount > 0)
        {
            notices.Add($"检测到 {SkippedMissingCount} 条会话文件路径已失效，可能是 Codex 更新后移动或清理了历史文件；已跳过这些记录，不影响本次 API 切换。");
        }
        if (WarningMessage.Length > 0)
        {
            notices.Add("Codex 更新后可能改变了历史数据格式，本次历史同步未完全执行，但 API 切换已经完成。\n详情：" + WarningMessage);
        }
        return notices.Count == 0 ? string.Empty : "\n\n" + string.Join("\n\n", notices);
    }

    internal string ToCommandLineNotice()
    {
        var notices = new List<string>();
        if (SkippedMissingCount > 0)
        {
            notices.Add($"Skipped {SkippedMissingCount} missing or stale conversation JSONL file(s); Codex may have moved or cleaned them during an update.");
        }
        if (WarningMessage.Length > 0)
        {
            notices.Add("History synchronization warning (the API switch still completed): " + WarningMessage);
        }
        return string.Join(Environment.NewLine, notices);
    }
}

internal sealed record SwitchOutcome(
    HistorySyncOutcome HistorySync,
    bool CodexApiKeyOverrideCleared,
    bool CodexApiKeyOverrideRestored,
    bool ThirdPartyCompatibilityEnabled,
    string CompatibilityNotice)
{
    internal bool HasNotice =>
        HistorySync.HasNotice || CodexApiKeyOverrideCleared || CodexApiKeyOverrideRestored || CompatibilityNotice.Length > 0;

    internal string ToUserNotice()
    {
        var notices = new List<string>();
        var history = HistorySync.ToUserNotice().Trim();
        if (history.Length > 0) notices.Add(history);
        if (CodexApiKeyOverrideCleared)
        {
            notices.Add("检测到 CODEX_API_KEY 会覆盖 ChatGPT 官方登录。已加密暂存并从当前应用环境移除；由 CAS 启动 Codex 时会使用官方登录环境。");
        }
        if (CodexApiKeyOverrideRestored)
        {
            notices.Add("已恢复先前暂存的 CODEX_API_KEY 环境变量。由 CAS 启动 Codex 时会继续使用它。");
        }
        if (CompatibilityNotice.Length > 0) notices.Add(CompatibilityNotice);
        return notices.Count == 0 ? string.Empty : "\n\n" + string.Join("\n\n", notices);
    }

    internal string ToCommandLineNotice()
    {
        var notices = new List<string>();
        var history = HistorySync.ToCommandLineNotice();
        if (history.Length > 0) notices.Add(history);
        if (CodexApiKeyOverrideCleared)
        {
            notices.Add("Encrypted and cleared the CODEX_API_KEY override for CAS-launched Codex processes so ChatGPT login is used.");
        }
        if (CodexApiKeyOverrideRestored)
        {
            notices.Add("Restored the previously saved CODEX_API_KEY override for CAS-launched Codex processes.");
        }
        if (CompatibilityNotice.Length > 0) notices.Add(CompatibilityNotice);
        return string.Join(Environment.NewLine, notices);
    }
}

internal sealed class EnvironmentChange
{
    internal static readonly EnvironmentChange None = new(false, null);
    private readonly Action? rollback;
    private bool committed;

    internal EnvironmentChange(bool wasCleared, Action? rollbackAction)
    {
        WasCleared = wasCleared;
        rollback = rollbackAction;
    }

    internal bool WasCleared { get; }
    internal void Commit() => committed = true;
    internal void Rollback()
    {
        if (!committed) rollback?.Invoke();
    }
}

internal sealed record SessionMetadataChange(
    string Path,
    string BackupPath,
    byte[] OriginalBytes,
    byte[] UpdatedBytes);

internal sealed class SessionMetadataSyncResult
{
    private readonly IReadOnlyList<SessionMetadataChange> changes;

    internal SessionMetadataSyncResult(
        int changedCount,
        string backupDirectory,
        IReadOnlyList<SessionMetadataChange> metadataChanges,
        int skippedMissingCount)
    {
        ChangedCount = changedCount;
        BackupDirectory = backupDirectory;
        changes = metadataChanges;
        SkippedMissingCount = skippedMissingCount;
    }

    internal int ChangedCount { get; }
    internal string BackupDirectory { get; }
    internal int SkippedMissingCount { get; }

    internal void Rollback()
    {
        foreach (var change in changes)
        {
            AtomicFile.WriteBytes(change.Path, change.OriginalBytes);
        }
    }
}


internal sealed record ConversationSummary(
    string Id,
    string Title,
    string FirstUserMessage,
    string Preview,
    string Model,
    string ModelProvider,
    string Source,
    string RolloutPath,
    bool RolloutExists,
    DateTimeOffset UpdatedAt,
    long UpdatedAtUnix,
    long UpdatedAtMs,
    string DatabasePath)
{
    internal string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? FirstUserMessage : Title;
    internal string FileState => RolloutExists ? "文件正常" : "文件缺失";
}

internal sealed record ConversationExportResult(
    int RequestedCount,
    int ExportedCount,
    int SkippedMissingFileCount,
    string OutputPath)
{
    internal string ToDisplayString() => $"已导出 {ExportedCount} 条对话到：{OutputPath}" +
        (SkippedMissingFileCount > 0 ? $"\n跳过 {SkippedMissingFileCount} 条缺失会话文件的记录。" : string.Empty);
}

internal sealed record ConversationImportResult(
    int ImportedCount,
    int SkippedExistingCount,
    string PackagePath)
{
    internal string ToDisplayString() => $"已从对话包导入 {ImportedCount} 条对话。" +
        (SkippedExistingCount > 0 ? $"\n跳过 {SkippedExistingCount} 条已存在的对话。" : string.Empty);
}

internal sealed record ConversationDeleteResult(
    int RequestedCount,
    int DeletedDatabaseRows,
    int DeletedFiles,
    int RemovedIndexEntries)
{
    internal string ToDisplayString() => $"已删除 {DeletedDatabaseRows} 条对话索引，删除 {DeletedFiles} 个会话文件，移除 {RemovedIndexEntries} 条 session_index 记录。";
}

internal sealed class ConversationPackageManifest
{
    public int Version { get; set; } = 1;
    public string CreatedAt { get; set; } = string.Empty;
    public string SourcePlatform { get; set; } = string.Empty;
    public List<ConversationPackageEntry> Conversations { get; set; } = new();
}

internal sealed class ConversationPackageEntry
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FirstUserMessage { get; set; } = string.Empty;
    public string Preview { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ModelProvider { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string OriginalRolloutPath { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public long UpdatedAt { get; set; }
    public long UpdatedAtMs { get; set; }
    public Dictionary<string, string?> ThreadValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record LaunchTarget(
    string DisplayName,
    string FileName,
    IReadOnlyList<string> Arguments,
    bool UseShellExecute,
    bool OpenInTerminal = false)
{
    internal ProcessStartInfo CreateStartInfo(string? codexApiKey)
    {
        if (OpenInTerminal && OperatingSystem.IsMacOS()) return CreateMacTerminalStartInfo(codexApiKey);

        var info = new ProcessStartInfo(FileName)
        {
            UseShellExecute = UseShellExecute,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in Arguments) info.ArgumentList.Add(argument);
        if (!UseShellExecute)
        {
            if (string.IsNullOrWhiteSpace(codexApiKey)) info.Environment.Remove("CODEX_API_KEY");
            else info.Environment["CODEX_API_KEY"] = codexApiKey;
        }
        return info;
    }

    [SupportedOSPlatform("macos")]
    private ProcessStartInfo CreateMacTerminalStartInfo(string? codexApiKey)
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), "codex-api-switcher");
        Directory.CreateDirectory(scriptDirectory);
        var scriptPath = Path.Combine(scriptDirectory, "launch-codex.command");
        var lines = new List<string>
        {
            "#!/bin/sh",
            "cd " + ShellQuote(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
        };
        if (string.IsNullOrWhiteSpace(codexApiKey)) lines.Add("unset CODEX_API_KEY");
        else lines.Add("export CODEX_API_KEY=" + ShellQuote(codexApiKey));
        lines.Add("exec " + ShellQuote(FileName));
        File.WriteAllLines(scriptPath, lines);
        File.SetUnixFileMode(scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var info = new ProcessStartInfo("open")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        info.ArgumentList.Add("-a");
        info.ArgumentList.Add("Terminal");
        info.ArgumentList.Add(scriptPath);
        return info;
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
