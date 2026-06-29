using System.Security;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;

namespace CodexApiSwitcher.Platform;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexApiSwitcher";
    private const string StartupArgument = "--startup-launch";

    internal static bool IsEnabled(string executablePath)
    {
        executablePath = Path.GetFullPath(executablePath);
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(ValueName) as string;
            return string.Equals(value?.Trim(), BuildWindowsCommand(executablePath), StringComparison.OrdinalIgnoreCase);
        }

        var path = GetStartupFilePath();
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                var expected = BuildMacProgramArguments(executablePath);
                var document = XDocument.Load(path);
                var values = document.Descendants("key")
                    .FirstOrDefault(element => element.Value == "ProgramArguments")?
                    .ElementsAfterSelf().FirstOrDefault(element => element.Name.LocalName == "array")?
                    .Elements("string").Select(element => element.Value).ToArray();
                return values is not null && values.SequenceEqual(expected, StringComparer.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        var content = File.ReadAllText(path, Encoding.UTF8);
        return content.Contains(executablePath, StringComparison.Ordinal) && content.Contains(StartupArgument, StringComparison.Ordinal);
    }

    internal static void SetEnabled(bool enabled, string executablePath)
    {
        executablePath = Path.GetFullPath(executablePath);
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath) ??
                throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");
            if (enabled) key.SetValue(ValueName, BuildWindowsCommand(executablePath), RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
            return;
        }

        var path = GetStartupFilePath();
        if (!enabled)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (OperatingSystem.IsMacOS())
        {
            var arguments = BuildMacProgramArguments(executablePath);
            var argumentXml = string.Join(string.Empty, arguments.Select(argument =>
                "<string>" + (SecurityElement.Escape(argument) ?? argument) + "</string>"));
            var plist = $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.codex-api-switcher.startup</string>
  <key>ProgramArguments</key>
  <array>{argumentXml}</array>
  <key>RunAtLoad</key><true/>
  <key>ProcessType</key><string>Interactive</string>
  <key>LimitLoadToSessionType</key><string>Aqua</string>
</dict>
</plist>
""";
            Core.AtomicFile.WriteText(path, plist);
            return;
        }

        var quoted = "\"" + executablePath.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var desktop = $"""
[Desktop Entry]
Type=Application
Name=Codex API Switcher
Comment=Start CAS in the system tray
Exec={quoted} {StartupArgument}
Terminal=false
X-GNOME-Autostart-enabled=true
""";
        Core.AtomicFile.WriteText(path, desktop);
    }

    private static string GetStartupFilePath()
    {
        var testPath = Environment.GetEnvironmentVariable("CODEX_API_SWITCHER_STARTUP_FILE");
        if (!string.IsNullOrWhiteSpace(testPath)) return Path.GetFullPath(testPath);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "LaunchAgents", "com.codex-api-switcher.startup.plist");
        }

        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(config)) config = Path.Combine(home, ".config");
        return Path.Combine(config, "autostart", "codex-api-switcher.desktop");
    }

    private static string BuildWindowsCommand(string executablePath) =>
        "\"" + executablePath.Replace("\"", "\\\"") + "\" " + StartupArgument;

    private static string[] BuildMacProgramArguments(string executablePath)
    {
        var bundle = FindContainingAppBundle(executablePath);
        return bundle.Length > 0
            ? ["/usr/bin/open", "-g", bundle, "--args", StartupArgument]
            : [executablePath, StartupArgument];
    }

    internal static string FindContainingAppBundle(string executablePath)
    {
        if (!OperatingSystem.IsMacOS()) return string.Empty;
        var directory = new FileInfo(Path.GetFullPath(executablePath)).Directory;
        while (directory is not null)
        {
            if (directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(Path.Combine(directory.FullName, "Contents", "MacOS")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return string.Empty;
    }
}
