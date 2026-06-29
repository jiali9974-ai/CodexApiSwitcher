using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;

namespace CodexApiSwitcher.Platform;

internal interface ISecretStore
{
    string BackendName { get; }
    bool Exists(string path);
    void Save(string path, string value, string purpose);
    string Read(string path, string purpose);
    void Delete(string path, string purpose);
}

internal static class SecretStoreFactory
{
    internal static ISecretStore Create(string dataDirectory)
    {
        if (OperatingSystem.IsWindows()) return new WindowsDpapiSecretStore();
        if (OperatingSystem.IsMacOS()) return new MacKeychainSecretStore();
        if (OperatingSystem.IsLinux() && CommandRunner.FindOnPath("secret-tool").Length > 0)
        {
            return new LinuxSecretServiceStore();
        }
        return new UnixProtectedFileSecretStore(Path.Combine(dataDirectory, "master.key"));
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsDpapiSecretStore : ISecretStore
{
    internal static readonly byte[] LegacyDefaultEntropy = Encoding.UTF8.GetBytes("CodexApiSwitcher-v1");
    internal static readonly byte[] LegacyProfileEntropy = Encoding.UTF8.GetBytes("CodexApiSwitcher-profile-v1");
    internal static readonly byte[] EnvironmentEntropy = Encoding.UTF8.GetBytes("CodexApiSwitcher-CODEX_API_KEY-v1");

    public string BackendName => "Windows DPAPI";
    public bool Exists(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    public void Save(string path, string value, string purpose)
    {
        var plain = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var encrypted = ProtectedData.Protect(plain, EntropyFor(purpose), DataProtectionScope.CurrentUser);
        try { Core.AtomicFile.WriteBytes(path, encrypted); }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public string Read(string path, string purpose)
    {
        var encrypted = File.ReadAllBytes(path);
        var plain = ProtectedData.Unprotect(encrypted, EntropyFor(purpose), DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plain); }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public void Delete(string path, string purpose)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static byte[] EntropyFor(string purpose) => purpose switch
    {
        "default" => LegacyDefaultEntropy,
        "profile" => LegacyProfileEntropy,
        "environment" => EnvironmentEntropy,
        _ => SHA256.HashData(Encoding.UTF8.GetBytes("CodexApiSwitcher:" + purpose))
    };
}

internal abstract class CommandSecretStore : ISecretStore
{
    protected const string ServiceName = "com.codex-api-switcher.credentials";
    public abstract string BackendName { get; }

    public bool Exists(string path)
    {
        if (!File.Exists(path)) return false;
        try { return Read(path, "probe").Length > 0; }
        catch { return false; }
    }

    public abstract void Save(string path, string value, string purpose);
    public abstract string Read(string path, string purpose);
    public abstract void Delete(string path, string purpose);

    protected static string Account(string path) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))).ToLowerInvariant();

    protected static void WriteMarker(string path, string backend) =>
        Core.AtomicFile.WriteText(path, "backend=" + backend + Environment.NewLine);
}

internal sealed class MacKeychainSecretStore : CommandSecretStore
{
    public override string BackendName => "macOS Keychain";

    public override void Save(string path, string value, string purpose)
    {
        var result = CommandRunner.Run("security", new[]
        {
            "add-generic-password", "-U", "-s", ServiceName, "-a", Account(path), "-w", value
        });
        result.EnsureSuccess("无法写入 macOS Keychain");
        WriteMarker(path, "macos-keychain");
    }

    public override string Read(string path, string purpose)
    {
        var result = CommandRunner.Run("security", new[]
        {
            "find-generic-password", "-s", ServiceName, "-a", Account(path), "-w"
        });
        result.EnsureSuccess("无法从 macOS Keychain 读取 API Key");
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    public override void Delete(string path, string purpose)
    {
        CommandRunner.Run("security", new[]
        {
            "delete-generic-password", "-s", ServiceName, "-a", Account(path)
        }, throwOnStartFailure: false);
        if (File.Exists(path)) File.Delete(path);
    }
}

internal sealed class LinuxSecretServiceStore : CommandSecretStore
{
    public override string BackendName => "Linux Secret Service";

    public override void Save(string path, string value, string purpose)
    {
        var result = CommandRunner.Run("secret-tool", new[]
        {
            "store", "--label=Codex API Switcher", "service", ServiceName, "account", Account(path)
        }, value);
        result.EnsureSuccess("无法写入 Linux Secret Service");
        WriteMarker(path, "linux-secret-service");
    }

    public override string Read(string path, string purpose)
    {
        var result = CommandRunner.Run("secret-tool", new[]
        {
            "lookup", "service", ServiceName, "account", Account(path)
        });
        result.EnsureSuccess("无法从 Linux Secret Service 读取 API Key");
        var value = result.StandardOutput.TrimEnd('\r', '\n');
        if (value.Length == 0) throw new InvalidOperationException("Linux Secret Service 中没有对应的 API Key。");
        return value;
    }

    public override void Delete(string path, string purpose)
    {
        CommandRunner.Run("secret-tool", new[]
        {
            "clear", "service", ServiceName, "account", Account(path)
        }, throwOnStartFailure: false);
        if (File.Exists(path)) File.Delete(path);
    }
}

internal sealed class UnixProtectedFileSecretStore : ISecretStore
{
    private static readonly byte[] Magic = "CAS2"u8.ToArray();
    private readonly string masterKeyPath;

    internal UnixProtectedFileSecretStore(string keyPath) => masterKeyPath = keyPath;
    public string BackendName => "Unix 0600 protected vault";
    public bool Exists(string path) => File.Exists(path) && new FileInfo(path).Length > Magic.Length + 28;

    public void Save(string path, string value, string purpose)
    {
        var key = LoadOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plain = Encoding.UTF8.GetBytes(value ?? string.Empty);
        var cipher = new byte[plain.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(purpose));
            Core.AtomicFile.WriteBytes(path, Magic.Concat(nonce).Concat(tag).Concat(cipher).ToArray());
            Restrict(path);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(cipher);
        }
    }

    public string Read(string path, string purpose)
    {
        var payload = File.ReadAllBytes(path);
        if (payload.Length <= Magic.Length + 28 || !payload.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidOperationException("凭据文件格式无效或来自其他操作系统；请重新输入 API Key。");
        }
        var key = LoadOrCreateKey();
        var plain = new byte[payload.Length - Magic.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(
                payload.AsSpan(Magic.Length, 12),
                payload.AsSpan(Magic.Length + 28),
                payload.AsSpan(Magic.Length + 12, 16),
                plain,
                Encoding.UTF8.GetBytes(purpose));
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public void Delete(string path, string purpose)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private byte[] LoadOrCreateKey()
    {
        if (File.Exists(masterKeyPath)) return File.ReadAllBytes(masterKeyPath);
        var key = RandomNumberGenerator.GetBytes(32);
        Core.AtomicFile.WriteBytes(masterKeyPath, key);
        Restrict(masterKeyPath);
        return key.ToArray();
    }

    private static void Restrict(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    internal void EnsureSuccess(string context)
    {
        if (ExitCode != 0) throw new InvalidOperationException(context + "：" + StandardError.Trim());
    }
}

internal static class CommandRunner
{
    internal static CommandResult Run(
        string fileName,
        IEnumerable<string> arguments,
        string? standardInput = null,
        bool throwOnStartFailure = true)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动命令：" + fileName);
            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10_000);
            return new CommandResult(process.ExitCode, stdout, stderr);
        }
        catch when (!throwOnStartFailure)
        {
            return new CommandResult(-1, string.Empty, string.Empty);
        }
    }

    internal static string FindOnPath(string command)
    {
        var names = OperatingSystem.IsWindows()
            ? new[] { command + ".exe", command + ".cmd", command + ".bat", command }
            : new[] { command };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            foreach (var name in names)
            {
                var path = Path.Combine(directory.Trim(), name);
                if (File.Exists(path)) return path;
            }
        }
        return string.Empty;
    }
}
