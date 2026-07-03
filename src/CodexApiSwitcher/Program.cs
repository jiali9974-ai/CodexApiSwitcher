using Avalonia;
using CodexApiSwitcher.Core;
using CodexApiSwitcher.Platform;
using System.Runtime.InteropServices;

namespace CodexApiSwitcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            if (ShouldRunCommand(options)) return RunCommand(options);
            InitializeMacDiagnostics();
            var instanceScope = GetOption(options, "--instance-scope", string.Empty);
            if (instanceScope.Length > 0) Environment.SetEnvironmentVariable("CODEX_API_SWITCHER_INSTANCE_SCOPE", instanceScope);
            var isStartupLaunch = options.ContainsKey("--startup-launch");
            var isAutomationLaunch = options.ContainsKey("--render-ui") || options.ContainsKey("--ui-smoke-test");
            using var singleInstance = isAutomationLaunch ? null : SingleInstanceCoordinator.Acquire();
            if (singleInstance is { IsPrimary: false })
            {
                if (!isStartupLaunch) singleInstance.NotifyExisting();
                return 0;
            }

            App.LaunchOptions = new AppLaunchOptions(
                isStartupLaunch,
                GetOption(options, "--root", GetDefaultRoot()),
                GetOption(options, "--render-ui", string.Empty),
                GetOption(options, "--ui-smoke-test", string.Empty),
                GetOption(options, "--single-instance-smoke-report", string.Empty));
            App.SingleInstance = singleInstance;
            HideWindowsConsoleForGui();
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return Environment.ExitCode;
        }
        catch (Exception ex)
        {
            LogMacFailure(ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();

    private static void HideWindowsConsoleForGui()
    {
        if (!OperatingSystem.IsWindows()) return;
        var console = GetConsoleWindow();
        if (console == IntPtr.Zero) return;
        ShowWindow(console, 0);
        FreeConsole();
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private static void InitializeMacDiagnostics()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var path = GetMacLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
            {
                File.Move(path, path + ".old", true);
            }
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CAS {Environment.ProcessPath} started.{Environment.NewLine}");
            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
                LogMacFailure(eventArgs.ExceptionObject as Exception ?? new Exception(eventArgs.ExceptionObject?.ToString()));
            TaskScheduler.UnobservedTaskException += (_, eventArgs) => LogMacFailure(eventArgs.Exception);
        }
        catch { }
    }

    private static void LogMacFailure(Exception ex)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var path = GetMacLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}");
        }
        catch { }
    }

    private static string GetMacLogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "CodexApiSwitcher.log");

    internal static string GetDefaultRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return configured;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    private static bool ShouldRunCommand(Dictionary<string, string> options) => options.Keys.Any(key => key is
        "--emit-token" or "--emit-profile-token" or "--status" or "--switch-third-party" or
        "--save-profile" or "--delete-profile" or "--list-profiles" or "--switch-official" or
        "--rollback" or "--reset-config" or "--repair-sidebar" or "--repair-reconnecting" or "--detect-proxy" or "--armor-status" or
        "--enable-armor" or "--restore-armor" or "--codex-launch-plan" or
        "--launch-codex" or "--close-codex" or "--normalize-hotkey" or "--save-hotkey" or
        "--show-hotkey" or "--normalize-mouse-button" or "--save-mouse-button" or
        "--show-mouse-button" or "--enable-startup" or "--disable-startup" or
        "--show-startup" or "--platform-info");

    private static int RunCommand(Dictionary<string, string> options)
    {
        var root = GetOption(options, "--root", GetDefaultRoot());
        var executable = CurrentExecutable.Resolve();
        var service = new SwitcherService(root, executable);

        if (options.ContainsKey("--platform-info"))
        {
            Console.WriteLine($"OS={CodexPlatform.Description}; SecretStore={service.SecretBackendName}; Runtime={Environment.Version}");
        }
        else if (options.ContainsKey("--emit-token")) Console.Out.Write(service.ReadToken());
        else if (options.ContainsKey("--emit-profile-token")) Console.Out.Write(service.ReadThirdPartyProfileToken(RequireOption(options, "--name")));
        else if (options.ContainsKey("--status")) Console.WriteLine(service.GetStatus().ToDisplayString());
        else if (options.ContainsKey("--switch-third-party"))
        {
            var key = GetOption(options, "--key", string.Empty);
            var profile = GetOption(options, "--profile", string.Empty);
            if (key.Length == 0 && profile.Length == 0) key = RequireOption(options, "--key");
            var outcome = service.SwitchToThirdParty(
                RequireOption(options, "--url"),
                RequireOption(options, "--model"),
                key,
                profile,
                options.ContainsKey("--compat-mode"));
            Console.WriteLine("Switched to third-party Responses API.");
            if (outcome.HasNotice) Console.WriteLine(outcome.ToCommandLineNotice());
        }
        else if (options.ContainsKey("--save-profile"))
        {
            var profile = service.SaveThirdPartyProfile(
                RequireOption(options, "--name"), RequireOption(options, "--url"),
                RequireOption(options, "--model"), RequireOption(options, "--key"));
            Console.WriteLine(profile.Name + "|" + profile.BaseUrl + "|" + profile.Model);
        }
        else if (options.ContainsKey("--delete-profile"))
        {
            service.DeleteThirdPartyProfile(RequireOption(options, "--name"));
            Console.WriteLine("Deleted profile.");
        }
        else if (options.ContainsKey("--list-profiles"))
        {
            foreach (var profile in service.LoadThirdPartyProfiles()) Console.WriteLine(profile.Name + "|" + profile.BaseUrl + "|" + profile.Model);
        }
        else if (options.ContainsKey("--switch-official"))
        {
            var outcome = service.SwitchToOfficial(GetOption(options, "--model", "gpt-5.5"));
            Console.WriteLine("Switched to official OpenAI login.");
            if (outcome.HasNotice) Console.WriteLine(outcome.ToCommandLineNotice());
        }
        else if (options.ContainsKey("--rollback")) { service.Rollback(); Console.WriteLine("Restored the latest backup."); }
        else if (options.ContainsKey("--reset-config")) { service.ResetModelConfiguration(GetOption(options, "--model", "gpt-5.5")); Console.WriteLine("Rebuilt the model configuration for official OpenAI login."); }
        else if (options.ContainsKey("--repair-sidebar")) Console.WriteLine(service.RepairConversationIndex());
        else if (options.ContainsKey("--detect-proxy")) Console.WriteLine(service.DetectLocalProxyEndpoint());
        else if (options.ContainsKey("--repair-reconnecting")) Console.WriteLine(service.RepairReconnectingProxy().ToDisplayString());
        else if (options.ContainsKey("--armor-status")) Console.WriteLine(service.GetArmorStatus().ToDisplayString());
        else if (options.ContainsKey("--enable-armor")) Console.WriteLine(service.EnableArmor());
        else if (options.ContainsKey("--restore-armor")) Console.WriteLine(service.RestoreArmor());
        else if (options.ContainsKey("--codex-launch-plan")) Console.WriteLine(service.GetCodexLaunchPlan());
        else if (options.ContainsKey("--launch-codex")) Console.WriteLine(service.LaunchCodex());
        else if (options.ContainsKey("--close-codex")) Console.WriteLine(service.CloseCodexProcesses(options.ContainsKey("--dry-run")));
        else if (options.ContainsKey("--normalize-hotkey")) Console.WriteLine(HotkeySetting.Parse(RequireOption(options, "--hotkey")).ToDisplayString());
        else if (options.ContainsKey("--save-hotkey"))
        {
            var value = HotkeySetting.Parse(RequireOption(options, "--hotkey")).ToDisplayString();
            service.SaveOpenHotkey(value);
            Console.WriteLine(value);
        }
        else if (options.ContainsKey("--show-hotkey")) Console.WriteLine(service.LoadSettings().GetOpenHotkey());
        else if (options.ContainsKey("--normalize-mouse-button")) Console.WriteLine(MouseButtonSetting.Parse(RequireOption(options, "--mouse-button")).ToDisplayString());
        else if (options.ContainsKey("--save-mouse-button"))
        {
            var value = MouseButtonSetting.Parse(RequireOption(options, "--mouse-button")).ToDisplayString();
            service.SaveOpenMouseButton(value);
            Console.WriteLine(value);
        }
        else if (options.ContainsKey("--show-mouse-button")) Console.WriteLine(service.LoadSettings().GetOpenMouseButton());
        else if (options.ContainsKey("--enable-startup")) { StartupManager.SetEnabled(true, executable); Console.WriteLine("Enabled"); }
        else if (options.ContainsKey("--disable-startup")) { StartupManager.SetEnabled(false, executable); Console.WriteLine("Disabled"); }
        else if (options.ContainsKey("--show-startup")) Console.WriteLine(StartupManager.IsEnabled(executable) ? "Enabled" : "Disabled");
        else throw new InvalidOperationException("Unknown command.");
        return 0;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var key = args[index];
            var value = "true";
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) value = args[++index];
            result[key] = value;
        }
        return result;
    }

    private static string RequireOption(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException("Missing required option: " + key);

    private static string GetOption(Dictionary<string, string> options, string key, string fallback) =>
        options.TryGetValue(key, out var value) ? value : fallback;
}

internal sealed record AppLaunchOptions(
    bool StartHidden,
    string Root,
    string RenderPath,
    string UiSmokeReportPath,
    string SingleInstanceSmokeReportPath)
{
    internal static readonly AppLaunchOptions Default = new(false, Program.GetDefaultRoot(), string.Empty, string.Empty, string.Empty);
}
