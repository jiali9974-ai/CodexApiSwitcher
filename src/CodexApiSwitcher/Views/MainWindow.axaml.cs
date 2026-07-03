using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CodexApiSwitcher.Core;
using CodexApiSwitcher.Platform;
using System.Diagnostics;
using System.Text;

namespace CodexApiSwitcher.Views;

internal sealed partial class MainWindow : Window
{
    private static readonly IBrush Ink = Brush.Parse("#172033");
    private static readonly IBrush Muted = Brush.Parse("#586376");
    private static readonly IBrush Blue = Brush.Parse("#2457D6");
    private static readonly IBrush Green = Brush.Parse("#15803D");
    private static readonly IBrush Amber = Brush.Parse("#92400E");
    private static readonly IBrush Red = Brush.Parse("#B42318");
    private static readonly IBrush White = Brush.Parse("#FFFFFF");
    private static readonly IBrush BlueSurface = Brush.Parse("#EEF4FF");
    private static readonly IBrush GreenSurface = Brush.Parse("#ECFDF3");
    private static readonly IBrush AmberSurface = Brush.Parse("#FFFBEB");
    private static readonly IBrush RedSurface = Brush.Parse("#FFF1F0");

    private readonly TextBox rootBox;
    private readonly ComboBox profileBox;
    private readonly TextBox urlBox;
    private readonly TextBox thirdPartyModelBox;
    private readonly TextBox officialModelBox;
    private readonly TextBox keyBox;
    private readonly TextBox hotkeyBox;
    private readonly ComboBox mouseButtonBox;
    private readonly CheckBox startupCheckBox;
    private readonly CheckBox compatibilityCheckBox;
    private readonly CheckBox showKeyCheckBox;
    private readonly Border statusBorder;
    private readonly TextBlock statusText;
    private readonly TextBlock keyStateText;
    private readonly TextBlock armorStateText;
    private readonly TextBlock backupLocationText;
    private readonly TextBlock footerText;
    private readonly Button browseButton;
    private readonly Button saveProfileButton;
    private readonly Button deleteProfileButton;
    private readonly Button thirdPartyButton;
    private readonly Button officialButton;
    private readonly Button rollbackButton;
    private readonly Button repairButton;
    private readonly Button resetConfigButton;
    private readonly Button launchCodexButton;
    private readonly Button closeCodexButton;
    private readonly Button armorButton;
    private readonly Button restoreArmorButton;
    private readonly Button hotkeyButton;
    private readonly List<Control> operationControls = new();
    private List<ThirdPartyProfile> currentProfiles = new();
    private bool loading;
    private bool loadingProfiles;
    private bool loadingStartup;
    private bool capturingHotkey;
    private bool snapshotMode;
    private bool codexProcessButtonsDryRun;
    private bool rootLoading;
    private GlobalInputHook? globalInput;

    internal MainWindow(string initialRoot, bool renderSnapshot)
    {
        AvaloniaXamlLoader.Load(this);
        snapshotMode = renderSnapshot;
        rootBox = Find<TextBox>("RootBox");
        profileBox = Find<ComboBox>("ProfileBox");
        urlBox = Find<TextBox>("UrlBox");
        thirdPartyModelBox = Find<TextBox>("ThirdPartyModelBox");
        officialModelBox = Find<TextBox>("OfficialModelBox");
        keyBox = Find<TextBox>("KeyBox");
        hotkeyBox = Find<TextBox>("HotkeyBox");
        mouseButtonBox = Find<ComboBox>("MouseButtonBox");
        startupCheckBox = Find<CheckBox>("StartupCheckBox");
        compatibilityCheckBox = Find<CheckBox>("CompatibilityCheckBox");
        showKeyCheckBox = Find<CheckBox>("ShowKeyCheckBox");
        statusBorder = Find<Border>("StatusBorder");
        statusText = Find<TextBlock>("StatusText");
        keyStateText = Find<TextBlock>("KeyStateText");
        armorStateText = Find<TextBlock>("ArmorStateText");
        backupLocationText = Find<TextBlock>("BackupLocationText");
        footerText = Find<TextBlock>("FooterText");
        browseButton = Find<Button>("BrowseButton");
        saveProfileButton = Find<Button>("SaveProfileButton");
        deleteProfileButton = Find<Button>("DeleteProfileButton");
        thirdPartyButton = Find<Button>("ThirdPartyButton");
        officialButton = Find<Button>("OfficialButton");
        rollbackButton = Find<Button>("RollbackButton");
        repairButton = Find<Button>("RepairButton");
        resetConfigButton = Find<Button>("ResetConfigButton");
        launchCodexButton = Find<Button>("LaunchCodexButton");
        closeCodexButton = Find<Button>("CloseCodexButton");
        armorButton = Find<Button>("ArmorButton");
        restoreArmorButton = Find<Button>("RestoreArmorButton");
        hotkeyButton = Find<Button>("HotkeyButton");

        operationControls.AddRange(new Control[]
        {
            profileBox, urlBox, thirdPartyModelBox, officialModelBox, keyBox, compatibilityCheckBox,
            browseButton, saveProfileButton, deleteProfileButton, thirdPartyButton, officialButton,
            rollbackButton, repairButton, resetConfigButton, launchCodexButton, closeCodexButton,
            armorButton, restoreArmorButton, hotkeyButton, mouseButtonBox, startupCheckBox
        });

        rootBox.Text = initialRoot;
        mouseButtonBox.ItemsSource = new[] { "关闭", "侧键1", "侧键2" };
        WireEvents();
        ConfigurePlatformText();
        Opened += async (_, _) =>
        {
            await LoadRootSettingsAsync();
            LoadStartupAndInputSettings();
            if (!snapshotMode) StartGlobalInputHook();
        };
    }

    internal bool AllowExit { get; set; }

    internal void HideToTray(bool showNotice = true)
    {
        Hide();
        if (showNotice) SetStatus(OperatingSystem.IsMacOS()
            ? "CAS 仍在后台运行；可通过菜单栏图标、Dock 图标或再次双击 App 打开。"
            : "CAS 仍在后台运行；点击托盘图标或使用快捷键可重新打开。", Muted, White);
    }

    internal async Task LaunchCodexFromTrayAsync() => await RunActionAsync(launchCodexButton, "正在启动 Codex", async service =>
    {
        var dryRun = codexProcessButtonsDryRun;
        var result = await Task.Run(() => dryRun ? service.GetCodexLaunchPlan() : service.LaunchCodex());
        await DialogWindow.ShowMessageAsync(this, "启动 Codex", result);
    });

    internal async Task CloseCodexFromTrayAsync() => await RunActionAsync(closeCodexButton, "正在关闭 Codex 后台", async service =>
    {
        var dryRun = codexProcessButtonsDryRun;
        var result = await Task.Run(() => service.CloseCodexProcesses(dryRun));
        await DialogWindow.ShowMessageAsync(this, "关闭 Codex 后台", result);
    });

    internal void NoteSingleInstanceActivation()
    {
        SetStatus("检测到 CAS 已在后台运行，已打开现有窗口。", Green, GreenSurface);
    }

    internal async Task SaveSnapshotAsync(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height)));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(this);
        await using var stream = File.Create(path);
        bitmap.Save(stream);
    }

    private T Find<T>(string name) where T : Control => this.FindControl<T>(name) ?? throw new InvalidOperationException("UI control not found: " + name);

    private void WireEvents()
    {
        browseButton.Click += BrowseRootAsync;
        saveProfileButton.Click += async (_, _) => await SaveProfileAsync();
        deleteProfileButton.Click += async (_, _) => await DeleteProfileAsync();
        thirdPartyButton.Click += async (_, _) => await SwitchThirdPartyAsync();
        officialButton.Click += async (_, _) => await SwitchOfficialAsync();
        rollbackButton.Click += async (_, _) => await RollbackAsync();
        repairButton.Click += async (_, _) => await RepairSidebarAsync();
        resetConfigButton.Click += async (_, _) => await ResetConfigAsync();
        launchCodexButton.Click += async (_, _) => await LaunchCodexFromTrayAsync();
        closeCodexButton.Click += async (_, _) => await CloseCodexFromTrayAsync();
        armorButton.Click += async (_, _) => await EnableArmorAsync();
        restoreArmorButton.Click += async (_, _) => await RestoreArmorAsync();
        hotkeyButton.Click += (_, _) => BeginHotkeyCapture();
        profileBox.SelectionChanged += (_, _) => ProfileSelectionChanged();
        mouseButtonBox.SelectionChanged += (_, _) => MouseButtonSelectionChanged();
        startupCheckBox.IsCheckedChanged += (_, _) => StartupSelectionChanged();
        compatibilityCheckBox.IsCheckedChanged += (_, _) => CompatibilitySelectionChanged();
        showKeyCheckBox.IsCheckedChanged += (_, _) => keyBox.PasswordChar = showKeyCheckBox.IsChecked == true ? '\0' : '●';
        rootBox.LostFocus += async (_, _) => await LoadRootSettingsAsync();
        keyBox.TextChanged += (_, _) => UpdateKeyState();
        KeyDown += CaptureHotkey;
        Closing += (_, args) =>
        {
            if (!AllowExit && !snapshotMode)
            {
                if (OperatingSystem.IsMacOS())
                {
                    AllowExit = true;
                    App.CurrentApp?.Exit();
                    return;
                }
                args.Cancel = true;
                HideToTray();
            }
        };
        Closed += (_, _) => globalInput?.Dispose();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty && WindowState == WindowState.Minimized && !snapshotMode && !OperatingSystem.IsMacOS()) HideToTray();
        };
    }

    private void ConfigurePlatformText()
    {
        footerText.Text = CodexPlatform.Description switch
        {
            "macOS" => "切换完成后请重新打开 Codex。红色关闭按钮会退出 CAS；最小化会留在 Dock。首次使用全局快捷键时，macOS 可能要求授予“辅助功能”权限。",
            "Linux" => "切换完成后请重新打开 Codex。全局快捷键需要 X11；Wayland 会保留设置但可能无法监听。开机启动使用 XDG Autostart。",
            _ => "切换完成后请重新打开 Codex。快捷键和鼠标侧键都支持打开/收回后台。开机启动会直接驻留托盘。"
        };
    }

    private SwitcherService GetService() => new(rootBox.Text?.Trim() ?? string.Empty, CurrentExecutable.Resolve());

    private async Task LoadRootSettingsAsync()
    {
        await LoadRootSettingsAsync(false);
    }

    private async Task LoadRootSettingsAsync(bool force)
    {
        if (loading && !force) return;
        if (rootLoading) return;
        rootLoading = true;
        var rootSnapshot = rootBox.Text?.Trim() ?? string.Empty;
        backupLocationText.Text = "备份位置：" + Path.Combine(rootSnapshot, "config-switcher-backups") + "；" + Path.Combine(rootSnapshot, "history_sync_backups");
        SetStatus("正在后台读取 Codex 配置...", Blue, BlueSurface);
        SetControlsEnabled(false);
        try
        {
            var loaded = await Task.Run(() =>
            {
                var service = new SwitcherService(rootSnapshot, CurrentExecutable.Resolve());
                return (service.GetStatus(), service.GetArmorStatus(), service.LoadSettings(), service.HasStoredToken(), service.LoadThirdPartyProfiles(), service.SecretBackendName);
            });
            if (!string.Equals(rootSnapshot, rootBox.Text?.Trim(), PathComparison)) return;
            var (status, armorStatus, settings, storedToken, profiles, backend) = loaded;
            urlBox.Text = settings.BaseUrl.Length > 0 ? settings.BaseUrl : status.BaseUrl.Length > 0 ? status.BaseUrl : "https://api.example.com";
            thirdPartyModelBox.Text = settings.ThirdPartyModel.Length > 0 ? settings.ThirdPartyModel : status.Model.Length > 0 ? status.Model : "gpt-5.5";
            officialModelBox.Text = settings.OfficialModel.Length > 0 ? settings.OfficialModel : "gpt-5.5";
            compatibilityCheckBox.IsChecked = false;
            keyBox.Text = string.Empty;
            RefreshProfiles(profiles, string.Empty);
            keyStateText.Text = storedToken ? $"已通过 {backend} 保存默认 Key；留空即可继续使用。" : $"尚未保存 Key。首次切换时将使用 {backend} 安全保存。";
            keyStateText.Foreground = storedToken ? Green : Amber;
            armorStateText.Text = armorStatus.ToDisplayString();
            armorStateText.Foreground = armorStatus.IsEnabled ? Green : Muted;
            SetStatus("当前状态：" + status.ToDisplayString(), status.IsThirdParty ? Blue : Ink, status.IsThirdParty ? BlueSurface : White);
        }
        catch (Exception ex)
        {
            SetStatus("无法读取配置：" + ex.Message, Red, RedSurface);
            armorStateText.Text = "破甲状态：无法读取。";
            armorStateText.Foreground = Red;
        }
        finally
        {
            rootLoading = false;
            SetControlsEnabled(true);
        }
    }

    private void LoadStartupAndInputSettings()
    {
        try
        {
            var service = GetService();
            var settings = service.LoadSettings();
            loadingStartup = true;
            hotkeyBox.Text = settings.GetOpenHotkey();
            mouseButtonBox.SelectedIndex = MouseButtonSetting.ParseOrDefault(settings.OpenMouseButton).ToComboIndex();
            startupCheckBox.IsChecked = StartupManager.IsEnabled(CurrentExecutable.Resolve());
            loadingStartup = false;
        }
        catch
        {
            hotkeyBox.Text = HotkeySetting.Default.ToDisplayString();
            mouseButtonBox.SelectedIndex = 0;
            loadingStartup = false;
        }
    }

    private void RefreshProfiles(List<ThirdPartyProfile> profiles, string preferred)
    {
        loadingProfiles = true;
        currentProfiles = profiles;
        profileBox.ItemsSource = new[] { "不使用档案" }.Concat(profiles.Select(profile => profile.Name + "  ·  " + profile.BaseUrl)).ToArray();
        var index = profiles.FindIndex(profile => string.Equals(profile.Name, preferred, StringComparison.OrdinalIgnoreCase));
        profileBox.SelectedIndex = index >= 0 ? index + 1 : 0;
        deleteProfileButton.IsEnabled = index >= 0;
        loadingProfiles = false;
    }

    private ThirdPartyProfile? SelectedProfile => profileBox.SelectedIndex > 0 && profileBox.SelectedIndex <= currentProfiles.Count ? currentProfiles[profileBox.SelectedIndex - 1] : null;

    private void ProfileSelectionChanged()
    {
        if (loadingProfiles) return;
        var profile = SelectedProfile;
        deleteProfileButton.IsEnabled = profile is not null;
        if (profile is null) { UpdateKeyState(); return; }
        urlBox.Text = profile.BaseUrl;
        thirdPartyModelBox.Text = profile.Model;
        keyBox.Text = string.Empty;
        UpdateKeyState();
    }

    private void UpdateKeyState()
    {
        if (!string.IsNullOrWhiteSpace(keyBox.Text))
        {
            keyStateText.Text = SelectedProfile is { } profile ? $"将更新档案「{profile.Name}」的加密 Key。" : "将更新默认保存的加密 Key。";
            keyStateText.Foreground = Green;
        }
        else if (SelectedProfile is { } selected)
        {
            keyStateText.Text = $"已选择档案「{selected.Name}」；留空将使用该档案的加密 Key。";
            keyStateText.Foreground = Green;
        }
    }

    private async void BrowseRootAsync(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择包含 config.toml 的 Codex 根目录",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        rootBox.Text = folders[0].Path.LocalPath;
        await LoadRootSettingsAsync();
        LoadStartupAndInputSettings();
    }

    private async Task SaveProfileAsync()
    {
        await RunActionAsync(saveProfileButton, "正在保存中转站档案", async service =>
        {
            var key = keyBox.Text ?? string.Empty;
            var url = urlBox.Text ?? string.Empty;
            var model = thirdPartyModelBox.Text ?? string.Empty;
            var name = SelectedProfile?.Name ?? GetProfileName(url);
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("保存档案时需要填写 API Key；之后切换时可以留空。");
            await Task.Run(() => service.SaveThirdPartyProfile(name, url, model, key));
            keyBox.Text = string.Empty;
            RefreshProfiles(await Task.Run(service.LoadThirdPartyProfiles), name);
            await DialogWindow.ShowMessageAsync(this, "档案已保存", $"已保存中转站档案「{name}」。下次可从下拉框直接选择。");
        });
    }

    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile) return;
        if (!await DialogWindow.ConfirmAsync(this, "删除中转站档案", $"确定删除中转站档案「{profile.Name}」吗？对应的本机加密 Key 也会删除。")) return;
        await RunActionAsync(deleteProfileButton, "正在删除中转站档案", async service =>
        {
            await Task.Run(() => service.DeleteThirdPartyProfile(profile.Name));
            RefreshProfiles(await Task.Run(service.LoadThirdPartyProfiles), string.Empty);
        });
    }

    private async Task SwitchThirdPartyAsync()
    {
        await RunActionAsync(thirdPartyButton, "正在切换到第三方 API", async service =>
        {
            var url = urlBox.Text ?? string.Empty;
            var model = thirdPartyModelBox.Text ?? string.Empty;
            var key = keyBox.Text ?? string.Empty;
            var profile = SelectedProfile?.Name ?? string.Empty;
            var compatibilityMode = compatibilityCheckBox.IsChecked == true;
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("请填写第三方 Base URL 和模型名称。");
            if (string.IsNullOrWhiteSpace(key) && profile.Length == 0 && !service.HasStoredToken()) throw new InvalidOperationException("首次切换第三方 API 时必须填写 API Key。");
            var outcome = await Task.Run(() => service.SwitchToThirdParty(url, model, key, profile, compatibilityMode));
            keyBox.Text = string.Empty;
            await DialogWindow.ShowMessageAsync(this, "切换完成", "已切换到第三方 Responses API。" + outcome.ToUserNotice() + "\n\n请重新打开 Codex。");
        });
    }

    private async Task SwitchOfficialAsync()
    {
        await RunActionAsync(officialButton, "正在切换到官方登录", async service =>
        {
            var model = officialModelBox.Text ?? string.Empty;
            var outcome = await Task.Run(() => service.SwitchToOfficial(model));
            await DialogWindow.ShowMessageAsync(this, "切换完成", "已切换到官方 OpenAI 登录。" + outcome.ToUserNotice() + "\n\n第三方 Key 仍以加密形式保留，auth.json 未被修改。\n请重新打开 Codex。");
        });
    }

    private async Task RollbackAsync() => await RunActionAsync(rollbackButton, "正在恢复最近备份", async service =>
    {
        await Task.Run(service.Rollback);
        await DialogWindow.ShowMessageAsync(this, "恢复完成", "已恢复最近一次备份。\n\n请彻底退出并重新打开 Codex。");
    });

    private async Task RepairSidebarAsync()
    {
        if (!await DialogWindow.ConfirmAsync(this, "确认修复会话列表", "此操作会自动识别并备份新旧状态数据库，然后恢复顶层用户会话的可见标记。不会改动会话 JSONL、记忆文件或 auth.json。\n\n请先彻底退出 Codex，再继续。")) return;
        await RunActionAsync(repairButton, "正在修复会话列表", async service =>
        {
            var result = await Task.Run(service.RepairConversationIndex);
            await DialogWindow.ShowMessageAsync(this, "修复完成", result + "\n\n请重新打开 Codex 检查侧栏。");
        });
    }

    private async Task ResetConfigAsync()
    {
        if (!await DialogWindow.ConfirmAsync(this, "确认恢复基础配置", "将先完整备份 config.toml，然后重建官方模型配置。\n\n会恢复 model_provider 和 model，移除损坏的 custom provider 段；MCP、插件、沙箱、记忆、会话和 auth.json 均保持不变。")) return;
        await RunActionAsync(resetConfigButton, "正在恢复基础配置", async service =>
        {
            var model = officialModelBox.Text ?? "gpt-5.5";
            await Task.Run(() => service.ResetModelConfiguration(model));
            await DialogWindow.ShowMessageAsync(this, "恢复完成", "基础模型配置已恢复为官方 OpenAI 登录。原 config.toml 已备份，MCP 和其他无关配置已保留。");
        });
    }

    private async Task EnableArmorAsync()
    {
        try
        {
            var currentService = GetService();
            if (!currentService.GetStatus().IsThirdParty && currentService.ShouldShowArmorThirdPartyReminder())
            {
                currentService.MarkArmorThirdPartyReminderShown();
                SetStatus("当前是官方登录。建议先切换到第三方 API 后再破甲；如果仍想在官方状态启用，请再次点击一键破甲。", Amber, AmberSurface);
                await DialogWindow.ShowMessageAsync(this, "建议先切换第三方 API", "当前 Codex 仍是官方 OpenAI 登录状态。\n\n建议先切换到第三方 API 后再执行一键破甲。\n\n如果你仍然想在官方状态启用破甲，请再次点击“一键破甲”。此提醒只显示一次。");
                await LoadRootSettingsAsync(true);
                return;
            }
        }
        catch (Exception ex)
        {
            SetStatus("操作失败：" + ex.Message, Red, RedSurface);
            await DialogWindow.ShowMessageAsync(this, "操作失败", ex.Message);
            return;
        }
        if (!await DialogWindow.ConfirmAsync(this, "确认一键破甲", "将先备份 config.toml，然后写入 GPT-5.5 破甲指令文件，并设置 model_instructions_file。\n\n请先彻底退出 Codex，再继续。")) return;
        await RunActionAsync(armorButton, "正在启用一键破甲", async service =>
        {
            var result = await Task.Run(service.EnableArmor);
            await DialogWindow.ShowMessageAsync(this, "破甲完成", result + "\n\n请重新打开 Codex。");
        });
    }

    private async Task RestoreArmorAsync()
    {
        if (!await DialogWindow.ConfirmAsync(this, "确认一键还原", "将先备份 config.toml，然后恢复破甲前的 model_instructions_file 配置，并删除 CAS 写入的破甲指令文件。\n\n请先彻底退出 Codex，再继续。")) return;
        await RunActionAsync(restoreArmorButton, "正在还原破甲配置", async service =>
        {
            var result = await Task.Run(service.RestoreArmor);
            await DialogWindow.ShowMessageAsync(this, "还原完成", result + "\n\n请重新打开 Codex。");
        });
    }

    private async Task RunActionAsync(Button button, string busyText, Func<SwitcherService, Task> action)
    {
        if (loading) return;
        loading = true;
        var original = button.Content;
        SetControlsEnabled(false);
        button.Content = "正在处理...";
        SetStatus(busyText + "，请稍候。", Blue, BlueSurface);
        try
        {
            await action(GetService());
            await LoadRootSettingsAsync(true);
        }
        catch (Exception ex)
        {
            SetStatus("操作失败：" + ex.Message, Red, RedSurface);
            await DialogWindow.ShowMessageAsync(this, "操作失败", ex.Message);
        }
        finally
        {
            button.Content = original;
            loading = false;
            SetControlsEnabled(true);
        }
    }

    private void BeginHotkeyCapture()
    {
        capturingHotkey = true;
        hotkeyBox.Text = "请按组合键...";
        hotkeyBox.Focus();
        SetStatus("请按下新的打开界面快捷键，例如 Ctrl+Alt+C；按 Esc 取消。", Blue, BlueSurface);
    }

    private void CaptureHotkey(object? sender, KeyEventArgs args)
    {
        if (!capturingHotkey) return;
        args.Handled = true;
        if (args.Key == Key.Escape)
        {
            capturingHotkey = false;
            hotkeyBox.Text = GetService().LoadSettings().GetOpenHotkey();
            SetStatus("已取消快捷键设置。", Muted, White);
            return;
        }
        if (args.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        try
        {
            var setting = HotkeySetting.FromParts(
                args.KeyModifiers.HasFlag(KeyModifiers.Control),
                args.KeyModifiers.HasFlag(KeyModifiers.Alt),
                args.KeyModifiers.HasFlag(KeyModifiers.Shift),
                args.KeyModifiers.HasFlag(KeyModifiers.Meta),
                args.Key.ToString());
            GetService().SaveOpenHotkey(setting.ToDisplayString());
            hotkeyBox.Text = setting.ToDisplayString();
            capturingHotkey = false;
            globalInput?.Update(setting, MouseButtonSetting.FromComboIndex(mouseButtonBox.SelectedIndex));
            SetStatus("打开界面快捷键已保存：" + setting.ToDisplayString(), Green, GreenSurface);
        }
        catch (Exception ex)
        {
            capturingHotkey = false;
            SetStatus("快捷键设置失败：" + ex.Message, Red, RedSurface);
        }
    }

    private void MouseButtonSelectionChanged()
    {
        if (loadingProfiles || loadingStartup || mouseButtonBox.SelectedIndex < 0) return;
        try
        {
            var value = MouseButtonSetting.FromComboIndex(mouseButtonBox.SelectedIndex);
            GetService().SaveOpenMouseButton(value.ToDisplayString());
            globalInput?.Update(HotkeySetting.ParseOrDefault(hotkeyBox.Text), value);
            SetStatus(value.IsEnabled ? "鼠标侧键唤出已保存：" + value.ToDisplayString() : "鼠标侧键唤出已关闭。", Green, GreenSurface);
        }
        catch (Exception ex) { SetStatus("鼠标侧键设置失败：" + ex.Message, Red, RedSurface); }
    }

    private void StartupSelectionChanged()
    {
        if (loadingStartup) return;
        try
        {
            StartupManager.SetEnabled(startupCheckBox.IsChecked == true, CurrentExecutable.Resolve());
            SetStatus(startupCheckBox.IsChecked == true ? $"已开启开机启动；下次登录 {CodexPlatform.Description} 后会自动驻留后台。" : "已关闭开机启动。", Green, GreenSurface);
        }
        catch (Exception ex) { SetStatus("开机启动设置失败：" + ex.Message, Red, RedSurface); }
    }

    private void CompatibilitySelectionChanged()
    {
        if (loading) return;
        SetStatus(compatibilityCheckBox.IsChecked == true
            ? "第三方兼容模式已开启；下次切换第三方时会临时收起不兼容的高级工具。"
            : "第三方兼容模式已关闭；下次切换第三方时会恢复完整工具能力。", Blue, BlueSurface);
    }

    private void StartGlobalInputHook()
    {
        try
        {
            globalInput = new GlobalInputHook(
                HotkeySetting.ParseOrDefault(hotkeyBox.Text),
                MouseButtonSetting.FromComboIndex(mouseButtonBox.SelectedIndex),
                () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible) HideToTray(false); else App.CurrentApp?.ShowMainWindow();
                }));
            globalInput.Start(message => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetStatus(OperatingSystem.IsMacOS()
                    ? "全局快捷键监听未启动：" + message + "。请在系统设置 → 隐私与安全性 → 辅助功能中允许本应用。"
                    : "全局快捷键监听未启动：" + message, Amber, AmberSurface)));
        }
        catch (Exception ex)
        {
            SetStatus("全局快捷键监听未启动：" + ex.Message, Amber, AmberSurface);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (var control in operationControls) control.IsEnabled = enabled;
        deleteProfileButton.IsEnabled = enabled && SelectedProfile is not null;
        rootBox.IsReadOnly = !enabled;
    }

    private void SetStatus(string text, IBrush foreground, IBrush background)
    {
        statusText.Text = text;
        statusText.Foreground = foreground;
        statusBorder.Background = background;
    }

    internal async Task RunUiSmokeTestAsync(string reportPath)
    {
        var report = new List<string>
        {
            "Codex API Switcher UI smoke test",
            "Started: " + DateTimeOffset.Now.ToString("O"),
            "Root: " + (rootBox.Text ?? string.Empty),
            string.Empty
        };
        DialogWindow.AutoAcceptDialogs = true;
        codexProcessButtonsDryRun = true;
        var oldExitCode = Environment.ExitCode;
        Environment.ExitCode = 0;

        try
        {
            await WaitUntilIdleAsync("initial UI load");
            await LoadRootSettingsAsync(true);

            await RunUiSmokeStepAsync(report, "save-profile-button", async () =>
            {
                profileBox.SelectedIndex = 0;
                urlBox.Text = "https://ui-smoke.example.test/v1";
                thirdPartyModelBox.Text = "ui-smoke-third";
                keyBox.Text = "ui-smoke-profile-token";
                await SaveProfileAsync();
            });

            await RunUiSmokeStepAsync(report, "delete-profile-button", async () =>
            {
                var index = currentProfiles.FindIndex(profile => profile.Name.Contains("ui-smoke.example.test", StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw new InvalidOperationException("测试档案未出现在档案列表中。");
                profileBox.SelectedIndex = index + 1;
                await DeleteProfileAsync();
            });

            await RunUiSmokeStepAsync(report, "switch-third-party-button", async () =>
            {
                profileBox.SelectedIndex = 0;
                urlBox.Text = "https://ui-smoke.example.test/v1/responses";
                thirdPartyModelBox.Text = "ui-smoke-third";
                keyBox.Text = "ui-smoke-switch-token";
                compatibilityCheckBox.IsChecked = true;
                await SwitchThirdPartyAsync();
            });

            await RunUiSmokeStepAsync(report, "switch-official-button", async () =>
            {
                officialModelBox.Text = "ui-smoke-official";
                await SwitchOfficialAsync();
            });

            await RunUiSmokeStepAsync(report, "reset-config-button", async () =>
            {
                officialModelBox.Text = "ui-smoke-reset";
                await ResetConfigAsync();
            });

            await RunUiSmokeStepAsync(report, "repair-sidebar-button", RepairSidebarAsync);
            await RunUiSmokeStepAsync(report, "armor-reminder-button", EnableArmorAsync);
            await RunUiSmokeStepAsync(report, "armor-button", EnableArmorAsync);
            await RunUiSmokeStepAsync(report, "restore-armor-button", RestoreArmorAsync);
            await RunUiSmokeStepAsync(report, "rollback-button", RollbackAsync);
            await RunUiSmokeStepAsync(report, "launch-codex-button", LaunchCodexFromTrayAsync);
            await RunUiSmokeStepAsync(report, "close-codex-button", CloseCodexFromTrayAsync);

            report.Add(string.Empty);
            report.Add("RESULT: PASS");
            SetStatus("UI 烟测通过。", Green, GreenSurface);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            report.Add(string.Empty);
            report.Add("RESULT: FAIL");
            report.Add(ex.ToString());
            SetStatus("UI 烟测失败：" + ex.Message, Red, RedSurface);
        }
        finally
        {
            if (Environment.ExitCode == 0) Environment.ExitCode = oldExitCode;
            codexProcessButtonsDryRun = false;
            DialogWindow.AutoAcceptDialogs = false;
            WriteUiSmokeReport(reportPath, report);
        }
    }

    private async Task RunUiSmokeStepAsync(List<string> report, string name, Func<Task> step)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await WaitUntilIdleAsync(name + " before");
            await step();
            await WaitUntilIdleAsync(name + " after");
            report.Add($"PASS {name} ({timer.ElapsedMilliseconds} ms)");
        }
        catch (Exception ex)
        {
            report.Add($"FAIL {name} ({timer.ElapsedMilliseconds} ms): {ex.Message}");
            throw;
        }
    }

    private async Task WaitUntilIdleAsync(string context)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while ((loading || rootLoading) && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        if (loading || rootLoading) throw new TimeoutException("等待界面空闲超时：" + context);
    }

    private static void WriteUiSmokeReport(string reportPath, IEnumerable<string> report)
    {
        var fullPath = Path.GetFullPath(reportPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllLines(fullPath, report, new UTF8Encoding(false));
        Console.WriteLine("UI smoke report: " + fullPath);
    }

    private static string GetProfileName(string? url)
    {
        try { return new Uri(url ?? string.Empty).Host is { Length: > 0 } host ? host : "未命名中转站"; }
        catch { return "未命名中转站"; }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
