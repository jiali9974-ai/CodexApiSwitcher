using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using CodexApiSwitcher.Platform;
using CodexApiSwitcher.Views;

namespace CodexApiSwitcher;

internal sealed partial class App : Application
{
    private TrayIcon? trayIcon;
    private IClassicDesktopStyleApplicationLifetime? desktop;
    internal static AppLaunchOptions LaunchOptions { get; set; } = AppLaunchOptions.Default;
    internal static SingleInstanceCoordinator? SingleInstance { get; set; }
    internal static App? CurrentApp => Current as App;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            desktop = lifetime;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var automationWindow = LaunchOptions.RenderPath.Length > 0 ||
                                   LaunchOptions.UiSmokeReportPath.Length > 0 ||
                                   LaunchOptions.SingleInstanceSmokeReportPath.Length > 0;
            var window = new MainWindow(LaunchOptions.Root, automationWindow);
            desktop.MainWindow = window;
            BuildTray(window);
            SingleInstance?.StartListening(() => Dispatcher.UIThread.Post(ShowMainWindow));
            window.Opened += async (_, _) =>
            {
                if (LaunchOptions.RenderPath.Length > 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await Task.Delay(650);
                        await window.SaveSnapshotAsync(LaunchOptions.RenderPath);
                        Console.WriteLine("UI snapshot: " + Path.GetFullPath(LaunchOptions.RenderPath));
                        Exit();
                    });
                }
                else if (LaunchOptions.UiSmokeReportPath.Length > 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await window.RunUiSmokeTestAsync(LaunchOptions.UiSmokeReportPath);
                        Exit();
                    });
                }
                else if (LaunchOptions.SingleInstanceSmokeReportPath.Length > 0)
                {
                    window.HideToTray(showNotice: false);
                }
                else if (LaunchOptions.StartHidden)
                {
                    window.HideToTray(showNotice: false);
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal void ShowMainWindow()
    {
        if (desktop?.MainWindow is not { } window) return;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        if (window is MainWindow mainWindow) mainWindow.NoteSingleInstanceActivation();
        if (LaunchOptions.SingleInstanceSmokeReportPath.Length > 0)
        {
            var path = Path.GetFullPath(LaunchOptions.SingleInstanceSmokeReportPath);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, "PASS: existing macOS CAS instance received activation." + Environment.NewLine);
            Exit();
        }
    }

    internal void Exit()
    {
        if (desktop?.MainWindow is MainWindow window) window.AllowExit = true;
        trayIcon?.Dispose();
        trayIcon = null;
        SingleInstance?.Dispose();
        SingleInstance = null;
        desktop?.Shutdown();
    }

    private void BuildTray(MainWindow window)
    {
        var menu = new NativeMenu();
        var open = new NativeMenuItem("打开界面");
        open.Click += (_, _) => ShowMainWindow();
        var launch = new NativeMenuItem("启动 Codex");
        launch.Click += async (_, _) => await window.LaunchCodexFromTrayAsync();
        var close = new NativeMenuItem("关闭 Codex 后台");
        close.Click += async (_, _) => await window.CloseCodexFromTrayAsync();
        var exit = new NativeMenuItem("退出 CAS");
        exit.Click += (_, _) => Exit();
        menu.Items.Add(open);
        menu.Items.Add(launch);
        menu.Items.Add(close);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exit);

        trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CodexApiSwitcher/Assets/cas-logo.ico"))),
            ToolTipText = "CAS - Codex API 切换器",
            Menu = menu,
            IsVisible = true
        };
        trayIcon.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
    }
}
