using System.Diagnostics;
using CodexApiSwitcher.Core;

namespace CodexApiSwitcher.Platform;

internal static class CodexPlatform
{
    internal static string Description =>
        OperatingSystem.IsWindows() ? "Windows" :
        OperatingSystem.IsMacOS() ? "macOS" :
        OperatingSystem.IsLinux() ? "Linux" : Environment.OSVersion.Platform.ToString();

    internal static LaunchTarget ResolveLaunchTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            var appId = FindCodexAppUserModelId();
            if (appId.Length > 0)
            {
                return new LaunchTarget("shell:AppsFolder\\" + appId, "explorer.exe", new[] { "shell:AppsFolder\\" + appId }, false);
            }
            var desktop = FindWindowsDesktopExecutable();
            if (desktop.Length > 0) return Executable(desktop);
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var app in new[] { "/Applications/Codex.app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Codex.app") })
            {
                if (Directory.Exists(app)) return new LaunchTarget(app, "/usr/bin/open", new[] { app }, false);
            }
            var terminalCommand = CommandRunner.FindOnPath("codex");
            if (terminalCommand.Length > 0) return TerminalExecutable(terminalCommand);
        }

        var command = CommandRunner.FindOnPath("codex");
        if (command.Length > 0) return Executable(command);
        throw new FileNotFoundException("未找到 Codex 桌面应用或 codex 命令。");
    }

    internal static List<Process> GetCodexProcesses()
    {
        var current = Environment.ProcessId;
        var results = new Dictionary<int, Process>();
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                foreach (var process in Process.GetProcesses())
                {
                    string name;
                    try { name = process.ProcessName; }
                    catch { process.Dispose(); continue; }
                    var matches = name.Equals("Codex", StringComparison.OrdinalIgnoreCase) ||
                                  name.Equals("codex-cli", StringComparison.OrdinalIgnoreCase) ||
                                  name.StartsWith("Codex Helper", StringComparison.OrdinalIgnoreCase);
                    if (matches && process.Id != current && results.TryAdd(process.Id, process)) continue;
                    process.Dispose();
                }
            }
            catch { }
            return results.Values.ToList();
        }

        var names = new[] { "Codex", "codex", "codex-cli" };
        foreach (var name in names)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    if (process.Id == current || !results.TryAdd(process.Id, process)) process.Dispose();
                }
            }
            catch { }
        }
        return results.Values.ToList();
    }

    internal static bool IsCodexRunning()
    {
        var processes = GetCodexProcesses();
        var running = processes.Count > 0;
        foreach (var process in processes) process.Dispose();
        return running;
    }

    internal static bool CloseProcess(Process process)
    {
        if (process.HasExited) return false;
        try
        {
            if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow() && process.WaitForExit(4000)) return true;
        }
        catch { }
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(4000);
        }
        return true;
    }

    private static LaunchTarget Executable(string path) => new(path, path, Array.Empty<string>(), false);

    private static LaunchTarget TerminalExecutable(string path) => new(path + "（Terminal）", path, Array.Empty<string>(), false, true);

    private static string FindCodexAppUserModelId()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_APP_ID");
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            foreach (var directory in Directory.GetDirectories(windowsApps, "OpenAI.Codex_*__*").OrderDescending())
            {
                var name = Path.GetFileName(directory);
                var separator = name.LastIndexOf("__", StringComparison.Ordinal);
                if (separator > 0) return "OpenAI.Codex_" + name[(separator + 2)..] + "!App";
            }
        }
        catch { }
        return string.Empty;
    }

    private static string FindWindowsDesktopExecutable()
    {
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            foreach (var directory in Directory.GetDirectories(windowsApps, "OpenAI.Codex_*__*").OrderDescending())
            {
                var candidate = Path.Combine(directory, "app", "Codex.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }
        return string.Empty;
    }
}
