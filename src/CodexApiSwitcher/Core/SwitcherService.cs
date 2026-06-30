using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CodexApiSwitcher.Platform;

namespace CodexApiSwitcher.Core;

internal sealed partial class SwitcherService
{
    private const string ProviderId = "custom";
    private readonly string root;
    private readonly string executablePath;
    private readonly string configPath;
    private readonly string dataDirectory;
    private readonly string credentialPath;
    private readonly string settingsPath;
    private readonly string profilesPath;
    private readonly string profileCredentialDirectory;
    private readonly string backupDirectory;
    private readonly string stableHelperPath;
    private readonly string codexApiKeyEnvironmentBackupPath;
    private readonly string compatibilityBackupPath;
    private readonly ISecretStore secretStore;

    internal SwitcherService(string rootPath, string exePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) throw new InvalidOperationException("请选择 Codex 根目录。");
        root = Path.GetFullPath(rootPath);
        executablePath = Path.GetFullPath(exePath);
        configPath = Path.Combine(root, "config.toml");
        dataDirectory = Path.Combine(root, "api-switcher");
        credentialPath = Path.Combine(dataDirectory, "credential.dat");
        settingsPath = Path.Combine(dataDirectory, "settings.dat");
        profilesPath = Path.Combine(dataDirectory, "profiles.dat");
        profileCredentialDirectory = Path.Combine(dataDirectory, "profiles");
        backupDirectory = Path.Combine(root, "config-switcher-backups");
        stableHelperPath = Path.Combine(dataDirectory, OperatingSystem.IsWindows()
            ? "CodexApiSwitcher.AuthHelper.exe"
            : "codex-api-switcher-auth-helper");
        codexApiKeyEnvironmentBackupPath = Path.Combine(dataDirectory, "codex-api-key.user-env.dat");
        compatibilityBackupPath = Path.Combine(dataDirectory, "third-party-compatibility.dat");
        secretStore = SecretStoreFactory.Create(dataDirectory);
    }

    internal string Root => root;
    internal string SecretBackendName => secretStore.BackendName;

    internal ProviderStatus GetStatus()
    {
        var lines = ReadConfig();
        var provider = GetTopLevelValue(lines, "model_provider");
        var model = GetTopLevelValue(lines, "model");
        var section = "model_providers." + ProviderId;
        var url = GetSectionValue(lines, section, "base_url");
        var helperAuth = SectionExists(lines, section + ".auth");
        var reusedLogin = string.Equals(GetSectionValue(lines, section, "requires_openai_auth"), "true", StringComparison.OrdinalIgnoreCase);
        return new ProviderStatus(provider, model, url, helperAuth, reusedLogin, IsRealCodexRoot() && HasCodexApiKeyEnvironmentOverride());
    }

    internal StoredSettings LoadSettings()
    {
        var settings = new StoredSettings();
        if (!File.Exists(settingsPath)) return settings;
        foreach (var line in File.ReadAllLines(settingsPath, Encoding.UTF8))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            string value;
            try { value = DecodeSetting(line[(separator + 1)..]); }
            catch { continue; }
            switch (line[..separator])
            {
                case "url": settings.BaseUrl = value; break;
                case "thirdModel": settings.ThirdPartyModel = value; break;
                case "officialModel": settings.OfficialModel = value; break;
                case "openHotkey": settings.OpenHotkey = value; break;
                case "openMouseButton": settings.OpenMouseButton = value; break;
                case "thirdPartyCompatibilityMode":
                    settings.ThirdPartyCompatibilityMode = value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
        return settings;
    }

    internal void SaveOpenHotkey(string hotkey)
    {
        var settings = LoadSettings();
        settings.OpenHotkey = HotkeySetting.Parse(hotkey).ToDisplayString();
        SaveSettings(settings);
    }

    internal void SaveOpenMouseButton(string mouseButton)
    {
        var settings = LoadSettings();
        settings.OpenMouseButton = MouseButtonSetting.Parse(mouseButton).ToDisplayString();
        SaveSettings(settings);
    }

    internal bool HasStoredToken() => secretStore.Exists(credentialPath);

    internal string ReadToken()
    {
        if (!HasStoredToken()) throw new InvalidOperationException("No encrypted third-party API key is stored.");
        return secretStore.Read(credentialPath, "default");
    }

    internal List<ThirdPartyProfile> LoadThirdPartyProfiles()
    {
        var profiles = new List<ThirdPartyProfile>();
        if (!File.Exists(profilesPath)) return profiles;
        foreach (var line in File.ReadAllLines(profilesPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('|');
            if (parts.Length < 4 || parts[0] != "profile") continue;
            try
            {
                var name = DecodeSetting(parts[1]);
                var baseUrl = DecodeSetting(parts[2]);
                var model = DecodeSetting(parts[3]);
                var fileName = parts.Length >= 5 ? DecodeSetting(parts[4]) : GetProfileCredentialFileName(name);
                if (name.Length > 0 && baseUrl.Length > 0 && model.Length > 0 && fileName.Length > 0)
                {
                    profiles.Add(new ThirdPartyProfile(name, baseUrl, model, fileName));
                }
            }
            catch { }
        }
        return profiles
            .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal ThirdPartyProfile SaveThirdPartyProfile(string name, string url, string model, string key)
    {
        var cleanName = NormalizeProfileName(name);
        var cleanUrl = NormalizeBaseUrl(url);
        var cleanModel = (model ?? string.Empty).Trim();
        var cleanKey = (key ?? string.Empty).Trim();
        if (cleanModel.Length == 0) throw new InvalidOperationException("Profile model is required.");
        if (cleanKey.Length == 0) throw new InvalidOperationException("Profile API key is required.");

        Directory.CreateDirectory(profileCredentialDirectory);
        var profiles = LoadThirdPartyProfiles();
        var existing = profiles.FirstOrDefault(profile => string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
        var fileName = existing?.CredentialFileName ?? GetProfileCredentialFileName(cleanName);
        var updated = new ThirdPartyProfile(cleanName, cleanUrl, cleanModel, fileName);
        profiles.RemoveAll(profile => string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
        profiles.Add(updated);
        secretStore.Save(GetProfileCredentialPath(updated), cleanKey, "profile");
        SaveThirdPartyProfiles(profiles);
        return updated;
    }

    internal void DeleteThirdPartyProfile(string name)
    {
        var cleanName = NormalizeProfileName(name);
        var profiles = LoadThirdPartyProfiles();
        var existing = profiles.FirstOrDefault(profile => string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;
        profiles.RemoveAll(profile => string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
        SaveThirdPartyProfiles(profiles);
        secretStore.Delete(GetProfileCredentialPath(existing), "profile");
    }

    internal string ReadThirdPartyProfileToken(string name)
    {
        var cleanName = NormalizeProfileName(name);
        var profile = LoadThirdPartyProfiles().FirstOrDefault(candidate => string.Equals(candidate.Name, cleanName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Third-party profile was not found: " + cleanName);
        var path = GetProfileCredentialPath(profile);
        if (!secretStore.Exists(path)) throw new InvalidOperationException("The selected profile has no encrypted API key. It may have been moved or deleted.");
        return secretStore.Read(path, "profile");
    }

    internal SwitchOutcome SwitchToThirdParty(string url, string model, string key, string profileName, bool compatibilityMode)
    {
        AssertConfig();
        AssertCodexIsStopped();
        var cleanUrl = NormalizeBaseUrl(url);
        var cleanModel = (model ?? string.Empty).Trim();
        if (cleanModel.Length == 0) throw new InvalidOperationException("Base URL and model are required.");

        Directory.CreateDirectory(dataDirectory);
        var cleanKey = (key ?? string.Empty).Trim();
        var cleanProfileName = (profileName ?? string.Empty).Trim();
        if (cleanKey.Length > 0)
        {
            secretStore.Save(credentialPath, cleanKey, "default");
            var effectiveName = cleanProfileName.Length == 0 ? GetDefaultProfileName(cleanUrl) : cleanProfileName;
            SaveThirdPartyProfile(effectiveName, cleanUrl, cleanModel, cleanKey);
        }
        else if (cleanProfileName.Length > 0)
        {
            cleanKey = ReadThirdPartyProfileToken(cleanProfileName);
            secretStore.Save(credentialPath, cleanKey, "default");
        }
        else if (!HasStoredToken())
        {
            throw new InvalidOperationException("An API key is required for the first third-party switch.");
        }

        var lines = ReadConfig();
        BackupConfig();
        InstallStableCredentialHelper();
        var history = TrySynchronizeConversationProvider(ProviderId);
        SetTopLevelValue(lines, "model_provider", ProviderId);
        SetTopLevelValue(lines, "model", cleanModel);
        RemoveProviderSections(lines);
        AddProviderSections(lines, cleanUrl);
        var compatibilitySummary = compatibilityMode
            ? ApplyThirdPartyCompatibilityMode(lines)
            : RestoreThirdPartyCompatibilityMode(lines);
        WriteConfigAtomically(lines);
        if (!compatibilityMode && compatibilitySummary.Length > 0) DeleteCompatibilityBackup();

        var settings = LoadSettings();
        settings.BaseUrl = cleanUrl;
        settings.ThirdPartyModel = cleanModel;
        settings.ThirdPartyCompatibilityMode = false;
        if (settings.OfficialModel.Length == 0) settings.OfficialModel = "gpt-5.5";
        SaveSettings(settings);
        var restored = RestoreCodexApiKeyEnvironmentOverride();
        return new SwitchOutcome(history, false, restored, compatibilityMode, compatibilitySummary);
    }

    internal SwitchOutcome SwitchToOfficial(string model)
    {
        AssertConfig();
        AssertCodexIsStopped();
        var cleanModel = (model ?? string.Empty).Trim();
        if (cleanModel.Length == 0) throw new InvalidOperationException("Official model is required.");

        var lines = ReadConfig();
        BackupConfig();
        var environmentChange = CaptureAndClearCodexApiKeyEnvironmentOverride();
        try
        {
            var history = TrySynchronizeConversationProvider("openai");
            SetTopLevelValue(lines, "model_provider", "openai");
            SetTopLevelValue(lines, "model", cleanModel);
            RemoveProviderSections(lines);
            var compatibilitySummary = RestoreThirdPartyCompatibilityMode(lines);
            WriteConfigAtomically(lines);
            if (compatibilitySummary.Length > 0) DeleteCompatibilityBackup();
            var settings = LoadSettings();
            settings.OfficialModel = cleanModel;
            SaveSettings(settings);
            environmentChange.Commit();
            return new SwitchOutcome(history, environmentChange.WasCleared, false, false, compatibilitySummary);
        }
        catch
        {
            environmentChange.Rollback();
            throw;
        }
    }

    internal void ResetModelConfiguration(string model)
    {
        AssertConfig();
        var cleanModel = string.IsNullOrWhiteSpace(model) ? "gpt-5.5" : model.Trim();
        var lines = ReadConfig();
        BackupConfig();
        SetTopLevelValue(lines, "model_provider", "openai");
        SetTopLevelValue(lines, "model", cleanModel);
        RemoveProviderSections(lines);
        var compatibilitySummary = RestoreThirdPartyCompatibilityMode(lines);
        WriteConfigAtomically(lines);
        if (compatibilitySummary.Length > 0) DeleteCompatibilityBackup();
        var settings = LoadSettings();
        settings.OfficialModel = cleanModel;
        SaveSettings(settings);
    }

    internal void Rollback()
    {
        AssertConfig();
        var latest = Directory.Exists(backupDirectory)
            ? new DirectoryInfo(backupDirectory).GetFiles("config.toml.*.bak").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (latest is null) throw new InvalidOperationException("No configuration backup was found.");
        BackupConfig();
        File.Copy(latest.FullName, configPath, true);
    }

    internal string GetCodexLaunchPlan() => "Codex launch target: " + CodexPlatform.ResolveLaunchTarget().DisplayName;

    internal string LaunchCodex()
    {
        if (CodexPlatform.IsCodexRunning()) return "Codex 已经在运行。";
        var target = CodexPlatform.ResolveLaunchTarget();
        string? key = null;
        if (GetStatus().IsThirdParty && secretStore.Exists(codexApiKeyEnvironmentBackupPath))
        {
            key = secretStore.Read(codexApiKeyEnvironmentBackupPath, "environment");
        }
        Process.Start(target.CreateStartInfo(key));
        return "已发送 Codex 启动请求：" + target.DisplayName;
    }

    internal string CloseCodexProcesses(bool dryRun)
    {
        var processes = CodexPlatform.GetCodexProcesses();
        if (dryRun)
        {
            var count = processes.Count;
            foreach (var process in processes) process.Dispose();
            return $"Would close {count} Codex process(es).";
        }
        if (processes.Count == 0) return "未发现正在运行的 Codex 进程。";

        var closed = 0;
        var failures = new List<string>();
        foreach (var process in processes)
        {
            try { if (CodexPlatform.CloseProcess(process)) closed++; }
            catch (Exception ex) { failures.Add(SafeProcessName(process) + ": " + ex.Message); }
            finally { process.Dispose(); }
        }
        if (failures.Count > 0) throw new InvalidOperationException($"已关闭 {closed} 个 Codex 进程，但有 {failures.Count} 个关闭失败：{string.Join("；", failures)}");
        return $"已关闭 {closed} 个 Codex 进程。";
    }

    private void SaveSettings(StoredSettings settings)
    {
        AtomicFile.WriteText(settingsPath, new[]
        {
            "url=" + EncodeSetting(settings.BaseUrl),
            "thirdModel=" + EncodeSetting(settings.ThirdPartyModel),
            "officialModel=" + EncodeSetting(settings.OfficialModel),
            "openHotkey=" + EncodeSetting(settings.GetOpenHotkey()),
            "openMouseButton=" + EncodeSetting(settings.GetOpenMouseButton()),
            "thirdPartyCompatibilityMode=" + EncodeSetting(settings.ThirdPartyCompatibilityMode ? "1" : "0")
        });
    }

    private void SaveThirdPartyProfiles(IEnumerable<ThirdPartyProfile> profiles)
    {
        AtomicFile.WriteText(profilesPath, profiles.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Select(profile =>
            "profile|" + EncodeSetting(profile.Name) + "|" + EncodeSetting(profile.BaseUrl) + "|" + EncodeSetting(profile.Model) + "|" + EncodeSetting(profile.CredentialFileName)));
    }

    private string GetProfileCredentialPath(ThirdPartyProfile profile) => Path.Combine(profileCredentialDirectory, profile.CredentialFileName);

    private void InstallStableCredentialHelper()
    {
        if (!File.Exists(executablePath))
        {
            var candidates = CurrentExecutable.DescribeCandidates();
            throw new FileNotFoundException("Current switcher executable was not found. Tried: " + candidates, executablePath);
        }
        Directory.CreateDirectory(dataDirectory);
        if (!PathEquals(executablePath, stableHelperPath)) File.Copy(executablePath, stableHelperPath, true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stableHelperPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static string NormalizeProfileName(string? name)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0) clean = "未命名中转站";
        if (clean.Length > 80) clean = clean[..80].Trim();
        return clean.Length == 0 ? "未命名中转站" : clean;
    }

    private static string GetDefaultProfileName(string url)
    {
        try { return new Uri(url).Host is { Length: > 0 } host ? host : "中转站"; }
        catch { return "中转站"; }
    }

    private static string GetProfileCredentialFileName(string name) => GetPathToken("profile:" + NormalizeProfileName(name)) + ".dat";
    private static string EncodeSetting(string? value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    private static string DecodeSetting(string? value) => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));

    private static string NormalizeBaseUrl(string? value)
    {
        var clean = (value ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(clean, UriKind.Absolute, out var uri)) throw new InvalidOperationException("Base URL is not a valid absolute URL.");
        var path = uri.AbsolutePath.TrimEnd('/');
        foreach (var suffix in new[] { "/v1/responses/compact", "/v1/chat/completions", "/v1/responses" })
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length] + "/v1";
                break;
            }
        }
        if (path.Length == 0 || path == "/") path = "/v1";
        var builder = new UriBuilder(uri) { Path = path, Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private bool IsRealCodexRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured) && PathEquals(configured, root)) return true;
        return PathEquals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"), root);
    }

    private void AssertCodexIsStopped()
    {
        if (IsRealCodexRoot() && CodexPlatform.IsCodexRunning())
        {
            throw new InvalidOperationException("Codex 仍在运行。为避免历史数据库或会话 JSONL 冲突，请彻底退出 Codex 后再切换。");
        }
    }

    private bool HasCodexApiKeyEnvironmentOverride()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_API_KEY"))) return true;
        if (!OperatingSystem.IsWindows()) return false;
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_API_KEY", EnvironmentVariableTarget.User)) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_API_KEY", EnvironmentVariableTarget.Machine));
    }

    private EnvironmentChange CaptureAndClearCodexApiKeyEnvironmentOverride()
    {
        if (!IsRealCodexRoot() && Environment.GetEnvironmentVariable("CODEX_SWITCHER_TEST_PROCESS_ENV") != "1") return EnvironmentChange.None;
        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_API_KEY", EnvironmentVariableTarget.Machine)))
        {
            throw new InvalidOperationException("检测到系统级 CODEX_API_KEY，它会覆盖 ChatGPT 官方登录。请先移除系统级环境变量，再切换到官方登录。");
        }

        var target = OperatingSystem.IsWindows() && IsRealCodexRoot() ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;
        var originalTarget = Environment.GetEnvironmentVariable("CODEX_API_KEY", target);
        var originalProcess = Environment.GetEnvironmentVariable("CODEX_API_KEY", EnvironmentVariableTarget.Process);
        var value = string.IsNullOrWhiteSpace(originalTarget) ? originalProcess : originalTarget;
        if (string.IsNullOrWhiteSpace(value)) return EnvironmentChange.None;

        secretStore.Save(codexApiKeyEnvironmentBackupPath, value, "environment");
        Environment.SetEnvironmentVariable("CODEX_API_KEY", null, target);
        Environment.SetEnvironmentVariable("CODEX_API_KEY", null, EnvironmentVariableTarget.Process);
        return new EnvironmentChange(true, () =>
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", originalTarget, target);
            Environment.SetEnvironmentVariable("CODEX_API_KEY", originalProcess, EnvironmentVariableTarget.Process);
        });
    }

    private bool RestoreCodexApiKeyEnvironmentOverride()
    {
        if (!secretStore.Exists(codexApiKeyEnvironmentBackupPath)) return false;
        var target = OperatingSystem.IsWindows() && IsRealCodexRoot() ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_API_KEY", target))) return false;
        var value = secretStore.Read(codexApiKeyEnvironmentBackupPath, "environment");
        Environment.SetEnvironmentVariable("CODEX_API_KEY", value, target);
        Environment.SetEnvironmentVariable("CODEX_API_KEY", value, EnvironmentVariableTarget.Process);
        return true;
    }

    private static bool PathEquals(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), comparison);
    }

    private static string SafeProcessName(Process process)
    {
        try { return process.ProcessName + " #" + process.Id; }
        catch { return "Codex process"; }
    }

    private static string GetPathToken(string path)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }
}
