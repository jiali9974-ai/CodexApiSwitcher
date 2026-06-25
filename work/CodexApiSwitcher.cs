using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexApiSwitcher
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                bool startupLaunch = IsStartupLaunch(args);
                if (args.Length > 0 && !startupLaunch)
                {
                    return RunCommand(args);
                }

                IntPtr console = GetConsoleWindow();
                if (console != IntPtr.Zero)
                {
                    ShowWindow(console, 0);
                    FreeConsole();
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(startupLaunch));
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length > 0)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }

                MessageBox.Show(ex.Message, "Codex API 切换器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool IsStartupLaunch(string[] args)
        {
            return args.Any(arg =>
                string.Equals(arg, "--startup-launch", StringComparison.OrdinalIgnoreCase));
        }

        private static int RunCommand(string[] args)
        {
            Dictionary<string, string> options = ParseArgs(args);
            string root = GetOption(options, "--root", GetDefaultRoot());
            SwitcherService service = new SwitcherService(root, Application.ExecutablePath);

            if (options.ContainsKey("--emit-token"))
            {
                Console.Out.Write(service.ReadToken());
                return 0;
            }

            if (options.ContainsKey("--emit-profile-token"))
            {
                Console.Out.Write(service.ReadThirdPartyProfileToken(RequireOption(options, "--name")));
                return 0;
            }

            if (options.ContainsKey("--status"))
            {
                Console.WriteLine(service.GetStatus().ToDisplayString());
                return 0;
            }

            if (options.ContainsKey("--switch-third-party"))
            {
                string url = RequireOption(options, "--url");
                string model = RequireOption(options, "--model");
                string key = GetOption(options, "--key", string.Empty);
                string profileName = GetOption(options, "--profile", string.Empty);
                bool compatibilityMode = options.ContainsKey("--compat-mode");
                if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(profileName))
                {
                    key = RequireOption(options, "--key");
                }
                SwitchOutcome outcome = service.SwitchToThirdParty(
                    url,
                    model,
                    key,
                    profileName,
                    compatibilityMode);
                Console.WriteLine("Switched to third-party Responses API.");
                if (outcome.HasNotice)
                {
                    Console.WriteLine(outcome.ToCommandLineNotice());
                }
                return 0;
            }

            if (options.ContainsKey("--save-profile"))
            {
                string name = RequireOption(options, "--name");
                string url = RequireOption(options, "--url");
                string model = RequireOption(options, "--model");
                string key = RequireOption(options, "--key");
                ThirdPartyProfile profile = service.SaveThirdPartyProfile(name, url, model, key);
                Console.WriteLine(profile.Name + "|" + profile.BaseUrl + "|" + profile.Model);
                return 0;
            }

            if (options.ContainsKey("--delete-profile"))
            {
                service.DeleteThirdPartyProfile(RequireOption(options, "--name"));
                Console.WriteLine("Deleted profile.");
                return 0;
            }

            if (options.ContainsKey("--list-profiles"))
            {
                foreach (ThirdPartyProfile profile in service.LoadThirdPartyProfiles())
                {
                    Console.WriteLine(profile.Name + "|" + profile.BaseUrl + "|" + profile.Model);
                }
                return 0;
            }

            if (options.ContainsKey("--switch-official"))
            {
                string model = GetOption(options, "--model", "gpt-5.5");
                SwitchOutcome outcome = service.SwitchToOfficial(model);
                Console.WriteLine("Switched to official OpenAI login.");
                if (outcome.HasNotice)
                {
                    Console.WriteLine(outcome.ToCommandLineNotice());
                }
                return 0;
            }

            if (options.ContainsKey("--rollback"))
            {
                service.Rollback();
                Console.WriteLine("Restored the latest backup.");
                return 0;
            }

            if (options.ContainsKey("--reset-config"))
            {
                string model = GetOption(options, "--model", "gpt-5.5");
                service.ResetModelConfiguration(model);
                Console.WriteLine("Rebuilt the model configuration for official OpenAI login.");
                return 0;
            }

            if (options.ContainsKey("--repair-sidebar"))
            {
                Console.WriteLine(service.RepairConversationIndex());
                return 0;
            }

            if (options.ContainsKey("--codex-launch-plan"))
            {
                Console.WriteLine(service.GetCodexLaunchPlan());
                return 0;
            }

            if (options.ContainsKey("--launch-codex"))
            {
                Console.WriteLine(service.LaunchCodex());
                return 0;
            }

            if (options.ContainsKey("--close-codex"))
            {
                Console.WriteLine(service.CloseCodexProcesses(options.ContainsKey("--dry-run")));
                return 0;
            }

            if (options.ContainsKey("--normalize-hotkey"))
            {
                Console.WriteLine(HotkeySetting.Parse(RequireOption(options, "--hotkey")).ToDisplayString());
                return 0;
            }

            if (options.ContainsKey("--save-hotkey"))
            {
                string hotkey = HotkeySetting.Parse(RequireOption(options, "--hotkey")).ToDisplayString();
                service.SaveOpenHotkey(hotkey);
                Console.WriteLine(hotkey);
                return 0;
            }

            if (options.ContainsKey("--show-hotkey"))
            {
                Console.WriteLine(service.LoadSettings().GetOpenHotkey());
                return 0;
            }

            if (options.ContainsKey("--normalize-mouse-button"))
            {
                Console.WriteLine(MouseButtonSetting.Parse(RequireOption(options, "--mouse-button")).ToDisplayString());
                return 0;
            }

            if (options.ContainsKey("--save-mouse-button"))
            {
                string mouseButton = MouseButtonSetting.Parse(RequireOption(options, "--mouse-button")).ToDisplayString();
                service.SaveOpenMouseButton(mouseButton);
                Console.WriteLine(mouseButton);
                return 0;
            }

            if (options.ContainsKey("--show-mouse-button"))
            {
                Console.WriteLine(service.LoadSettings().GetOpenMouseButton());
                return 0;
            }

            if (options.ContainsKey("--enable-startup"))
            {
                StartupManager.SetEnabled(true, Application.ExecutablePath);
                Console.WriteLine("Enabled");
                return 0;
            }

            if (options.ContainsKey("--disable-startup"))
            {
                StartupManager.SetEnabled(false, Application.ExecutablePath);
                Console.WriteLine("Disabled");
                return 0;
            }

            if (options.ContainsKey("--show-startup"))
            {
                Console.WriteLine(StartupManager.IsEnabled(Application.ExecutablePath) ? "Enabled" : "Disabled");
                return 0;
            }

            throw new InvalidOperationException("Unknown command.");
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                string value = "true";
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                result[key] = value;
            }
            return result;
        }

        private static string RequireOption(Dictionary<string, string> options, string key)
        {
            string value;
            if (!options.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Missing required option: " + key);
            }
            return value;
        }

        private static string GetOption(Dictionary<string, string> options, string key, string fallback)
        {
            string value;
            return options.TryGetValue(key, out value) ? value : fallback;
        }

        internal static string GetDefaultRoot()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            {
                return configured;
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
        }
    }

    internal sealed class MainForm : Form
    {
        private const int OpenWindowHotkeyId = 17217;
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModWin = 0x0008;
        private const uint ModNoRepeat = 0x4000;
        private const int SwRestore = 9;
        private const int WhMouseLl = 14;
        private const int HcAction = 0;
        private const int WmXButtonDown = 0x020B;
        private const int WmEnterSizeMove = 0x0231;
        private const int WmExitSizeMove = 0x0232;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookId,
            LowLevelMouseProc hookProcedure,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int code,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        private delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MousePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LowLevelMouseHookStruct
        {
            public MousePoint Point;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        private static readonly Color WindowBackground = Color.FromArgb(247, 249, 252);
        private static readonly Color Ink = Color.FromArgb(23, 32, 51);
        private static readonly Color MutedInk = Color.FromArgb(88, 99, 118);
        private static readonly Color BrandBlue = Color.FromArgb(36, 87, 214);
        private static readonly Color BrandBlueHover = Color.FromArgb(30, 72, 179);
        private static readonly Color BrandBluePressed = Color.FromArgb(24, 58, 145);
        private static readonly Color BrandTeal = Color.FromArgb(23, 166, 166);
        private static readonly Color BorderColor = Color.FromArgb(202, 213, 226);
        private static readonly Color FieldFocus = Color.FromArgb(240, 246, 255);
        private static readonly Color SuccessGreen = Color.FromArgb(21, 128, 61);
        private static readonly Color WarningAmber = Color.FromArgb(146, 64, 14);
        private static readonly Color ErrorRed = Color.FromArgb(180, 35, 24);

        private readonly TextBox rootBox = new TextBox();
        private readonly ComboBox profileBox = new ComboBox();
        private readonly TextBox urlBox = new TextBox();
        private readonly TextBox thirdPartyModelBox = new TextBox();
        private readonly TextBox officialModelBox = new TextBox();
        private readonly TextBox keyBox = new TextBox();
        private readonly ErrorProvider errorProvider = new ErrorProvider();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Label statusLabel = new Label();
        private readonly Label keyStateLabel = new Label();
        private readonly Label compatibilityNote = new Label();
        private readonly Label backupLocationLabel = new Label();
        private readonly Label watermarkLabel = new Label();
        private readonly TextBox hotkeyBox = new TextBox();
        private readonly Button officialButton = new Button();
        private readonly Button thirdPartyButton = new Button();
        private readonly Button rollbackButton = new Button();
        private readonly Button repairButton = new Button();
        private readonly Button resetConfigButton = new Button();
        private readonly Button launchCodexButton = new Button();
        private readonly Button closeCodexButton = new Button();
        private readonly Button hotkeyButton = new Button();
        private readonly Button browseButton = new Button();
        private readonly Button saveProfileButton = new Button();
        private readonly Button deleteProfileButton = new Button();
        private readonly ComboBox mouseButtonBox = new ComboBox();
        private readonly CheckBox startupCheckBox = new CheckBox();
        private readonly CheckBox compatibilityCheckBox = new CheckBox();
        private readonly Dictionary<Button, ButtonFeedback> buttonFeedback =
            new Dictionary<Button, ButtonFeedback>();
        private readonly System.Windows.Forms.Timer buttonAnimationTimer =
            new System.Windows.Forms.Timer();
        private readonly LowLevelMouseProc mouseHookProcedure;
        private List<ThirdPartyProfile> currentProfiles = new List<ThirdPartyProfile>();
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private HotkeySetting currentHotkey = HotkeySetting.Default;
        private MouseButtonSetting currentMouseButton = MouseButtonSetting.None;
        private bool suppressValidationFeedback;
        private bool loadingProfiles;
        private bool loadingMouseButton;
        private bool hasStoredToken;
        private bool rootSettingsLoaded;
        private bool allowExit;
        private bool capturingHotkey;
        private bool hotkeyRegistered;
        private bool registeringHotkey;
        private IntPtr hotkeyWindowHandle = IntPtr.Zero;
        private IntPtr mouseHookHandle = IntPtr.Zero;
        private bool trayNoticeShown;
        private bool loadingStartupState;
        private bool loadingCompatibilityState;
        private bool inWindowMoveLoop;
        private bool mouseHookPausedForMove;
        private MethodInvoker deferredMoveUiAction;
        private readonly bool startHiddenToTray;

        internal MainForm()
            : this(false)
        {
        }

        internal MainForm(bool startHidden)
        {
            startHiddenToTray = startHidden;
            Text = "CAS - Codex API 切换器";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(690, 755);
            MinimumSize = new Size(706, 794);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = WindowBackground;
            DoubleBuffered = true;
            Icon = CasLogoIcon.CreateIcon(64);
            KeyPreview = true;
            mouseHookProcedure = MouseHookCallback;

            BuildUi();
            BuildTray();
            rootBox.Text = Program.GetDefaultRoot();
            if (startHiddenToTray)
            {
                ShowInitialLoadingState();
            }
            else
            {
                LoadRootSettings();
            }
            LoadHotkeySettings();
            LoadStartupState();
            RegisterConfiguredHotkey();
            RefreshMouseHook();
        }

        private void BuildUi()
        {
            Label title = new Label();
            title.Text = "CAS · Codex API 切换器";
            title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(28, 22);
            Controls.Add(title);

            Label safety = new Label();
            safety.Text = "切换前请先彻底退出 Codex。工具会自动备份配置和历史库，不会修改会话正文、记忆文件或 auth.json。";
            safety.ForeColor = SuccessGreen;
            safety.BackColor = Color.FromArgb(236, 253, 243);
            safety.BorderStyle = BorderStyle.FixedSingle;
            safety.Location = new Point(30, 64);
            safety.Size = new Size(630, 38);
            safety.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(safety);

            AddLabel("Codex 根目录", 30, 122);
            rootBox.Location = new Point(160, 117);
            rootBox.Size = new Size(405, 27);
            rootBox.Leave += delegate { ReloadRootAndHotkeySettings(); };
            Controls.Add(rootBox);

            browseButton.Text = "选择...";
            browseButton.Location = new Point(575, 116);
            browseButton.Size = new Size(85, 30);
            browseButton.Click += BrowseRoot;
            Controls.Add(browseButton);

            statusLabel.Location = new Point(30, 158);
            statusLabel.Size = new Size(630, 38);
            statusLabel.BackColor = Color.White;
            statusLabel.BorderStyle = BorderStyle.FixedSingle;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(statusLabel);

            AddLabel("中转站档案", 30, 210);
            profileBox.Location = new Point(160, 205);
            profileBox.Size = new Size(270, 27);
            profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            profileBox.SelectedIndexChanged += ProfileSelectionChanged;
            Controls.Add(profileBox);

            saveProfileButton.Text = "保存档案";
            saveProfileButton.Location = new Point(445, 202);
            saveProfileButton.Size = new Size(95, 32);
            saveProfileButton.Click += SaveCurrentProfile;
            Controls.Add(saveProfileButton);

            deleteProfileButton.Text = "删除档案";
            deleteProfileButton.Location = new Point(555, 202);
            deleteProfileButton.Size = new Size(105, 32);
            deleteProfileButton.Click += DeleteCurrentProfile;
            Controls.Add(deleteProfileButton);

            AddLabel("第三方 Base URL", 30, 250);
            urlBox.Location = new Point(160, 245);
            urlBox.Size = new Size(500, 27);
            Controls.Add(urlBox);

            AddLabel("第三方模型", 30, 290);
            thirdPartyModelBox.Location = new Point(160, 285);
            thirdPartyModelBox.Size = new Size(210, 27);
            Controls.Add(thirdPartyModelBox);

            AddLabel("官方模型", 390, 290, 75);
            officialModelBox.Location = new Point(475, 285);
            officialModelBox.Size = new Size(185, 27);
            Controls.Add(officialModelBox);

            AddLabel("第三方 API Key", 30, 330);
            keyBox.Location = new Point(160, 325);
            keyBox.Size = new Size(410, 27);
            keyBox.UseSystemPasswordChar = true;
            Controls.Add(keyBox);

            CheckBox showKey = new CheckBox();
            showKey.Text = "显示";
            showKey.Location = new Point(580, 327);
            showKey.Size = new Size(70, 24);
            showKey.CheckedChanged += delegate { keyBox.UseSystemPasswordChar = !showKey.Checked; };
            Controls.Add(showKey);

            keyStateLabel.Location = new Point(160, 356);
            keyStateLabel.AutoSize = true;
            keyStateLabel.ForeColor = Color.DimGray;
            Controls.Add(keyStateLabel);

            compatibilityCheckBox.Text = "兼容模式";
            compatibilityCheckBox.Location = new Point(30, 378);
            compatibilityCheckBox.Size = new Size(95, 24);
            compatibilityCheckBox.CheckedChanged += CompatibilitySelectionChanged;
            Controls.Add(compatibilityCheckBox);

            compatibilityNote.Text = "遇到 Image generation 403 时开启；切第三方会收起高级工具，切回官方自动恢复。";
            compatibilityNote.Location = new Point(130, 374);
            compatibilityNote.Size = new Size(530, 34);
            compatibilityNote.ForeColor = MutedInk;
            Controls.Add(compatibilityNote);

            thirdPartyButton.Text = "切换到第三方 API";
            thirdPartyButton.Location = new Point(30, 418);
            thirdPartyButton.Size = new Size(195, 46);
            thirdPartyButton.Click += SwitchThirdParty;
            Controls.Add(thirdPartyButton);

            officialButton.Text = "切换到官方登录";
            officialButton.Location = new Point(245, 418);
            officialButton.Size = new Size(195, 46);
            officialButton.Click += SwitchOfficial;
            Controls.Add(officialButton);

            rollbackButton.Text = "恢复最近备份";
            rollbackButton.Location = new Point(460, 418);
            rollbackButton.Size = new Size(200, 46);
            rollbackButton.Click += Rollback;
            Controls.Add(rollbackButton);

            repairButton.Text = "修复会话列表";
            repairButton.Location = new Point(30, 484);
            repairButton.Size = new Size(145, 42);
            repairButton.Click += RepairSidebar;
            Controls.Add(repairButton);

            resetConfigButton.Text = "恢复基础配置";
            resetConfigButton.Location = new Point(190, 484);
            resetConfigButton.Size = new Size(145, 42);
            resetConfigButton.Click += ResetConfig;
            Controls.Add(resetConfigButton);

            launchCodexButton.Text = "启动 Codex";
            launchCodexButton.Location = new Point(350, 484);
            launchCodexButton.Size = new Size(145, 42);
            launchCodexButton.Click += LaunchCodex;
            Controls.Add(launchCodexButton);

            closeCodexButton.Text = "关闭 Codex 后台";
            closeCodexButton.Location = new Point(510, 484);
            closeCodexButton.Size = new Size(150, 42);
            closeCodexButton.Click += CloseCodex;
            Controls.Add(closeCodexButton);

            Label maintenanceNote = new Label();
            maintenanceNote.Text = "会话列表缺失时用修复；配置损坏时用基础配置恢复；关闭后台会结束所有 Codex 进程。";
            maintenanceNote.Location = new Point(30, 538);
            maintenanceNote.Size = new Size(630, 32);
            maintenanceNote.ForeColor = WarningAmber;
            Controls.Add(maintenanceNote);

            AddLabel("打开快捷键", 30, 581, 90);
            hotkeyBox.Location = new Point(130, 576);
            hotkeyBox.Size = new Size(165, 27);
            hotkeyBox.ReadOnly = true;
            hotkeyBox.Text = currentHotkey.ToDisplayString();
            hotkeyBox.KeyDown += CaptureHotkey;
            Controls.Add(hotkeyBox);

            hotkeyButton.Text = "设置快捷键";
            hotkeyButton.Location = new Point(310, 573);
            hotkeyButton.Size = new Size(145, 32);
            hotkeyButton.Click += BeginHotkeyCapture;
            Controls.Add(hotkeyButton);

            AddLabel("鼠标侧键", 475, 581, 75);
            mouseButtonBox.Location = new Point(555, 576);
            mouseButtonBox.Size = new Size(105, 27);
            mouseButtonBox.DropDownStyle = ComboBoxStyle.DropDownList;
            mouseButtonBox.Items.Add("关闭");
            mouseButtonBox.Items.Add("侧键1");
            mouseButtonBox.Items.Add("侧键2");
            mouseButtonBox.SelectedIndex = 0;
            mouseButtonBox.SelectedIndexChanged += MouseButtonSelectionChanged;
            Controls.Add(mouseButtonBox);

            startupCheckBox.Text = "开机启动";
            startupCheckBox.Location = new Point(30, 618);
            startupCheckBox.Size = new Size(115, 24);
            startupCheckBox.CheckedChanged += StartupSelectionChanged;
            Controls.Add(startupCheckBox);

            Label footer = new Label();
            footer.Text = "切换完成后请重新打开 Codex。快捷键和鼠标侧键都支持打开/收回后台。开机启动会直接驻留托盘。";
            footer.Location = new Point(155, 616);
            footer.Size = new Size(630, 45);
            footer.ForeColor = MutedInk;
            Controls.Add(footer);

            backupLocationLabel.Location = new Point(30, 676);
            backupLocationLabel.Size = new Size(630, 32);
            backupLocationLabel.ForeColor = MutedInk;
            backupLocationLabel.AutoEllipsis = true;
            Controls.Add(backupLocationLabel);

            watermarkLabel.Text = "github.com/yin-yizhen/codex-api-switcher";
            watermarkLabel.Location = new Point(300, 724);
            watermarkLabel.Size = new Size(360, 24);
            watermarkLabel.ForeColor = Color.FromArgb(145, 145, 145);
            watermarkLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(watermarkLabel);

            ConfigureFeedback(showKey, browseButton);
        }

        private void AddLabel(string text, int x, int y)
        {
            AddLabel(text, x, y, 125);
        }

        private void AddLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 24);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Ink;
            Controls.Add(label);
        }

        private void ConfigureFeedback(CheckBox showKey, Button chooseButton)
        {
            errorProvider.ContainerControl = this;
            errorProvider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
            toolTip.AutoPopDelay = 8000;
            toolTip.InitialDelay = 350;
            toolTip.ReshowDelay = 120;
            buttonAnimationTimer.Interval = 16;
            buttonAnimationTimer.Tick += AnimateButtons;

            StyleButton(thirdPartyButton, BrandBlue, BrandBlueHover, BrandBluePressed, Color.White);
            StyleButton(officialButton, Ink, Color.FromArgb(35, 48, 70), Color.FromArgb(13, 23, 38), Color.White);
            StyleButton(rollbackButton, Color.White, Color.FromArgb(248, 250, 252), Color.FromArgb(235, 241, 248), Ink);
            StyleButton(repairButton, Color.White, Color.FromArgb(248, 250, 252), Color.FromArgb(235, 241, 248), Ink);
            StyleButton(resetConfigButton, Color.White, Color.FromArgb(248, 250, 252), Color.FromArgb(235, 241, 248), Ink);
            StyleButton(launchCodexButton, BrandTeal, Color.FromArgb(19, 136, 136), Color.FromArgb(15, 112, 112), Color.White);
            StyleButton(closeCodexButton, Color.FromArgb(180, 35, 24), Color.FromArgb(150, 28, 20), Color.FromArgb(122, 22, 16), Color.White);
            StyleButton(hotkeyButton, Color.White, Color.FromArgb(248, 250, 252), Color.FromArgb(235, 241, 248), Ink);
            StyleButton(chooseButton, Color.White, Color.FromArgb(248, 250, 252), Color.FromArgb(235, 241, 248), Ink);
            StyleButton(saveProfileButton, Color.White, Color.FromArgb(248, 250, 252), Color.FromArgb(235, 241, 248), Ink);
            StyleButton(deleteProfileButton, Color.White, Color.FromArgb(248, 250, 252), Color.FromArgb(235, 241, 248), Ink);

            hotkeyBox.BorderStyle = BorderStyle.FixedSingle;
            hotkeyBox.BackColor = Color.White;
            profileBox.FlatStyle = FlatStyle.Flat;
            profileBox.BackColor = Color.White;
            mouseButtonBox.FlatStyle = FlatStyle.Flat;
            mouseButtonBox.BackColor = Color.White;

            WireInput(rootBox, "选择包含 config.toml 的 Codex 根目录。");
            WireInput(urlBox, "第三方 API 地址，工具会自动规范为 /v1。");
            WireInput(thirdPartyModelBox, "切换到第三方 API 时写入 config.toml 的模型名。");
            WireInput(officialModelBox, "切换回官方登录时写入 config.toml 的模型名。");
            WireInput(keyBox, "第三方 API Key 只会加密保存在本机。留空则继续使用已保存的 Key。");

            showKey.Cursor = Cursors.Hand;
            toolTip.SetToolTip(showKey, "临时显示或隐藏 API Key 输入框。");
            toolTip.SetToolTip(thirdPartyButton, "写入第三方 provider，并同步历史会话 provider。");
            toolTip.SetToolTip(officialButton, "恢复官方 OpenAI 登录 provider，并同步历史会话 provider。");
            toolTip.SetToolTip(rollbackButton, "把 config.toml 恢复到最近一次自动备份。");
            toolTip.SetToolTip(repairButton, "修复历史会话在侧栏不可见的问题。");
            toolTip.SetToolTip(resetConfigButton, "移除损坏的 custom provider 段，恢复官方基础配置。");
            toolTip.SetToolTip(launchCodexButton, "打开 Codex 桌面应用；如果已经运行，会直接提示。");
            toolTip.SetToolTip(closeCodexButton, "结束所有 Codex/Codex 后台进程，方便切换配置。");
            toolTip.SetToolTip(hotkeyBox, "当前用于打开 CAS 界面的全局快捷键。");
            toolTip.SetToolTip(hotkeyButton, "点击后按下新的组合键；也可以按鼠标侧键直接捕获为唤出方式。");
            toolTip.SetToolTip(chooseButton, "选择另一个 Codex 根目录。");
            toolTip.SetToolTip(profileBox, "选择已保存的中转站档案；API Key 会从本机加密档案中读取。");
            toolTip.SetToolTip(saveProfileButton, "把当前 Base URL、模型和 API Key 保存为一个可复用中转站档案。");
            toolTip.SetToolTip(deleteProfileButton, "删除当前选择的中转站档案和对应加密 Key。");
            toolTip.SetToolTip(compatibilityCheckBox, "中转站报 Image generation 403 时建议开启；会临时关闭 Codex 生图/图片查看/Web 搜索和高级插件，切回官方自动恢复。");
            toolTip.SetToolTip(compatibilityNote, "中转站报 Image generation 403 时建议开启；会临时关闭 Codex 生图/图片查看/Web 搜索和高级插件，切回官方自动恢复。");
            toolTip.SetToolTip(mouseButtonBox, "选择鼠标侧键唤出 CAS。侧键1通常是后退键，侧键2通常是前进键。");
            toolTip.SetToolTip(startupCheckBox, "勾选后随当前 Windows 用户开机启动，并直接驻留托盘。");
        }

        private void WireInput(TextBox textBox, string hint)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = Color.White;
            toolTip.SetToolTip(textBox, hint);
            textBox.Enter += delegate
            {
                textBox.BackColor = FieldFocus;
                statusLabel.BackColor = Color.FromArgb(238, 244, 255);
                statusLabel.ForeColor = BrandBlue;
                statusLabel.Text = hint;
            };
            textBox.Leave += delegate
            {
                textBox.BackColor = Color.White;
                UpdateInputFeedback();
            };
            textBox.TextChanged += delegate { UpdateInputFeedback(); };
        }

        private void StyleButton(
            Button button,
            Color normalBack,
            Color hoverBack,
            Color pressedBack,
            Color foreColor)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = BorderColor;
            button.UseVisualStyleBackColor = false;
            button.BackColor = normalBack;
            button.ForeColor = foreColor;
            button.Cursor = Cursors.Hand;
            button.Font = new Font(Font.FontFamily, button.Height >= 44 ? 9.5F : 9F, FontStyle.Bold);
            button.TabStop = true;

            buttonFeedback[button] = new ButtonFeedback(normalBack, hoverBack, pressedBack, foreColor);
            button.MouseEnter += delegate { ApplyButtonState(button, "hover"); };
            button.MouseLeave += delegate { ApplyButtonState(button, "normal"); };
            button.MouseDown += delegate { ApplyButtonState(button, "pressed"); };
            button.MouseUp += delegate
            {
                ApplyButtonState(button, button.ClientRectangle.Contains(button.PointToClient(Cursor.Position)) ? "hover" : "normal");
            };
            button.GotFocus += delegate { ApplyButtonState(button, "focus"); };
            button.LostFocus += delegate { ApplyButtonState(button, "normal"); };
        }

        private void ApplyButtonState(Button button, string state)
        {
            ButtonFeedback feedback;
            if (!buttonFeedback.TryGetValue(button, out feedback))
            {
                return;
            }
            if (!button.Enabled)
            {
                feedback.TargetBack = Color.FromArgb(229, 234, 242);
                button.BackColor = feedback.TargetBack;
                button.ForeColor = Color.FromArgb(142, 151, 165);
                button.FlatAppearance.BorderColor = Color.FromArgb(213, 220, 232);
                button.Cursor = Cursors.Default;
                return;
            }

            button.ForeColor = feedback.ForeColor;
            button.Cursor = Cursors.Hand;
            if (state == "pressed")
            {
                feedback.TargetBack = feedback.PressedBack;
                button.FlatAppearance.BorderColor = Darken(feedback.PressedBack, 0.16F);
            }
            else if (state == "hover")
            {
                feedback.TargetBack = feedback.HoverBack;
                button.FlatAppearance.BorderColor = Darken(feedback.HoverBack, 0.10F);
            }
            else if (state == "focus")
            {
                feedback.TargetBack = feedback.HoverBack;
                button.FlatAppearance.BorderColor = BrandBlue;
            }
            else
            {
                feedback.TargetBack = feedback.NormalBack;
                button.FlatAppearance.BorderColor = BorderColor;
            }

            if (inWindowMoveLoop)
            {
                button.BackColor = feedback.TargetBack;
                return;
            }

            EnsureButtonAnimation();
        }

        private void EnsureButtonAnimation()
        {
            if (inWindowMoveLoop)
            {
                return;
            }
            if (!buttonAnimationTimer.Enabled)
            {
                buttonAnimationTimer.Start();
            }
        }

        private void AnimateButtons(object sender, EventArgs e)
        {
            if (inWindowMoveLoop)
            {
                buttonAnimationTimer.Stop();
                return;
            }

            bool anyAnimating = false;
            foreach (KeyValuePair<Button, ButtonFeedback> entry in buttonFeedback)
            {
                Button button = entry.Key;
                ButtonFeedback feedback = entry.Value;
                if (button.IsDisposed)
                {
                    continue;
                }

                Color next = Blend(button.BackColor, feedback.TargetBack, 0.35F);
                if (ColorDistance(next, feedback.TargetBack) <= 3)
                {
                    next = feedback.TargetBack;
                }
                else
                {
                    anyAnimating = true;
                }
                button.BackColor = next;
            }

            if (!anyAnimating)
            {
                buttonAnimationTimer.Stop();
            }
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                from.A + (int)((to.A - from.A) * amount),
                from.R + (int)((to.R - from.R) * amount),
                from.G + (int)((to.G - from.G) * amount),
                from.B + (int)((to.B - from.B) * amount));
        }

        private static int ColorDistance(Color left, Color right)
        {
            return Math.Abs(left.R - right.R) + Math.Abs(left.G - right.G) + Math.Abs(left.B - right.B);
        }

        private static Color Darken(Color color, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                color.A,
                Math.Max(0, color.R - (int)(color.R * amount)),
                Math.Max(0, color.G - (int)(color.G * amount)),
                Math.Max(0, color.B - (int)(color.B * amount)));
        }

        private void UpdateInputFeedback()
        {
            if (suppressValidationFeedback)
            {
                return;
            }

            errorProvider.SetError(rootBox, string.Empty);
            errorProvider.SetError(urlBox, string.Empty);
            errorProvider.SetError(thirdPartyModelBox, string.Empty);
            errorProvider.SetError(officialModelBox, string.Empty);
            errorProvider.SetError(keyBox, string.Empty);

            string root = rootBox.Text.Trim();
            if (root.Length > 0 && !Directory.Exists(root))
            {
                errorProvider.SetError(rootBox, "这个 Codex 根目录不存在。");
            }

            string url = urlBox.Text.Trim();
            Uri uri;
            if (url.Length > 0 && !Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                errorProvider.SetError(urlBox, "Base URL 必须是完整地址，例如 https://api.example.com/v1。");
            }

            if (thirdPartyModelBox.Text.Trim().Length == 0)
            {
                errorProvider.SetError(thirdPartyModelBox, "第三方模型不能为空。");
            }
            if (officialModelBox.Text.Trim().Length == 0)
            {
                errorProvider.SetError(officialModelBox, "官方模型不能为空。");
            }

            if (keyBox.Text.Trim().Length > 0)
            {
                string selectedName = GetSelectedProfileName();
                keyStateLabel.Text = selectedName.Length > 0
                    ? "将更新档案「" + selectedName + "」的加密 Key。"
                    : "将更新默认保存的加密 Key。";
                keyStateLabel.ForeColor = SuccessGreen;
            }
            else
            {
                ThirdPartyProfile selectedProfile = GetSelectedProfile();
                if (selectedProfile != null)
                {
                    keyStateLabel.Text = "已选择档案「" + selectedProfile.Name + "」；留空将使用该档案的加密 Key。";
                    keyStateLabel.ForeColor = SuccessGreen;
                }
                else
                {
                    keyStateLabel.Text = hasStoredToken
                        ? "已保存默认加密 Key；留空即可继续使用。"
                        : "尚未保存 Key。首次切换第三方时必须填写。";
                    keyStateLabel.ForeColor = hasStoredToken ? SuccessGreen : WarningAmber;
                }
            }
        }

        private void RefreshProfiles(string preferredName)
        {
            RefreshProfilesFromList(GetService().LoadThirdPartyProfiles(), preferredName);
        }

        private void RefreshProfilesFromList(List<ThirdPartyProfile> profiles, string preferredName)
        {
            loadingProfiles = true;
            try
            {
                currentProfiles = profiles ?? new List<ThirdPartyProfile>();
                profileBox.Items.Clear();
                profileBox.Items.Add("不使用档案");
                foreach (ThirdPartyProfile profile in currentProfiles)
                {
                    profileBox.Items.Add(profile.Name + "  ·  " + profile.BaseUrl);
                }

                int selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(preferredName))
                {
                    for (int i = 0; i < currentProfiles.Count; i++)
                    {
                        if (string.Equals(
                            currentProfiles[i].Name,
                            preferredName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i + 1;
                            break;
                        }
                    }
                }
                profileBox.SelectedIndex = selectedIndex;
            }
            finally
            {
                loadingProfiles = false;
            }

            deleteProfileButton.Enabled = GetSelectedProfile() != null;
            ApplyButtonState(deleteProfileButton, "normal");
            UpdateInputFeedback();
        }

        private void ProfileSelectionChanged(object sender, EventArgs e)
        {
            if (loadingProfiles)
            {
                return;
            }

            ThirdPartyProfile profile = GetSelectedProfile();
            deleteProfileButton.Enabled = profile != null;
            ApplyButtonState(deleteProfileButton, "normal");
            if (profile == null)
            {
                UpdateInputFeedback();
                return;
            }

            suppressValidationFeedback = true;
            try
            {
                urlBox.Text = profile.BaseUrl;
                thirdPartyModelBox.Text = profile.Model;
                keyBox.Text = string.Empty;
            }
            finally
            {
                suppressValidationFeedback = false;
            }
            UpdateInputFeedback();
        }

        private ThirdPartyProfile GetSelectedProfile()
        {
            int index = profileBox.SelectedIndex - 1;
            if (index >= 0 && index < currentProfiles.Count)
            {
                return currentProfiles[index];
            }
            return null;
        }

        private string GetSelectedProfileName()
        {
            ThirdPartyProfile profile = GetSelectedProfile();
            return profile == null ? string.Empty : profile.Name;
        }

        private string GetProfileNameForCurrentValues()
        {
            ThirdPartyProfile selected = GetSelectedProfile();
            if (selected != null)
            {
                return selected.Name;
            }

            string url = urlBox.Text.Trim();
            if (url.Length == 0)
            {
                return "未命名中转站";
            }

            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host;
                return string.IsNullOrWhiteSpace(host) ? url : host;
            }
            catch
            {
                return url;
            }
        }

        private void SaveCurrentProfile(object sender, EventArgs e)
        {
            RunAction(saveProfileButton, "正在保存中转站档案", delegate
            {
                string url = urlBox.Text.Trim().TrimEnd('/');
                string model = thirdPartyModelBox.Text.Trim();
                string key = keyBox.Text.Trim();
                string name = GetProfileNameForCurrentValues();
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(model))
                {
                    throw new InvalidOperationException("请填写第三方 Base URL 和模型名称。");
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("保存档案时需要填写 API Key；之后切换时可以留空。");
                }

                GetService().SaveThirdPartyProfile(name, url, model, key);
                keyBox.Text = string.Empty;
                RefreshProfiles(name);
                MessageBox.Show(
                    "已保存中转站档案「" + name + "」。下次可从下拉框直接选择。",
                    "档案已保存",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void DeleteCurrentProfile(object sender, EventArgs e)
        {
            ThirdPartyProfile profile = GetSelectedProfile();
            if (profile == null)
            {
                return;
            }

            DialogResult confirmed = MessageBox.Show(
                "确定删除中转站档案「" + profile.Name + "」吗？对应的本机加密 Key 也会删除。",
                "删除中转站档案",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmed != DialogResult.Yes)
            {
                return;
            }

            RunAction(deleteProfileButton, "正在删除中转站档案", delegate
            {
                GetService().DeleteThirdPartyProfile(profile.Name);
                RefreshProfiles(string.Empty);
            });
        }

        private void BrowseRoot(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择包含 config.toml 的 Codex 根目录";
                dialog.SelectedPath = Directory.Exists(rootBox.Text) ? rootBox.Text : Program.GetDefaultRoot();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    rootBox.Text = dialog.SelectedPath;
                    ReloadRootAndHotkeySettings();
                }
            }
        }

        private void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("打开界面", null, delegate { ShowFromTray(); });
            trayMenu.Items.Add("启动 Codex", null, LaunchCodex);
            trayMenu.Items.Add("关闭 Codex 后台", null, CloseCodex);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("退出 CAS", null, delegate
            {
                allowExit = true;
                Close();
            });

            trayIcon = new NotifyIcon();
            trayIcon.Icon = Icon;
            trayIcon.Text = "CAS - Codex API 切换器";
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = false;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowFromTray();
                }
            };
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
        }

        private void HideToTray()
        {
            if (trayIcon != null)
            {
                trayIcon.Visible = true;
            }
            ShowInTaskbar = false;
            Hide();
            if (!trayNoticeShown && trayIcon != null)
            {
                trayNoticeShown = true;
                trayIcon.ShowBalloonTip(
                    2500,
                    "CAS 仍在后台运行",
                    "点击托盘图标，或使用快捷键/鼠标侧键可重新打开界面。",
                    ToolTipIcon.Info);
            }
        }

        private void ShowFromTray()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(ShowFromTray));
                return;
            }

            if (!rootSettingsLoaded)
            {
                LoadRootSettings();
            }
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Show();
            ShowWindow(Handle, SwRestore);
            BringWindowToTop(Handle);
            TopMost = true;
            TopMost = false;
            Activate();
            SetForegroundWindow(Handle);
        }

        private void ToggleFromShortcut()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new MethodInvoker(ToggleFromShortcut));
                return;
            }

            if (Visible && WindowState != FormWindowState.Minimized)
            {
                HideToTray();
                return;
            }

            ShowFromTray();
        }

        private void LoadStartupState()
        {
            loadingStartupState = true;
            try
            {
                startupCheckBox.Checked = StartupManager.IsEnabled(Application.ExecutablePath);
            }
            catch
            {
                startupCheckBox.Checked = false;
            }
            finally
            {
                loadingStartupState = false;
            }
        }

        private void StartupSelectionChanged(object sender, EventArgs e)
        {
            if (loadingStartupState)
            {
                return;
            }

            try
            {
                StartupManager.SetEnabled(startupCheckBox.Checked, Application.ExecutablePath);
                statusLabel.BackColor = Color.FromArgb(236, 253, 243);
                statusLabel.ForeColor = SuccessGreen;
                statusLabel.Text = startupCheckBox.Checked
                    ? "已开启开机启动；下次登录 Windows 后会自动驻留托盘。"
                    : "已关闭开机启动。";
            }
            catch (Exception ex)
            {
                loadingStartupState = true;
                try
                {
                    startupCheckBox.Checked = !startupCheckBox.Checked;
                }
                finally
                {
                    loadingStartupState = false;
                }
                statusLabel.BackColor = Color.FromArgb(255, 241, 240);
                statusLabel.ForeColor = ErrorRed;
                statusLabel.Text = "开机启动设置失败：" + ex.Message;
                MessageBox.Show(ex.Message, "开机启动设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CompatibilitySelectionChanged(object sender, EventArgs e)
        {
            if (loadingCompatibilityState)
            {
                return;
            }

            try
            {
                statusLabel.BackColor = Color.FromArgb(238, 244, 255);
                statusLabel.ForeColor = BrandBlue;
                statusLabel.Text = compatibilityCheckBox.Checked
                    ? "第三方兼容模式已开启；下次切换第三方时会降低 Codex 工具能力暴露，减少 Image generation 403。"
                    : "第三方兼容模式已关闭；下次切换第三方时会恢复完整工具能力。";
            }
            catch (Exception ex)
            {
                loadingCompatibilityState = true;
                try
                {
                    compatibilityCheckBox.Checked = !compatibilityCheckBox.Checked;
                }
                finally
                {
                    loadingCompatibilityState = false;
                }
                statusLabel.BackColor = Color.FromArgb(255, 241, 240);
                statusLabel.ForeColor = ErrorRed;
                statusLabel.Text = "保存第三方兼容模式失败：" + ex.Message;
                MessageBox.Show(ex.Message, "第三方兼容模式设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void LoadHotkeySettings()
        {
            try
            {
                StoredSettings settings = GetService().LoadSettings();
                currentHotkey = HotkeySetting.ParseOrDefault(settings.OpenHotkey);
                currentMouseButton = MouseButtonSetting.ParseOrDefault(settings.OpenMouseButton);
            }
            catch
            {
                currentHotkey = HotkeySetting.Default;
                currentMouseButton = MouseButtonSetting.None;
            }

            hotkeyBox.Text = currentHotkey.ToDisplayString();
            loadingMouseButton = true;
            try
            {
                mouseButtonBox.SelectedIndex = currentMouseButton.ToComboIndex();
            }
            finally
            {
                loadingMouseButton = false;
            }
            RefreshMouseHook();
        }

        private void ReloadRootAndHotkeySettings()
        {
            rootSettingsLoaded = false;
            LoadRootSettings();
            LoadHotkeySettings();
            RegisterConfiguredHotkey();
        }

        private void BeginHotkeyCapture(object sender, EventArgs e)
        {
            capturingHotkey = true;
            hotkeyBox.BackColor = FieldFocus;
            hotkeyBox.Text = "请按组合键/侧键...";
            hotkeyBox.Focus();
            RefreshMouseHook();
            statusLabel.BackColor = Color.FromArgb(238, 244, 255);
            statusLabel.ForeColor = BrandBlue;
            statusLabel.Text = "请按下新的打开界面快捷键，例如 Ctrl+Alt+C；也可以按鼠标侧键。";
        }

        private void CaptureHotkey(object sender, KeyEventArgs e)
        {
            if (!capturingHotkey)
            {
                return;
            }

            e.SuppressKeyPress = true;
            ApplyCapturedHotkey(e.KeyData);
        }

        private void ApplyCapturedHotkey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape)
            {
                CancelHotkeyCapture();
                return;
            }

            if (IsModifierOnlyKey(keyData))
            {
                return;
            }

            HotkeySetting previous = currentHotkey;
            try
            {
                HotkeySetting candidate = HotkeySetting.FromKeys(keyData);
                UnregisterConfiguredHotkey();
                currentHotkey = candidate;
                RegisterConfiguredHotkey();
                if (!hotkeyRegistered)
                {
                    currentHotkey = previous;
                    RegisterConfiguredHotkey();
                    throw new InvalidOperationException("这个快捷键已被占用，请换一个组合键。");
                }

                GetService().SaveOpenHotkey(currentHotkey.ToDisplayString());
                capturingHotkey = false;
                hotkeyBox.BackColor = Color.White;
                hotkeyBox.Text = currentHotkey.ToDisplayString();
                statusLabel.BackColor = Color.FromArgb(236, 253, 243);
                statusLabel.ForeColor = SuccessGreen;
                statusLabel.Text = "打开界面快捷键已保存：" + currentHotkey.ToDisplayString();
            }
            catch (Exception ex)
            {
                currentHotkey = previous;
                capturingHotkey = false;
                hotkeyBox.BackColor = Color.White;
                hotkeyBox.Text = currentHotkey.ToDisplayString();
                statusLabel.BackColor = Color.FromArgb(255, 241, 240);
                statusLabel.ForeColor = ErrorRed;
                statusLabel.Text = "快捷键设置失败：" + ex.Message;
                MessageBox.Show(ex.Message, "快捷键设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CancelHotkeyCapture()
        {
            capturingHotkey = false;
            hotkeyBox.BackColor = Color.White;
            hotkeyBox.Text = currentHotkey.ToDisplayString();
            RefreshMouseHook();
            statusLabel.BackColor = Color.FromArgb(248, 250, 252);
            statusLabel.ForeColor = MutedInk;
            statusLabel.Text = "已取消快捷键设置。";
        }

        private void ApplyCapturedMouseButton(MouseButtonSetting mouseButton)
        {
            try
            {
                currentMouseButton = mouseButton;
                GetService().SaveOpenMouseButton(currentMouseButton.ToDisplayString());
                loadingMouseButton = true;
                try
                {
                    mouseButtonBox.SelectedIndex = currentMouseButton.ToComboIndex();
                }
                finally
                {
                    loadingMouseButton = false;
                }
                capturingHotkey = false;
                hotkeyBox.BackColor = Color.White;
                hotkeyBox.Text = currentHotkey.ToDisplayString();
                RefreshMouseHook();
                statusLabel.BackColor = Color.FromArgb(236, 253, 243);
                statusLabel.ForeColor = SuccessGreen;
                statusLabel.Text = "鼠标侧键唤出已保存：" + currentMouseButton.ToDisplayString();
            }
            catch (Exception ex)
            {
                capturingHotkey = false;
                hotkeyBox.BackColor = Color.White;
                hotkeyBox.Text = currentHotkey.ToDisplayString();
                RefreshMouseHook();
                statusLabel.BackColor = Color.FromArgb(255, 241, 240);
                statusLabel.ForeColor = ErrorRed;
                statusLabel.Text = "鼠标侧键设置失败：" + ex.Message;
                MessageBox.Show(ex.Message, "鼠标侧键设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void MouseButtonSelectionChanged(object sender, EventArgs e)
        {
            if (loadingMouseButton)
            {
                return;
            }

            try
            {
                currentMouseButton = MouseButtonSetting.FromComboIndex(mouseButtonBox.SelectedIndex);
                GetService().SaveOpenMouseButton(currentMouseButton.ToDisplayString());
                RefreshMouseHook();
                statusLabel.BackColor = Color.FromArgb(236, 253, 243);
                statusLabel.ForeColor = SuccessGreen;
                statusLabel.Text = currentMouseButton.IsEnabled
                    ? "鼠标侧键唤出已保存：" + currentMouseButton.ToDisplayString()
                    : "鼠标侧键唤出已关闭。";
            }
            catch (Exception ex)
            {
                statusLabel.BackColor = Color.FromArgb(255, 241, 240);
                statusLabel.ForeColor = ErrorRed;
                statusLabel.Text = "鼠标侧键设置失败：" + ex.Message;
                MessageBox.Show(ex.Message, "鼠标侧键设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshMouseHook()
        {
            bool shouldHook = (currentMouseButton != null && currentMouseButton.IsEnabled) || capturingHotkey;
            if (!shouldHook)
            {
                UnregisterMouseHook();
                return;
            }
            if (mouseHookHandle != IntPtr.Zero)
            {
                return;
            }

            mouseHookHandle = SetWindowsHookEx(
                WhMouseLl,
                mouseHookProcedure,
                GetModuleHandle(null),
                0);
            if (mouseHookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                statusLabel.BackColor = Color.FromArgb(255, 251, 235);
                statusLabel.ForeColor = WarningAmber;
                statusLabel.Text = "鼠标侧键监听未启动（错误 " + error + "）。键盘快捷键仍可使用。";
            }
        }

        private void UnregisterMouseHook()
        {
            if (mouseHookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(mouseHookHandle);
                mouseHookHandle = IntPtr.Zero;
            }
        }

        private void PauseInteractiveWorkForMove()
        {
            if (inWindowMoveLoop)
            {
                return;
            }

            inWindowMoveLoop = true;
            buttonAnimationTimer.Stop();
            mouseHookPausedForMove = mouseHookHandle != IntPtr.Zero;
            if (mouseHookPausedForMove)
            {
                UnregisterMouseHook();
            }
        }

        private void ResumeInteractiveWorkAfterMove()
        {
            if (!inWindowMoveLoop)
            {
                return;
            }

            inWindowMoveLoop = false;
            if (mouseHookPausedForMove)
            {
                mouseHookPausedForMove = false;
                RefreshMouseHook();
            }

            MethodInvoker pending = deferredMoveUiAction;
            deferredMoveUiAction = null;
            if (pending != null)
            {
                pending();
            }

            foreach (Button button in buttonFeedback.Keys.ToList())
            {
                ApplyButtonState(button, "normal");
            }
        }

        private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code == HcAction && wParam.ToInt32() == WmXButtonDown)
            {
                LowLevelMouseHookStruct hook =
                    (LowLevelMouseHookStruct)Marshal.PtrToStructure(
                        lParam,
                        typeof(LowLevelMouseHookStruct));
                int xButton = (int)((hook.MouseData >> 16) & 0xffff);
                MouseButtonSetting pressed = MouseButtonSetting.FromXButton(xButton);
                if (pressed.IsEnabled)
                {
                    if (capturingHotkey)
                    {
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            ApplyCapturedMouseButton(pressed);
                        }));
                        return new IntPtr(1);
                    }

                    if (currentMouseButton != null && currentMouseButton.Equals(pressed))
                    {
                        BeginInvoke(new MethodInvoker(ToggleFromShortcut));
                        return new IntPtr(1);
                    }
                }
            }

            return CallNextHookEx(mouseHookHandle, code, wParam, lParam);
        }

        private static bool IsModifierOnlyKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            return keyCode == Keys.ControlKey ||
                keyCode == Keys.Menu ||
                keyCode == Keys.ShiftKey ||
                keyCode == Keys.LWin ||
                keyCode == Keys.RWin;
        }

        private void RegisterConfiguredHotkey()
        {
            UnregisterConfiguredHotkey();
            registeringHotkey = true;
            try
            {
                uint modifiers = currentHotkey.ToModifierFlags() | ModNoRepeat;
                hotkeyWindowHandle = Handle;
                hotkeyRegistered = RegisterHotKey(
                    hotkeyWindowHandle,
                    OpenWindowHotkeyId,
                    modifiers,
                    currentHotkey.ToVirtualKey());
                if (!hotkeyRegistered)
                {
                    int error = Marshal.GetLastWin32Error();
                    statusLabel.BackColor = Color.FromArgb(255, 251, 235);
                    statusLabel.ForeColor = WarningAmber;
                    statusLabel.Text = "快捷键未注册，可能已被占用：" + currentHotkey.ToDisplayString() + "（错误 " + error + "）";
                }
            }
            finally
            {
                registeringHotkey = false;
            }
        }

        private void UnregisterConfiguredHotkey()
        {
            if (hotkeyRegistered)
            {
                UnregisterHotKey(hotkeyWindowHandle, OpenWindowHotkeyId);
                hotkeyRegistered = false;
                hotkeyWindowHandle = IntPtr.Zero;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!registeringHotkey && !hotkeyRegistered)
            {
                RegisterConfiguredHotkey();
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterConfiguredHotkey();
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit &&
                e.CloseReason != CloseReason.WindowsShutDown &&
                e.CloseReason != CloseReason.ApplicationExitCall)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            base.OnFormClosing(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (startHiddenToTray)
            {
                BeginInvoke(new MethodInvoker(HideToTray));
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            UnregisterConfiguredHotkey();
            UnregisterMouseHook();
            buttonAnimationTimer.Stop();
            buttonAnimationTimer.Dispose();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayIcon = null;
            }
            if (trayMenu != null)
            {
                trayMenu.Dispose();
                trayMenu = null;
            }

            base.OnFormClosed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                HideToTray();
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam.ToInt32() == OpenWindowHotkeyId)
            {
                ToggleFromShortcut();
                return;
            }
            if (message.Msg == WmEnterSizeMove)
            {
                PauseInteractiveWorkForMove();
            }
            else if (message.Msg == WmExitSizeMove)
            {
                ResumeInteractiveWorkAfterMove();
            }

            base.WndProc(ref message);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (capturingHotkey)
            {
                ApplyCapturedHotkey(keyData);
                return true;
            }

            return base.ProcessCmdKey(ref message, keyData);
        }

        private SwitcherService GetService()
        {
            return new SwitcherService(rootBox.Text.Trim(), Application.ExecutablePath);
        }

        private void ShowInitialLoadingState()
        {
            statusLabel.Text = "正在后台读取 Codex 配置...";
            statusLabel.ForeColor = BrandBlue;
            statusLabel.BackColor = Color.FromArgb(238, 244, 255);
            keyStateLabel.Text = "配置加载完成后即可切换。";
            keyStateLabel.ForeColor = MutedInk;
            compatibilityCheckBox.Checked = false;
            urlBox.Text = "https://api.example.com";
            thirdPartyModelBox.Text = "gpt-5.5";
            officialModelBox.Text = "gpt-5.5";
            profileBox.Items.Clear();
            profileBox.Items.Add("不使用档案");
            profileBox.SelectedIndex = 0;
            SetButtonsEnabled(false);
            browseButton.Enabled = true;
            launchCodexButton.Enabled = true;
            closeCodexButton.Enabled = true;
            hotkeyButton.Enabled = true;
            rootBox.ReadOnly = false;
            ApplyButtonState(browseButton, "normal");
            ApplyButtonState(launchCodexButton, "normal");
            ApplyButtonState(closeCodexButton, "normal");
            ApplyButtonState(hotkeyButton, "normal");
        }

        private void SafeBeginInvoke(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void RunWhenWindowIsIdle(MethodInvoker action)
        {
            if (inWindowMoveLoop)
            {
                deferredMoveUiAction = action;
                return;
            }

            action();
        }

        private void BeginLoadRootSettings()
        {
            string rootSnapshot = rootBox.Text.Trim();
            backupLocationLabel.Text = "备份位置：" +
                Path.Combine(rootSnapshot, "config-switcher-backups") + "；" +
                Path.Combine(rootSnapshot, "history_sync_backups");
            statusLabel.Text = "正在后台读取 Codex 配置...";
            statusLabel.ForeColor = BrandBlue;
            statusLabel.BackColor = Color.FromArgb(238, 244, 255);

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    SwitcherService service = new SwitcherService(rootSnapshot, Application.ExecutablePath);
                    ProviderStatus status = service.GetStatus();
                    StoredSettings settings = service.LoadSettings();
                    bool storedToken = service.HasStoredToken();
                    List<ThirdPartyProfile> profiles = service.LoadThirdPartyProfiles();
                    SafeBeginInvoke(new MethodInvoker(delegate
                    {
                        if (!string.Equals(rootBox.Text.Trim(), rootSnapshot, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        RunWhenWindowIsIdle(new MethodInvoker(delegate
                        {
                            ApplyRootSettings(status, settings, storedToken, profiles);
                        }));
                    }));
                }
                catch (Exception ex)
                {
                    SafeBeginInvoke(new MethodInvoker(delegate
                    {
                        if (!string.Equals(rootBox.Text.Trim(), rootSnapshot, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        RunWhenWindowIsIdle(new MethodInvoker(delegate
                        {
                            ApplyRootSettingsError(ex);
                        }));
                    }));
                }
            });
        }

        private void ApplyRootSettings(
            ProviderStatus status,
            StoredSettings settings,
            bool storedToken,
            List<ThirdPartyProfile> profiles)
        {
            suppressValidationFeedback = true;
            try
            {
                urlBox.Text = !string.IsNullOrWhiteSpace(settings.BaseUrl)
                    ? settings.BaseUrl
                    : (!string.IsNullOrWhiteSpace(status.BaseUrl) ? status.BaseUrl : "https://api.example.com");
                thirdPartyModelBox.Text = !string.IsNullOrWhiteSpace(settings.ThirdPartyModel)
                    ? settings.ThirdPartyModel
                    : (!string.IsNullOrWhiteSpace(status.Model) ? status.Model : "gpt-5.5");
                officialModelBox.Text = !string.IsNullOrWhiteSpace(settings.OfficialModel)
                    ? settings.OfficialModel
                    : "gpt-5.5";
                loadingCompatibilityState = true;
                compatibilityCheckBox.Checked = false;
                loadingCompatibilityState = false;
                keyBox.Text = string.Empty;
                hasStoredToken = storedToken;
                RefreshProfilesFromList(profiles, string.Empty);
            }
            finally
            {
                suppressValidationFeedback = false;
            }
            rootSettingsLoaded = true;
            UpdateInputFeedback();
            RenderStatus(status);
            SetButtonsEnabled(true);
        }

        private void ApplyRootSettingsError(Exception ex)
        {
            suppressValidationFeedback = false;
            rootSettingsLoaded = false;
            statusLabel.Text = "无法读取配置：" + ex.Message;
            statusLabel.ForeColor = ErrorRed;
            statusLabel.BackColor = Color.FromArgb(255, 241, 240);
            keyStateLabel.Text = string.Empty;
            SetButtonsEnabled(false);
            browseButton.Enabled = true;
            launchCodexButton.Enabled = true;
            closeCodexButton.Enabled = true;
            hotkeyButton.Enabled = true;
            saveProfileButton.Enabled = false;
            deleteProfileButton.Enabled = false;
            rootBox.ReadOnly = false;
            ApplyButtonState(browseButton, "normal");
            ApplyButtonState(launchCodexButton, "normal");
            ApplyButtonState(closeCodexButton, "normal");
            ApplyButtonState(hotkeyButton, "normal");
            ApplyButtonState(saveProfileButton, "normal");
            ApplyButtonState(deleteProfileButton, "normal");
        }

        private void LoadRootSettings()
        {
            string selectedRoot = rootBox.Text.Trim();
            backupLocationLabel.Text = "备份位置：" +
                Path.Combine(selectedRoot, "config-switcher-backups") + "；" +
                Path.Combine(selectedRoot, "history_sync_backups");

            try
            {
                suppressValidationFeedback = true;
                SwitcherService service = GetService();
                ProviderStatus status = service.GetStatus();
                StoredSettings settings = service.LoadSettings();

                urlBox.Text = !string.IsNullOrWhiteSpace(settings.BaseUrl)
                    ? settings.BaseUrl
                    : (!string.IsNullOrWhiteSpace(status.BaseUrl) ? status.BaseUrl : "https://api.example.com");
                thirdPartyModelBox.Text = !string.IsNullOrWhiteSpace(settings.ThirdPartyModel)
                    ? settings.ThirdPartyModel
                    : (!string.IsNullOrWhiteSpace(status.Model) ? status.Model : "gpt-5.5");
                officialModelBox.Text = !string.IsNullOrWhiteSpace(settings.OfficialModel)
                    ? settings.OfficialModel
                    : "gpt-5.5";
                loadingCompatibilityState = true;
                compatibilityCheckBox.Checked = false;
                loadingCompatibilityState = false;
                keyBox.Text = string.Empty;
                hasStoredToken = service.HasStoredToken();
                RefreshProfiles(string.Empty);
                suppressValidationFeedback = false;
                UpdateInputFeedback();
                RenderStatus(status);
                rootSettingsLoaded = true;
                SetButtonsEnabled(true);
            }
            catch (Exception ex)
            {
                suppressValidationFeedback = false;
                rootSettingsLoaded = false;
                statusLabel.Text = "无法读取配置：" + ex.Message;
                statusLabel.ForeColor = ErrorRed;
                statusLabel.BackColor = Color.FromArgb(255, 241, 240);
                keyStateLabel.Text = string.Empty;
                SetButtonsEnabled(false);
                browseButton.Enabled = true;
                launchCodexButton.Enabled = true;
                closeCodexButton.Enabled = true;
                hotkeyButton.Enabled = true;
                saveProfileButton.Enabled = false;
                deleteProfileButton.Enabled = false;
                rootBox.ReadOnly = false;
                ApplyButtonState(browseButton, "normal");
                ApplyButtonState(launchCodexButton, "normal");
                ApplyButtonState(closeCodexButton, "normal");
                ApplyButtonState(hotkeyButton, "normal");
                ApplyButtonState(saveProfileButton, "normal");
                ApplyButtonState(deleteProfileButton, "normal");
            }
        }

        private void RenderStatus(ProviderStatus status)
        {
            statusLabel.ForeColor = status.IsThirdParty ? BrandBlue : Ink;
            statusLabel.BackColor = status.IsThirdParty
                ? Color.FromArgb(238, 244, 255)
                : Color.FromArgb(248, 250, 252);
            statusLabel.Text = "当前状态：" + status.ToDisplayString();
        }

        private void EnsureRootSettingsReady()
        {
            if (!rootSettingsLoaded)
            {
                throw new InvalidOperationException("配置尚未加载完成或读取失败，请确认 Codex 根目录可读取后再操作。");
            }
        }

        private bool TryEnsureRootSettingsReady()
        {
            if (rootSettingsLoaded)
            {
                return true;
            }

            statusLabel.BackColor = Color.FromArgb(255, 251, 235);
            statusLabel.ForeColor = WarningAmber;
            statusLabel.Text = "配置尚未加载完成或读取失败，请确认 Codex 根目录可读取后再操作。";
            return false;
        }

        private void SetButtonsEnabled(bool enabled)
        {
            officialButton.Enabled = enabled;
            thirdPartyButton.Enabled = enabled;
            rollbackButton.Enabled = enabled;
            repairButton.Enabled = enabled;
            resetConfigButton.Enabled = enabled;
            launchCodexButton.Enabled = enabled;
            closeCodexButton.Enabled = enabled;
            hotkeyButton.Enabled = enabled;
            browseButton.Enabled = enabled;
            saveProfileButton.Enabled = enabled;
            deleteProfileButton.Enabled = enabled && GetSelectedProfile() != null;
            profileBox.Enabled = enabled;
            mouseButtonBox.Enabled = true;
            startupCheckBox.Enabled = true;
            compatibilityCheckBox.Enabled = enabled;
            rootBox.ReadOnly = !enabled;
            urlBox.ReadOnly = !enabled;
            thirdPartyModelBox.ReadOnly = !enabled;
            officialModelBox.ReadOnly = !enabled;
            keyBox.ReadOnly = !enabled;

            foreach (Button button in buttonFeedback.Keys.ToList())
            {
                ApplyButtonState(button, "normal");
            }
        }

        private void SwitchThirdParty(object sender, EventArgs e)
        {
            if (!TryEnsureRootSettingsReady()) return;
            RunAction(thirdPartyButton, "正在切换到第三方 API", delegate
            {
                string url = urlBox.Text.Trim().TrimEnd('/');
                string model = thirdPartyModelBox.Text.Trim();
                string key = keyBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(model))
                {
                    throw new InvalidOperationException("请填写第三方 Base URL 和模型名称。");
                }

                SwitcherService service = GetService();
                string profileName = GetSelectedProfileName();
                if (string.IsNullOrWhiteSpace(key)
                    && profileName.Length == 0
                    && !service.HasStoredToken())
                {
                    throw new InvalidOperationException("首次切换第三方 API 时必须填写 API Key。");
                }

                SwitchOutcome outcome = service.SwitchToThirdParty(
                    url,
                    model,
                    key,
                    profileName,
                    compatibilityCheckBox.Checked);
                keyBox.Text = string.Empty;
                MessageBox.Show(
                    "已切换到第三方 Responses API。" +
                    outcome.ToUserNotice() + "\n\n请重新打开 Codex。",
                    "切换完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void SwitchOfficial(object sender, EventArgs e)
        {
            if (!TryEnsureRootSettingsReady()) return;
            RunAction(officialButton, "正在切换到官方登录", delegate
            {
                string model = officialModelBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(model))
                {
                    throw new InvalidOperationException("请填写官方模型名称。");
                }

                SwitchOutcome outcome = GetService().SwitchToOfficial(model);
                MessageBox.Show(
                    "已切换到官方 OpenAI 登录。" +
                    outcome.ToUserNotice() +
                    "\n\n第三方 Key 仍以加密形式保留，auth.json 未被修改。\n请重新打开 Codex。",
                    "切换完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void Rollback(object sender, EventArgs e)
        {
            if (!TryEnsureRootSettingsReady()) return;
            RunAction(rollbackButton, "正在恢复最近备份", delegate
            {
                GetService().Rollback();
                MessageBox.Show(
                    "已恢复最近一次备份。\n\n请彻底退出并重新打开 Codex。",
                    "恢复完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void RepairSidebar(object sender, EventArgs e)
        {
            if (!TryEnsureRootSettingsReady()) return;
            DialogResult confirmed = MessageBox.Show(
                "此操作不是普通 API 切换。\n\n它会自动识别并备份新旧状态数据库，然后恢复顶层用户会话的可见标记。不会改动会话 JSONL、记忆文件或 auth.json。\n\n请先彻底退出 Codex，再点击“是”。",
                "确认修复会话列表",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmed != DialogResult.Yes)
            {
                return;
            }

            RunAction(repairButton, "正在修复会话列表", delegate
            {
                string result = GetService().RepairConversationIndex();
                MessageBox.Show(
                    result + "\n\n请重新打开 Codex 检查侧栏。",
                    "修复完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            });
        }

        private void ResetConfig(object sender, EventArgs e)
        {
            if (!TryEnsureRootSettingsReady()) return;
            string model = officialModelBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "gpt-5.5";
            }

            DialogResult confirmed = MessageBox.Show(
                "将先完整备份 config.toml，然后重建官方模型配置。\n\n" +
                "会恢复 model_provider 和 model，移除损坏的 custom provider 段；MCP、插件、沙箱、记忆、会话和 auth.json 均保持不变。\n\n" +
                "确定继续吗？",
                "确认恢复基础配置",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirmed != DialogResult.Yes)
            {
                return;
            }

            RunAction(resetConfigButton, "正在恢复基础配置", delegate
            {
                GetService().ResetModelConfiguration(model);
                MessageBox.Show(
                    "基础模型配置已恢复为官方 OpenAI 登录。\n\n原 config.toml 已备份，MCP 和其他无关配置已保留。请重新打开 Codex。",
                    "恢复完成",
                    MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            });
        }

        private void CloseCodex(object sender, EventArgs e)
        {
            RunAction(closeCodexButton, "正在关闭 Codex 后台", delegate
            {
                string result = GetService().CloseCodexProcesses(false);
                MessageBox.Show(result, "关闭 Codex 后台", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private void LaunchCodex(object sender, EventArgs e)
        {
            RunAction(launchCodexButton, "正在启动 Codex", delegate
            {
                string result = GetService().LaunchCodex();
                MessageBox.Show(result, "启动 Codex", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private void RunAction(Button sourceButton, string busyText, Action action)
        {
            string originalButtonText = sourceButton == null ? string.Empty : sourceButton.Text;
            Cursor previousCursor = Cursor.Current;
            bool restoreButtons = true;
            try
            {
                SetButtonsEnabled(false);
                if (sourceButton != null)
                {
                    sourceButton.Text = GetBusyButtonText(originalButtonText);
                    ApplyButtonState(sourceButton, "pressed");
                }
                statusLabel.BackColor = Color.FromArgb(238, 244, 255);
                statusLabel.ForeColor = BrandBlue;
                statusLabel.Text = busyText + "，请稍候。";
                Cursor.Current = Cursors.WaitCursor;
                Application.DoEvents();

                action();
                LoadRootSettings();
                restoreButtons = false;
            }
            catch (Exception ex)
            {
                statusLabel.BackColor = Color.FromArgb(255, 241, 240);
                statusLabel.ForeColor = ErrorRed;
                statusLabel.Text = "操作失败：" + ex.Message;
                MessageBox.Show(ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetButtonsEnabled(true);
            }
            finally
            {
                if (sourceButton != null)
                {
                    sourceButton.Text = originalButtonText;
                }
                Cursor.Current = previousCursor;
                if (restoreButtons)
                {
                    SetButtonsEnabled(true);
                }
            }
        }

        private static string GetBusyButtonText(string originalText)
        {
            if (string.IsNullOrWhiteSpace(originalText))
            {
                return "处理中...";
            }
            return originalText.Length <= 6 ? "处理中..." : "正在处理...";
        }
    }

    internal sealed class ButtonFeedback
    {
        internal readonly Color NormalBack;
        internal readonly Color HoverBack;
        internal readonly Color PressedBack;
        internal readonly Color ForeColor;
        internal Color TargetBack;

        internal ButtonFeedback(Color normalBack, Color hoverBack, Color pressedBack, Color foreColor)
        {
            NormalBack = normalBack;
            HoverBack = hoverBack;
            PressedBack = pressedBack;
            ForeColor = foreColor;
            TargetBack = normalBack;
        }
    }

    internal static class CasLogoIcon
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        internal static Icon CreateIcon(int size)
        {
            using (Bitmap bitmap = CreateBitmap(size))
            {
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon icon = Icon.FromHandle(handle))
                    {
                        return (Icon)icon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        internal static Bitmap CreateBitmap(int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                float scale = size / 64F;
                RectangleF outer = new RectangleF(5 * scale, 5 * scale, 54 * scale, 54 * scale);
                using (GraphicsPath outerPath = RoundedRectangle(outer, 14 * scale))
                using (SolidBrush background = new SolidBrush(Color.White))
                {
                    graphics.FillPath(background, outerPath);
                }

                RectangleF inset = new RectangleF(10 * scale, 10 * scale, 44 * scale, 44 * scale);
                using (GraphicsPath insetPath = RoundedRectangle(inset, 10 * scale))
                {
                    graphics.SetClip(insetPath);
                    using (SolidBrush left = new SolidBrush(Color.FromArgb(210, 226, 255)))
                    using (SolidBrush right = new SolidBrush(Color.FromArgb(185, 238, 238)))
                    {
                        graphics.FillRectangle(left, inset.Left, inset.Top, inset.Width / 2F, inset.Height);
                        graphics.FillRectangle(right, inset.Left + inset.Width / 2F, inset.Top, inset.Width / 2F, inset.Height);
                    }
                    graphics.ResetClip();
                }

                using (Pen divider = new Pen(Color.FromArgb(202, 213, 226), Math.Max(1F, 1.5F * scale)))
                {
                    graphics.DrawLine(
                        divider,
                        size / 2F,
                        13 * scale,
                        size / 2F,
                        size - 13 * scale);
                }

                using (GraphicsPath outerPath = RoundedRectangle(outer, 14 * scale))
                using (Pen border = new Pen(Color.FromArgb(23, 32, 51), Math.Max(2F, 3F * scale)))
                {
                    graphics.DrawPath(border, outerPath);
                }

                using (Font font = new Font("Segoe UI", 17F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush text = new SolidBrush(Color.FromArgb(23, 32, 51)))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    graphics.DrawString("CAS", font, text, new RectangleF(0, 1 * scale, size, size), format);
                }
            }

            return bitmap;
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = radius * 2F;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class SwitcherService
    {
        private const string ProviderId = "custom";
        private const uint WmSettingChange = 0x001A;
        private const uint SmtoAbortIfHung = 0x0002;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodexApiSwitcher-v1");
        private static readonly byte[] ProfileEntropy =
            Encoding.UTF8.GetBytes("CodexApiSwitcher-profile-v1");
        private static readonly byte[] CodexApiKeyEnvironmentEntropy =
            Encoding.UTF8.GetBytes("CodexApiSwitcher-CODEX_API_KEY-v1");
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr windowHandle,
            uint message,
            UIntPtr wParam,
            string lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);

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

        internal SwitcherService(string rootPath, string exePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new InvalidOperationException("请选择 Codex 根目录。");
            }

            root = Path.GetFullPath(rootPath);
            executablePath = Path.GetFullPath(exePath);
            configPath = Path.Combine(root, "config.toml");
            dataDirectory = Path.Combine(root, "api-switcher");
            credentialPath = Path.Combine(dataDirectory, "credential.dat");
            settingsPath = Path.Combine(dataDirectory, "settings.dat");
            profilesPath = Path.Combine(dataDirectory, "profiles.dat");
            profileCredentialDirectory = Path.Combine(dataDirectory, "profiles");
            backupDirectory = Path.Combine(root, "config-switcher-backups");
            stableHelperPath = Path.Combine(dataDirectory, "CodexApiSwitcher.AuthHelper.exe");
            codexApiKeyEnvironmentBackupPath = Path.Combine(
                dataDirectory,
                "codex-api-key.user-env.dat");
            compatibilityBackupPath = Path.Combine(
                dataDirectory,
                "third-party-compatibility.dat");
        }

        internal ProviderStatus GetStatus()
        {
            List<string> lines = ReadConfig();
            string provider = GetTopLevelValue(lines, "model_provider");
            string model = GetTopLevelValue(lines, "model");
            string section = "model_providers." + ProviderId;
            string url = GetSectionValue(lines, section, "base_url");
            bool helperAuth = SectionExists(lines, section + ".auth");
            bool reusedLogin = string.Equals(
                GetSectionValue(lines, section, "requires_openai_auth"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            bool apiKeyOverride = IsRealCodexRoot() && HasCodexApiKeyEnvironmentOverride();
            return new ProviderStatus(
                provider,
                model,
                url,
                helperAuth,
                reusedLogin,
                apiKeyOverride);
        }

        internal StoredSettings LoadSettings()
        {
            if (!File.Exists(settingsPath))
            {
                return new StoredSettings();
            }

            string[] lines = File.ReadAllLines(settingsPath, Encoding.UTF8);
            StoredSettings settings = new StoredSettings();
            foreach (string line in lines)
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, separator);
                string encoded = line.Substring(separator + 1);
                string value;
                try
                {
                    value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                }
                catch
                {
                    continue;
                }

                if (name == "url") settings.BaseUrl = value;
                if (name == "thirdModel") settings.ThirdPartyModel = value;
                if (name == "officialModel") settings.OfficialModel = value;
                if (name == "openHotkey") settings.OpenHotkey = value;
                if (name == "openMouseButton") settings.OpenMouseButton = value;
                if (name == "thirdPartyCompatibilityMode")
                {
                    settings.ThirdPartyCompatibilityMode =
                        value == "1"
                        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
                }
            }
            return settings;
        }

        internal bool HasStoredToken()
        {
            return File.Exists(credentialPath) && new FileInfo(credentialPath).Length > 0;
        }

        internal string ReadToken()
        {
            if (!HasStoredToken())
            {
                throw new InvalidOperationException("No encrypted third-party API key is stored.");
            }

            byte[] encrypted = File.ReadAllBytes(credentialPath);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        private bool HasCodexApiKeyEnvironmentOverride()
        {
            return !string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(
                        "CODEX_API_KEY",
                        EnvironmentVariableTarget.Process))
                || !string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(
                        "CODEX_API_KEY",
                        EnvironmentVariableTarget.User))
                || !string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(
                        "CODEX_API_KEY",
                        EnvironmentVariableTarget.Machine));
        }

        private bool UseProcessEnvironmentScopeForTests()
        {
            return !IsRealCodexRoot()
                && string.Equals(
                    Environment.GetEnvironmentVariable(
                        "CODEX_SWITCHER_TEST_PROCESS_ENV"),
                    "1",
                    StringComparison.Ordinal);
        }

        private bool ShouldManageCodexApiKeyEnvironment()
        {
            return IsRealCodexRoot() || UseProcessEnvironmentScopeForTests();
        }

        private CodexApiKeyEnvironmentChange CaptureAndClearCodexApiKeyEnvironmentOverride()
        {
            if (!ShouldManageCodexApiKeyEnvironment())
            {
                return CodexApiKeyEnvironmentChange.None;
            }

            bool processScope = UseProcessEnvironmentScopeForTests();
            if (!processScope && !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "CODEX_API_KEY",
                    EnvironmentVariableTarget.Machine)))
            {
                throw new InvalidOperationException(
                    "检测到系统级 CODEX_API_KEY，它会覆盖 ChatGPT 官方登录。请先在 Windows 系统环境变量中移除它，再切换到官方登录。");
            }

            EnvironmentVariableTarget target = processScope
                ? EnvironmentVariableTarget.Process
                : EnvironmentVariableTarget.User;
            string originalTarget = Environment.GetEnvironmentVariable(
                "CODEX_API_KEY",
                target);
            string originalProcess = Environment.GetEnvironmentVariable(
                "CODEX_API_KEY",
                EnvironmentVariableTarget.Process);
            string valueToBackup = !string.IsNullOrWhiteSpace(originalTarget)
                ? originalTarget
                : originalProcess;
            if (string.IsNullOrWhiteSpace(valueToBackup))
            {
                return CodexApiKeyEnvironmentChange.None;
            }

            SaveProtectedText(
                codexApiKeyEnvironmentBackupPath,
                valueToBackup,
                CodexApiKeyEnvironmentEntropy);
            try
            {
                Environment.SetEnvironmentVariable("CODEX_API_KEY", null, target);
                if (target != EnvironmentVariableTarget.Process)
                {
                    Environment.SetEnvironmentVariable(
                        "CODEX_API_KEY",
                        null,
                        EnvironmentVariableTarget.Process);
                    BroadcastEnvironmentChange();
                }
            }
            catch
            {
                Environment.SetEnvironmentVariable("CODEX_API_KEY", originalTarget, target);
                if (target != EnvironmentVariableTarget.Process)
                {
                    Environment.SetEnvironmentVariable(
                        "CODEX_API_KEY",
                        originalProcess,
                        EnvironmentVariableTarget.Process);
                    BroadcastEnvironmentChange();
                }
                throw;
            }

            return new CodexApiKeyEnvironmentChange(
                true,
                delegate
                {
                    Environment.SetEnvironmentVariable(
                        "CODEX_API_KEY",
                        originalTarget,
                        target);
                    if (target != EnvironmentVariableTarget.Process)
                    {
                        Environment.SetEnvironmentVariable(
                            "CODEX_API_KEY",
                            originalProcess,
                            EnvironmentVariableTarget.Process);
                        BroadcastEnvironmentChange();
                    }
                });
        }

        private bool RestoreCodexApiKeyEnvironmentOverride()
        {
            if (!ShouldManageCodexApiKeyEnvironment()
                || !File.Exists(codexApiKeyEnvironmentBackupPath))
            {
                return false;
            }

            EnvironmentVariableTarget target = UseProcessEnvironmentScopeForTests()
                ? EnvironmentVariableTarget.Process
                : EnvironmentVariableTarget.User;
            if (!string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("CODEX_API_KEY", target)))
            {
                return false;
            }

            string value = ReadProtectedText(
                codexApiKeyEnvironmentBackupPath,
                CodexApiKeyEnvironmentEntropy);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Environment.SetEnvironmentVariable("CODEX_API_KEY", value, target);
            if (target != EnvironmentVariableTarget.Process)
            {
                Environment.SetEnvironmentVariable(
                    "CODEX_API_KEY",
                    value,
                    EnvironmentVariableTarget.Process);
                BroadcastEnvironmentChange();
            }
            return true;
        }

        private void SaveProtectedText(string path, string value, byte[] entropy)
        {
            byte[] plain = Encoding.UTF8.GetBytes(value ?? string.Empty);
            byte[] encrypted = ProtectedData.Protect(
                plain,
                entropy,
                DataProtectionScope.CurrentUser);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                WriteBytesAtomically(path, encrypted);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        private string ReadProtectedText(string path, byte[] entropy)
        {
            byte[] encrypted = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(
                encrypted,
                entropy,
                DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        private static void BroadcastEnvironmentChange()
        {
            try
            {
                UIntPtr result;
                SendMessageTimeout(
                    new IntPtr(0xffff),
                    WmSettingChange,
                    UIntPtr.Zero,
                    "Environment",
                    SmtoAbortIfHung,
                    2000,
                    out result);
            }
            catch
            {
            }
        }

        internal string GetCodexLaunchPlan()
        {
            LaunchTarget target = ResolveCodexLaunchTarget();
            return "Codex launch target: " + target.DisplayName;
        }

        internal string LaunchCodex()
        {
            if (IsCodexRunning())
            {
                return "Codex 已经在运行。";
            }

            LaunchTarget target = ResolveCodexLaunchTarget();
            Process.Start(target.CreateStartInfo());
            return "已发送 Codex 启动请求：" + target.DisplayName;
        }

        internal string CloseCodexProcesses(bool dryRun)
        {
            List<Process> processes = GetCodexProcesses();
            if (dryRun)
            {
                int count = processes.Count;
                DisposeProcesses(processes);
                return "Would close " + count + " Codex process(es).";
            }

            if (processes.Count == 0)
            {
                return "未发现正在运行的 Codex 进程。";
            }

            int closed = 0;
            List<string> failures = new List<string>();
            foreach (Process process in processes)
            {
                try
                {
                    if (CloseCodexProcess(process))
                    {
                        closed++;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add(SafeProcessName(process) + ": " + ex.Message);
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    "已关闭 " + closed + " 个 Codex 进程，但有 " + failures.Count +
                    " 个关闭失败：" + string.Join("；", failures.ToArray()));
            }

            return "已关闭 " + closed + " 个 Codex 进程。";
        }

        internal void SaveOpenHotkey(string hotkey)
        {
            StoredSettings settings = LoadSettings();
            settings.OpenHotkey = HotkeySetting.Parse(hotkey).ToDisplayString();
            SaveSettings(settings);
        }

        internal void SaveOpenMouseButton(string mouseButton)
        {
            StoredSettings settings = LoadSettings();
            settings.OpenMouseButton = MouseButtonSetting.Parse(mouseButton).ToDisplayString();
            SaveSettings(settings);
        }

        internal void SaveThirdPartyCompatibilityMode(bool enabled)
        {
            StoredSettings settings = LoadSettings();
            settings.ThirdPartyCompatibilityMode = enabled;
            SaveSettings(settings);
        }

        internal SwitchOutcome SwitchToThirdParty(string url, string model, string key)
        {
            return SwitchToThirdParty(url, model, key, string.Empty, false);
        }

        internal SwitchOutcome SwitchToThirdParty(string url, string model, string key, string profileName)
        {
            return SwitchToThirdParty(url, model, key, profileName, false);
        }

        internal SwitchOutcome SwitchToThirdParty(
            string url,
            string model,
            string key,
            string profileName,
            bool compatibilityMode)
        {
            AssertConfig();
            AssertCodexIsStopped();
            string cleanUrl = NormalizeBaseUrl(url);
            string cleanModel = (model ?? string.Empty).Trim();
            if (cleanUrl.Length == 0 || cleanModel.Length == 0)
            {
                throw new InvalidOperationException("Base URL and model are required.");
            }
            if (!Uri.IsWellFormedUriString(cleanUrl, UriKind.Absolute))
            {
                throw new InvalidOperationException("Base URL is not a valid absolute URL.");
            }

            Directory.CreateDirectory(dataDirectory);
            string cleanKey = (key ?? string.Empty).Trim();
            string cleanProfileName = (profileName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(cleanKey))
            {
                SaveToken(cleanKey);
                string effectiveProfileName = string.IsNullOrWhiteSpace(cleanProfileName)
                    ? GetDefaultProfileName(cleanUrl)
                    : cleanProfileName;
                SaveThirdPartyProfile(effectiveProfileName, cleanUrl, cleanModel, cleanKey);
                cleanProfileName = effectiveProfileName;
            }
            else if (!string.IsNullOrWhiteSpace(cleanProfileName))
            {
                cleanKey = ReadThirdPartyProfileToken(cleanProfileName);
                SaveToken(cleanKey);
            }
            else if (!HasStoredToken())
            {
                throw new InvalidOperationException("An API key is required for the first third-party switch.");
            }

            List<string> lines = ReadConfig();
            BackupConfig();
            InstallStableCredentialHelper();
            HistorySyncOutcome historySync = TrySynchronizeConversationProvider(ProviderId);
            SetTopLevelValue(lines, "model_provider", ProviderId);
            SetTopLevelValue(lines, "model", cleanModel);
            RemoveProviderSections(lines);
            AddProviderSections(lines, cleanUrl);
            string compatibilitySummary = compatibilityMode
                ? ApplyThirdPartyCompatibilityMode(lines)
                : RestoreThirdPartyCompatibilityMode(lines);
            WriteConfigAtomically(lines);
            if (!compatibilityMode && compatibilitySummary.Length > 0)
            {
                DeleteCompatibilityBackup();
            }

            StoredSettings settings = LoadSettings();
            settings.BaseUrl = cleanUrl;
            settings.ThirdPartyModel = cleanModel;
            settings.ThirdPartyCompatibilityMode = false;
            if (string.IsNullOrWhiteSpace(settings.OfficialModel))
            {
                settings.OfficialModel = "gpt-5.5";
            }
            SaveSettings(settings);
            bool environmentOverrideRestored = RestoreCodexApiKeyEnvironmentOverride();
            return new SwitchOutcome(
                historySync,
                false,
                environmentOverrideRestored,
                compatibilityMode,
                compatibilitySummary);
        }

        internal string RepairConversationIndex()
        {
            AssertConfig();
            AssertCodexIsStopped();
            List<string> statePaths = GetStateDatabasePaths();
            if (statePaths.Count == 0)
            {
                throw new FileNotFoundException(
                    "state_5.sqlite was not found in the Codex root or sqlite subdirectory.");
            }

            int restored = 0;
            List<string> backupPaths = new List<string>();
            foreach (string statePath in statePaths)
            {
                backupPaths.Add(CreateStateBackup(statePath, "pre-sidebar-repair"));
                using (NativeSqlite database = NativeSqlite.Open(statePath))
                {
                    EnsureThreadColumns(database);
                    int before = database.ScalarInt(
                        "select count(*) from threads where has_user_event = 1");
                    database.Execute(
                        "update threads set has_user_event = 1 " +
                        "where has_user_event = 0 and first_user_message <> '' " +
                        "and source in ('vscode','cli')");
                    EnsureIntegrity(database);
                    int after = database.ScalarInt(
                        "select count(*) from threads where has_user_event = 1");
                    restored += after - before;
                }
            }

            return string.Format(
                "已检查 {0} 套状态数据库，恢复 {1} 条会话的侧栏可见标记。备份：{2}",
                statePaths.Count,
                restored,
                string.Join("；", backupPaths.ToArray()));
        }

        internal SwitchOutcome SwitchToOfficial(string model)
        {
            AssertConfig();
            AssertCodexIsStopped();
            string cleanModel = (model ?? string.Empty).Trim();
            if (cleanModel.Length == 0)
            {
                throw new InvalidOperationException("Official model is required.");
            }

            List<string> lines = ReadConfig();
            BackupConfig();
            CodexApiKeyEnvironmentChange environmentChange =
                CaptureAndClearCodexApiKeyEnvironmentOverride();
            try
            {
                HistorySyncOutcome historySync = TrySynchronizeConversationProvider("openai");
                SetTopLevelValue(lines, "model_provider", "openai");
                SetTopLevelValue(lines, "model", cleanModel);
                RemoveProviderSections(lines);
                string compatibilitySummary = RestoreThirdPartyCompatibilityMode(lines);
                WriteConfigAtomically(lines);
                if (compatibilitySummary.Length > 0)
                {
                    DeleteCompatibilityBackup();
                }

                StoredSettings settings = LoadSettings();
                settings.OfficialModel = cleanModel;
                SaveSettings(settings);
                environmentChange.Commit();
                return new SwitchOutcome(
                    historySync,
                    environmentChange.WasCleared,
                    false,
                    false,
                    compatibilitySummary);
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
            string cleanModel = (model ?? string.Empty).Trim();
            if (cleanModel.Length == 0)
            {
                cleanModel = "gpt-5.5";
            }

            List<string> lines = ReadConfig();
            BackupConfig();
            SetTopLevelValue(lines, "model_provider", "openai");
            SetTopLevelValue(lines, "model", cleanModel);
            RemoveProviderSections(lines);
            string compatibilitySummary = RestoreThirdPartyCompatibilityMode(lines);
            WriteConfigAtomically(lines);
            if (compatibilitySummary.Length > 0)
            {
                DeleteCompatibilityBackup();
            }

            StoredSettings settings = LoadSettings();
            settings.OfficialModel = cleanModel;
            SaveSettings(settings);
        }

        internal void Rollback()
        {
            AssertConfig();
            if (!Directory.Exists(backupDirectory))
            {
                throw new InvalidOperationException("No backup directory exists.");
            }

            FileInfo latest = new DirectoryInfo(backupDirectory)
                .GetFiles("config.toml.*.bak")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest == null)
            {
                throw new InvalidOperationException("No configuration backup was found.");
            }

            BackupConfig();
            File.Copy(latest.FullName, configPath, true);
        }

        private void SaveToken(string token)
        {
            byte[] plain = Encoding.UTF8.GetBytes(token);
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                WriteBytesAtomically(credentialPath, encrypted);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(encrypted, 0, encrypted.Length);
            }
        }

        internal List<ThirdPartyProfile> LoadThirdPartyProfiles()
        {
            List<ThirdPartyProfile> profiles = new List<ThirdPartyProfile>();
            if (!File.Exists(profilesPath))
            {
                return profiles;
            }

            string[] lines = File.ReadAllLines(profilesPath, Encoding.UTF8);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts.Length < 4 || parts[0] != "profile")
                {
                    continue;
                }

                try
                {
                    string name = DecodeSetting(parts[1]);
                    string baseUrl = DecodeSetting(parts[2]);
                    string model = DecodeSetting(parts[3]);
                    string fileName = parts.Length >= 5
                        ? DecodeSetting(parts[4])
                        : GetProfileCredentialFileName(name);
                    if (string.IsNullOrWhiteSpace(name)
                        || string.IsNullOrWhiteSpace(baseUrl)
                        || string.IsNullOrWhiteSpace(model)
                        || string.IsNullOrWhiteSpace(fileName))
                    {
                        continue;
                    }

                    profiles.Add(new ThirdPartyProfile(
                        name,
                        baseUrl,
                        model,
                        fileName));
                }
                catch
                {
                }
            }

            return profiles
                .GroupBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        internal ThirdPartyProfile SaveThirdPartyProfile(
            string name,
            string url,
            string model,
            string key)
        {
            string cleanName = NormalizeProfileName(name);
            string cleanUrl = NormalizeBaseUrl(url);
            string cleanModel = (model ?? string.Empty).Trim();
            string cleanKey = (key ?? string.Empty).Trim();
            if (cleanModel.Length == 0)
            {
                throw new InvalidOperationException("Profile model is required.");
            }
            if (cleanKey.Length == 0)
            {
                throw new InvalidOperationException("Profile API key is required.");
            }

            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(profileCredentialDirectory);
            List<ThirdPartyProfile> profiles = LoadThirdPartyProfiles();
            ThirdPartyProfile existing = profiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
            string fileName = existing == null
                ? GetProfileCredentialFileName(cleanName)
                : existing.CredentialFileName;
            ThirdPartyProfile updated = new ThirdPartyProfile(
                cleanName,
                cleanUrl,
                cleanModel,
                fileName);

            profiles.RemoveAll(profile =>
                string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
            profiles.Add(updated);
            SaveProfileToken(updated, cleanKey);
            SaveThirdPartyProfiles(profiles);
            return updated;
        }

        internal void DeleteThirdPartyProfile(string name)
        {
            string cleanName = NormalizeProfileName(name);
            List<ThirdPartyProfile> profiles = LoadThirdPartyProfiles();
            ThirdPartyProfile existing = profiles.FirstOrDefault(profile =>
                string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                return;
            }

            profiles.RemoveAll(profile =>
                string.Equals(profile.Name, cleanName, StringComparison.OrdinalIgnoreCase));
            SaveThirdPartyProfiles(profiles);
            string tokenPath = GetProfileCredentialPath(existing);
            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }
        }

        internal string ReadThirdPartyProfileToken(string name)
        {
            string cleanName = NormalizeProfileName(name);
            ThirdPartyProfile profile = LoadThirdPartyProfiles().FirstOrDefault(candidate =>
                string.Equals(candidate.Name, cleanName, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                throw new InvalidOperationException("Third-party profile was not found: " + cleanName);
            }

            string tokenPath = GetProfileCredentialPath(profile);
            if (!File.Exists(tokenPath))
            {
                throw new InvalidOperationException(
                    "The selected profile has no encrypted API key. It may have been moved or deleted.");
            }

            return ReadProtectedText(tokenPath, ProfileEntropy);
        }

        private void SaveThirdPartyProfiles(List<ThirdPartyProfile> profiles)
        {
            Directory.CreateDirectory(dataDirectory);
            List<string> lines = new List<string>();
            foreach (ThirdPartyProfile profile in profiles
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                lines.Add(
                    "profile|" +
                    EncodeSetting(profile.Name) + "|" +
                    EncodeSetting(profile.BaseUrl) + "|" +
                    EncodeSetting(profile.Model) + "|" +
                    EncodeSetting(profile.CredentialFileName));
            }
            WriteTextAtomically(profilesPath, lines);
        }

        private void SaveProfileToken(ThirdPartyProfile profile, string token)
        {
            SaveProtectedText(GetProfileCredentialPath(profile), token, ProfileEntropy);
        }

        private string GetProfileCredentialPath(ThirdPartyProfile profile)
        {
            return Path.Combine(profileCredentialDirectory, profile.CredentialFileName);
        }

        private static string NormalizeProfileName(string name)
        {
            string clean = (name ?? string.Empty).Trim();
            if (clean.Length == 0)
            {
                clean = "未命名中转站";
            }
            if (clean.Length > 80)
            {
                clean = clean.Substring(0, 80).Trim();
            }
            return clean.Length == 0 ? "未命名中转站" : clean;
        }

        private static string GetDefaultProfileName(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                if (!string.IsNullOrWhiteSpace(uri.Host))
                {
                    return uri.Host;
                }
            }
            catch
            {
            }
            return "中转站";
        }

        private static string GetProfileCredentialFileName(string name)
        {
            return GetPathToken("profile:" + NormalizeProfileName(name)) + ".dat";
        }

        private void InstallStableCredentialHelper()
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new FileNotFoundException("Current switcher executable was not found.", executablePath);
            }

            Directory.CreateDirectory(dataDirectory);
            if (string.Equals(
                Path.GetFullPath(executablePath),
                Path.GetFullPath(stableHelperPath),
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string temporary = stableHelperPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(executablePath, temporary, true);
                if (File.Exists(stableHelperPath))
                {
                    File.Copy(temporary, stableHelperPath, true);
                    File.Delete(temporary);
                }
                else
                {
                    File.Move(temporary, stableHelperPath);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private bool IsRealCodexRoot()
        {
            string normalizedRoot = Path.GetFullPath(RemoveExtendedPathPrefix(root)).TrimEnd('\\');
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                string normalizedConfigured = Path.GetFullPath(
                    RemoveExtendedPathPrefix(configured)).TrimEnd('\\');
                if (string.Equals(
                    normalizedConfigured,
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string defaultRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
            return string.Equals(
                Path.GetFullPath(defaultRoot).TrimEnd('\\'),
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase);
        }

        private void AssertCodexIsStopped()
        {
            if (IsRealCodexRoot() && IsCodexRunning())
            {
                throw new InvalidOperationException(
                    "Codex 仍在运行。为避免历史数据库或会话 JSONL 冲突，请彻底退出 Codex 后再切换。");
            }
        }

        private static bool IsCodexRunning()
        {
            List<Process> processes = GetCodexProcesses();
            bool running = processes.Count > 0;
            DisposeProcesses(processes);
            return running;
        }

        private static LaunchTarget ResolveCodexLaunchTarget()
        {
            LaunchTarget target = null;
            string appId = FindCodexAppUserModelId();
            if (appId.Length > 0)
            {
                target = LaunchTarget.ForShellApp(appId);
            }
            else
            {
                string executable = FindCodexDesktopExecutable();
                if (executable.Length > 0)
                {
                    target = LaunchTarget.ForExecutable(executable);
                }
                else
                {
                    string command = FindCodexCommandOnPath();
                    if (command.Length > 0)
                    {
                        target = LaunchTarget.ForExecutable(command);
                    }
                }

            }

            if (target != null)
            {
                return target;
            }
            throw new FileNotFoundException("未找到 Codex 桌面应用或 codex 命令。");
        }

        private static string FindCodexAppUserModelId()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_APP_ID");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            string windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps");
            try
            {
                if (Directory.Exists(windowsApps))
                {
                    foreach (string directory in Directory.GetDirectories(windowsApps, "OpenAI.Codex_*__*")
                        .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                    {
                        Match match = Regex.Match(
                            Path.GetFileName(directory),
                            @"^OpenAI\.Codex_.+__(?<publisher>[^_]+)$",
                            RegexOptions.CultureInvariant);
                        if (match.Success)
                        {
                            return "OpenAI.Codex_" + match.Groups["publisher"].Value + "!App";
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string FindCodexDesktopExecutable()
        {
            string windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps");
            try
            {
                if (!Directory.Exists(windowsApps))
                {
                    return string.Empty;
                }

                foreach (string directory in Directory.GetDirectories(windowsApps, "OpenAI.Codex_*__*")
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string candidate = Path.Combine(directory, "app", "Codex.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string FindCodexCommandOnPath()
        {
            string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] commandNames = { "codex.exe", "codex.cmd", "codex.bat", "codex.ps1" };
            foreach (string directory in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                foreach (string commandName in commandNames)
                {
                    string candidate;
                    try
                    {
                        candidate = Path.Combine(directory.Trim(), commandName);
                    }
                    catch
                    {
                        continue;
                    }

                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return string.Empty;
        }

        private static List<Process> GetCodexProcesses()
        {
            int currentProcessId = Process.GetCurrentProcess().Id;
            Dictionary<int, Process> results = new Dictionary<int, Process>();
            foreach (string name in new[] { "Codex", "codex" })
            {
                Process[] matches;
                try
                {
                    matches = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                foreach (Process process in matches)
                {
                    if (process.Id == currentProcessId || results.ContainsKey(process.Id))
                    {
                        process.Dispose();
                        continue;
                    }

                    results[process.Id] = process;
                }
            }

            return results.Values.ToList();
        }

        private static bool CloseCodexProcess(Process process)
        {
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow())
                {
                    if (process.WaitForExit(4000))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(4000);
            }

            return true;
        }

        private static void DisposeProcesses(IEnumerable<Process> processes)
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        private static string SafeProcessName(Process process)
        {
            try
            {
                return process.ProcessName + " #" + process.Id;
            }
            catch
            {
                return "Codex process";
            }
        }

        private static string NormalizeBaseUrl(string value)
        {
            string clean = (value ?? string.Empty).Trim().TrimEnd('/');
            Uri uri;
            if (!Uri.TryCreate(clean, UriKind.Absolute, out uri))
            {
                throw new InvalidOperationException("Base URL is not a valid absolute URL.");
            }

            string path = uri.AbsolutePath.TrimEnd('/');
            string lower = path.ToLowerInvariant();
            string[] endpointSuffixes =
            {
                "/v1/responses/compact",
                "/v1/chat/completions",
                "/v1/responses"
            };
            foreach (string suffix in endpointSuffixes)
            {
                if (lower.EndsWith(suffix, StringComparison.Ordinal))
                {
                    path = path.Substring(0, path.Length - suffix.Length) + "/v1";
                    lower = path.ToLowerInvariant();
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(path) || path == "/")
            {
                path = "/v1";
            }

            UriBuilder builder = new UriBuilder(uri);
            builder.Path = path;
            builder.Query = string.Empty;
            builder.Fragment = string.Empty;
            return builder.Uri.AbsoluteUri.TrimEnd('/');
        }

        private HistorySyncOutcome TrySynchronizeConversationProvider(string targetProvider)
        {
            int skippedMissing = 0;
            try
            {
                string summary = SynchronizeConversationProvider(
                    targetProvider,
                    out skippedMissing);
                HistorySyncOutcome outcome = HistorySyncOutcome.Success(
                    skippedMissing,
                    summary);
                if (outcome.HasNotice)
                {
                    AppendHistorySyncWarning(outcome.ToLogMessage());
                }
                return outcome;
            }
            catch (Exception ex)
            {
                HistorySyncOutcome outcome = HistorySyncOutcome.Warning(
                    skippedMissing,
                    ex.Message);
                AppendHistorySyncWarning(outcome.ToLogMessage());
                return outcome;
            }
        }

        private void AppendHistorySyncWarning(string message)
        {
            try
            {
                Directory.CreateDirectory(dataDirectory);
                string logPath = Path.Combine(dataDirectory, "history-sync-warnings.log");
                string entry = string.Format(
                    "[{0}] {1}{2}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    message,
                    Environment.NewLine);
                File.AppendAllText(logPath, entry, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private string SynchronizeConversationProvider(
            string targetProvider,
            out int skippedMissing)
        {
            skippedMissing = 0;
            List<string> statePaths = GetStateDatabasePaths();
            if (statePaths.Count == 0)
            {
                return "No state database exists yet; history synchronization was skipped.";
            }

            if (targetProvider != "openai" && targetProvider != ProviderId)
            {
                throw new InvalidOperationException("Unsupported provider: " + targetProvider);
            }

            string provider = targetProvider.Replace("'", "''");
            string where = "first_user_message <> '' and source in ('vscode','cli')";
            List<string> rolloutPaths = new List<string>();
            foreach (string statePath in statePaths)
            {
                using (NativeSqlite database = NativeSqlite.Open(statePath))
                {
                    EnsureThreadColumns(database);
                    rolloutPaths.AddRange(database.QueryTextColumn(
                        "select rollout_path from threads where " + where + " order by rollout_path"));
                }
            }

            SessionMetadataSyncResult metadataResult =
                SynchronizeSessionMetadata(
                    rolloutPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    targetProvider);
            skippedMissing = metadataResult.SkippedMissingCount;
            List<string> backupPaths = new List<string>();
            int total = 0;
            int providerChanges = 0;
            int visibilityChanges = 0;
            try
            {
                foreach (string statePath in statePaths)
                {
                    backupPaths.Add(CreateStateBackup(statePath, "pre-provider-sync"));
                    using (NativeSqlite database = NativeSqlite.Open(statePath))
                    {
                        EnsureThreadColumns(database);
                        database.Execute("begin immediate");
                        try
                        {
                            total += database.ScalarInt(
                                "select count(*) from threads where " + where);
                            providerChanges += database.ScalarInt(
                                "select count(*) from threads where " + where +
                                " and model_provider <> '" + provider + "'");
                            visibilityChanges += database.ScalarInt(
                                "select count(*) from threads where " + where +
                                " and has_user_event = 0");
                            database.Execute(
                                "update threads set model_provider = '" + provider +
                                "', has_user_event = 1 where " + where);
                            EnsureIntegrity(database);
                            int remaining = database.ScalarInt(
                                "select count(*) from threads where " + where +
                                " and model_provider <> '" + provider + "'");
                            if (remaining != 0)
                            {
                                throw new InvalidOperationException(
                                    "Provider synchronization incomplete in " + statePath +
                                    ": " + remaining + " rows remain.");
                            }
                            database.Execute("commit");
                        }
                        catch
                        {
                            try { database.Execute("rollback"); } catch { }
                            throw;
                        }
                    }
                }

                return string.Format(
                    "已同步 {0} 套状态数据库中的 {1} 条用户会话到 {2}（数据库 provider 变更 {3} 条，JSONL 元数据变更 {4} 条，可见标记恢复 {5} 条，缺失 JSONL 跳过 {6} 条）。数据库备份：{7}；会话备份：{8}",
                    statePaths.Count,
                    total,
                    targetProvider,
                    providerChanges,
                    metadataResult.ChangedCount,
                    visibilityChanges,
                    metadataResult.SkippedMissingCount,
                    string.Join("；", backupPaths.ToArray()),
                    metadataResult.BackupDirectory);
            }
            catch
            {
                metadataResult.Rollback();
                throw;
            }
        }

        private List<string> GetStateDatabasePaths()
        {
            string[] candidates =
            {
                Path.Combine(root, "sqlite", "state_5.sqlite"),
                Path.Combine(root, "state_5.sqlite")
            };
            return candidates
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private SessionMetadataSyncResult SynchronizeSessionMetadata(
            List<string> rolloutPaths,
            string targetProvider)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string backupRoot = Path.Combine(
                root,
                "history_sync_backups",
                "session-meta-provider-sync-" + stamp);
            List<SessionMetadataChange> changes = new List<SessionMetadataChange>();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            int skippedMissing;

            foreach (string fullPath in ResolveConversationJsonlPaths(
                rolloutPaths,
                out skippedMissing))
            {
                byte[] originalBytes;
                try
                {
                    originalBytes = File.ReadAllBytes(fullPath);
                }
                catch (FileNotFoundException)
                {
                    skippedMissing++;
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    skippedMissing++;
                    continue;
                }
                int lineEnd = Array.IndexOf(originalBytes, (byte)'\n');
                int firstLineLength = lineEnd >= 0 ? lineEnd : originalBytes.Length;
                if (firstLineLength > 0 && originalBytes[firstLineLength - 1] == (byte)'\r')
                {
                    firstLineLength--;
                }

                string firstLine = Encoding.UTF8.GetString(originalBytes, 0, firstLineLength);
                Dictionary<string, object> envelope =
                    serializer.Deserialize<Dictionary<string, object>>(firstLine);
                object payloadObject;
                if (!envelope.TryGetValue("payload", out payloadObject))
                {
                    throw new InvalidOperationException(
                        "Conversation JSONL has no payload object: " + fullPath);
                }
                Dictionary<string, object> payload = payloadObject as Dictionary<string, object>;
                if (payload == null)
                {
                    throw new InvalidOperationException(
                        "Conversation JSONL payload is invalid: " + fullPath);
                }

                object existingProvider;
                string existing = payload.TryGetValue("model_provider", out existingProvider)
                    ? Convert.ToString(existingProvider)
                    : string.Empty;
                if (string.Equals(existing, targetProvider, StringComparison.Ordinal))
                {
                    continue;
                }

                payload["model_provider"] = targetProvider;
                string replacementLine = serializer.Serialize(envelope);
                byte[] replacementBytes = Encoding.UTF8.GetBytes(replacementLine);
                int remainderOffset = lineEnd >= 0 ? lineEnd : originalBytes.Length;
                int remainderLength = originalBytes.Length - remainderOffset;
                byte[] updatedBytes = new byte[replacementBytes.Length + remainderLength];
                Buffer.BlockCopy(replacementBytes, 0, updatedBytes, 0, replacementBytes.Length);
                if (remainderLength > 0)
                {
                    Buffer.BlockCopy(
                        originalBytes,
                        remainderOffset,
                        updatedBytes,
                        replacementBytes.Length,
                        remainderLength);
                }

                string backupName = GetPathToken(fullPath) + "-" + Path.GetFileName(fullPath);
                string backupPath = Path.Combine(backupRoot, backupName);
                changes.Add(new SessionMetadataChange(
                    fullPath,
                    backupPath,
                    originalBytes,
                    updatedBytes));
            }

            if (changes.Count == 0)
            {
                return new SessionMetadataSyncResult(
                    0,
                    "无需创建（JSONL 已一致）",
                    new List<SessionMetadataChange>(),
                    skippedMissing);
            }

            List<SessionMetadataChange> applied = new List<SessionMetadataChange>();
            try
            {
                foreach (SessionMetadataChange change in changes)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(change.BackupPath));
                    File.Copy(change.Path, change.BackupPath, false);
                    WriteBytesAtomically(change.Path, change.UpdatedBytes);
                    applied.Add(change);
                }
            }
            catch
            {
                foreach (SessionMetadataChange change in applied)
                {
                    WriteBytesAtomically(change.Path, change.OriginalBytes);
                }
                throw;
            }

            return new SessionMetadataSyncResult(
                changes.Count,
                backupRoot,
                changes,
                skippedMissing);
        }

        private List<string> ResolveConversationJsonlPaths(
            List<string> rolloutPaths,
            out int skippedMissing)
        {
            skippedMissing = 0;
            List<string> fullPaths = new List<string>();
            foreach (string rolloutPath in rolloutPaths)
            {
                if (string.IsNullOrWhiteSpace(rolloutPath))
                {
                    skippedMissing++;
                    continue;
                }

                string fullPath;
                try
                {
                    fullPath = ResolveConversationJsonlPath(rolloutPath);
                }
                catch (FileNotFoundException)
                {
                    skippedMissing++;
                    continue;
                }
                if (!fullPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    fullPaths.Add(fullPath);
                }
            }
            return fullPaths;
        }

        private string ResolveConversationJsonlPath(string rolloutPath)
        {
            string fullPath = Path.GetFullPath(RemoveExtendedPathPrefix(rolloutPath));
            string rootPrefix = Path.GetFullPath(RemoveExtendedPathPrefix(root)).TrimEnd('\\') + "\\";
            bool isInsideCurrentRoot = fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase);
            if (isInsideCurrentRoot && File.Exists(fullPath))
            {
                return fullPath;
            }

            string relocatedPath = FindRelocatedConversationJsonl(fullPath, rootPrefix);
            if (relocatedPath.Length > 0)
            {
                return relocatedPath;
            }

            throw new FileNotFoundException(
                "Conversation JSONL was not found in the current Codex root: " + fullPath,
                fullPath);
        }

        private string FindRelocatedConversationJsonl(string missingFullPath, string rootPrefix)
        {
            string fileName = Path.GetFileName(missingFullPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            List<string> candidates = new List<string>();
            AddConversationCandidate(candidates, Path.Combine(root, "archived_sessions", fileName));
            AddConversationCandidates(candidates, Path.Combine(root, "archived_sessions"), fileName);
            AddConversationCandidates(candidates, Path.Combine(root, "sessions"), fileName);

            foreach (string candidate in candidates)
            {
                string fullCandidate = Path.GetFullPath(RemoveExtendedPathPrefix(candidate));
                if (fullCandidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(fullCandidate))
                {
                    return fullCandidate;
                }
            }

            return string.Empty;
        }

        private static void AddConversationCandidate(List<string> candidates, string path)
        {
            if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(path);
            }
        }

        private static void AddConversationCandidates(
            List<string> candidates,
            string directory,
            string fileName)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (string path in Directory.GetFiles(directory, fileName, SearchOption.AllDirectories))
            {
                AddConversationCandidate(candidates, path);
            }
        }

        private static string GetPathToken(string path)
        {
            byte[] input = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(input);
                StringBuilder token = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                {
                    token.Append(digest[i].ToString("x2"));
                }
                return token.ToString();
            }
        }

        private static string RemoveExtendedPathPrefix(string path)
        {
            if (path == null)
            {
                return string.Empty;
            }
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path.Substring(4);
            }
            return path;
        }

        private string CreateStateBackup(string statePath, string purpose)
        {
            string directory = Path.Combine(root, "history_sync_backups");
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string location = string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(statePath)).TrimEnd('\\'),
                Path.GetFullPath(root).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase)
                ? "root"
                : "sqlite";
            string backupPath = Path.Combine(
                directory,
                "state_5.sqlite." + location + "." + purpose + "." + stamp + ".bak");
            NativeSqlite.Backup(statePath, backupPath);
            return backupPath;
        }

        private static void EnsureThreadColumns(NativeSqlite database)
        {
            string[] required = { "source", "first_user_message", "has_user_event", "model_provider" };
            foreach (string column in required)
            {
                int exists = database.ScalarInt(
                    "select count(*) from pragma_table_info('threads') where name = '" + column + "'");
                if (exists == 0)
                {
                    throw new InvalidOperationException(
                        "threads table is missing required column: " + column);
                }
            }
        }

        private static void EnsureIntegrity(NativeSqlite database)
        {
            string result = database.ScalarText("pragma integrity_check");
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SQLite integrity_check failed: " + result);
            }
        }

        private void SaveSettings(StoredSettings settings)
        {
            Directory.CreateDirectory(dataDirectory);
            List<string> lines = new List<string>();
            lines.Add("url=" + EncodeSetting(settings.BaseUrl));
            lines.Add("thirdModel=" + EncodeSetting(settings.ThirdPartyModel));
            lines.Add("officialModel=" + EncodeSetting(settings.OfficialModel));
            lines.Add("openHotkey=" + EncodeSetting(settings.GetOpenHotkey()));
            lines.Add("openMouseButton=" + EncodeSetting(settings.GetOpenMouseButton()));
            lines.Add("thirdPartyCompatibilityMode=" + EncodeSetting(
                settings.GetThirdPartyCompatibilityMode() ? "1" : "0"));
            WriteTextAtomically(settingsPath, lines);
        }

        private static string EncodeSetting(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string DecodeSetting(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
        }

        private List<string> ReadConfig()
        {
            AssertConfig();
            return new List<string>(File.ReadAllLines(configPath, Encoding.UTF8));
        }

        private void AssertConfig()
        {
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("Codex root does not exist: " + root);
            }
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("config.toml was not found in the selected Codex root.", configPath);
            }
        }

        private string BackupConfig()
        {
            Directory.CreateDirectory(backupDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            string destination = Path.Combine(backupDirectory, "config.toml." + stamp + ".bak");
            File.Copy(configPath, destination, false);
            return destination;
        }

        private void WriteConfigAtomically(List<string> lines)
        {
            string temporary = Path.Combine(root, "config.toml.switcher." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                File.Copy(temporary, configPath, true);
                File.Delete(temporary);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static void WriteTextAtomically(string path, List<string> lines)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Copy(temporary, path, true);
                    File.Delete(temporary);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static void WriteBytesAtomically(string path, byte[] bytes)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path))
                {
                    File.Copy(temporary, path, true);
                    File.Delete(temporary);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static string GetTopLevelValue(List<string> lines, string key)
        {
            Regex expression = new Regex(
                @"^\s*" + Regex.Escape(key) + @"\s*=\s*""([^""]*)""\s*$",
                RegexOptions.CultureInvariant);
            foreach (string line in lines)
            {
                if (Regex.IsMatch(line, @"^\s*\["))
                {
                    break;
                }
                Match match = expression.Match(line);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }
            return string.Empty;
        }

        private static string GetSectionValue(List<string> lines, string section, string key)
        {
            bool inside = false;
            Regex keyExpression = new Regex(
                @"^\s*" + Regex.Escape(key) + @"\s*=\s*(.+?)\s*$",
                RegexOptions.CultureInvariant);
            foreach (string line in lines)
            {
                Match sectionMatch = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
                if (sectionMatch.Success)
                {
                    inside = string.Equals(sectionMatch.Groups[1].Value, section, StringComparison.Ordinal);
                    continue;
                }
                if (inside)
                {
                    Match keyMatch = keyExpression.Match(line);
                    if (keyMatch.Success)
                    {
                        return keyMatch.Groups[1].Value.Trim().Trim('"');
                    }
                }
            }
            return string.Empty;
        }

        private static bool SectionExists(List<string> lines, string section)
        {
            return lines.Any(line =>
            {
                Match match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
                return match.Success && string.Equals(match.Groups[1].Value, section, StringComparison.Ordinal);
            });
        }

        private static void SetTopLevelValue(List<string> lines, string key, string value)
        {
            int firstSection = lines.FindIndex(line => Regex.IsMatch(line, @"^\s*\["));
            if (firstSection < 0)
            {
                firstSection = lines.Count;
            }

            Regex expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            string replacement = key + " = \"" + EscapeToml(value) + "\"";
            bool found = false;
            for (int i = 0; i < firstSection; i++)
            {
                if (!expression.IsMatch(lines[i]))
                {
                    continue;
                }
                if (!found)
                {
                    lines[i] = replacement;
                    found = true;
                }
                else
                {
                    lines.RemoveAt(i);
                    i--;
                    firstSection--;
                }
            }

            if (!found)
            {
                lines.Insert(firstSection, replacement);
            }
        }

        private static bool TryGetTopLevelLine(List<string> lines, string key, out int index, out string line)
        {
            int firstSection = lines.FindIndex(candidate => Regex.IsMatch(candidate, @"^\s*\["));
            if (firstSection < 0)
            {
                firstSection = lines.Count;
            }

            Regex expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            for (int i = 0; i < firstSection; i++)
            {
                if (expression.IsMatch(lines[i]))
                {
                    index = i;
                    line = lines[i];
                    return true;
                }
            }

            index = -1;
            line = string.Empty;
            return false;
        }

        private static void RemoveTopLevelValue(List<string> lines, string key)
        {
            int index;
            string line;
            while (TryGetTopLevelLine(lines, key, out index, out line))
            {
                lines.RemoveAt(index);
            }
        }

        private static void SetTopLevelBoolean(List<string> lines, string key, bool value)
        {
            SetTopLevelLiteral(lines, key, value ? "true" : "false");
        }

        private static void SetTopLevelLiteral(List<string> lines, string key, string literal)
        {
            int firstSection = lines.FindIndex(line => Regex.IsMatch(line, @"^\s*\["));
            if (firstSection < 0)
            {
                firstSection = lines.Count;
            }

            Regex expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            string replacement = key + " = " + literal;
            bool found = false;
            for (int i = 0; i < firstSection; i++)
            {
                if (!expression.IsMatch(lines[i]))
                {
                    continue;
                }
                if (!found)
                {
                    lines[i] = replacement;
                    found = true;
                }
                else
                {
                    lines.RemoveAt(i);
                    i--;
                    firstSection--;
                }
            }

            if (!found)
            {
                lines.Insert(firstSection, replacement);
            }
        }

        private static bool TryGetSectionKeyLine(
            List<string> lines,
            string section,
            string key,
            out int index,
            out string line)
        {
            bool inside = false;
            Regex keyExpression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            for (int i = 0; i < lines.Count; i++)
            {
                Match sectionMatch = Regex.Match(lines[i], @"^\s*\[([^\]]+)\]\s*$");
                if (sectionMatch.Success)
                {
                    inside = string.Equals(sectionMatch.Groups[1].Value, section, StringComparison.Ordinal);
                    continue;
                }

                if (inside && keyExpression.IsMatch(lines[i]))
                {
                    index = i;
                    line = lines[i];
                    return true;
                }
            }

            index = -1;
            line = string.Empty;
            return false;
        }

        private static void RemoveSectionKey(List<string> lines, string section, string key)
        {
            int index;
            string line;
            while (TryGetSectionKeyLine(lines, section, key, out index, out line))
            {
                lines.RemoveAt(index);
            }
        }

        private static void SetSectionBoolean(List<string> lines, string section, string key, bool value)
        {
            SetSectionLiteral(lines, section, key, value ? "true" : "false");
        }

        private static void SetSectionLiteral(List<string> lines, string section, string key, string literal)
        {
            EnsureSectionExists(lines, section);
            int sectionIndex = FindSectionIndex(lines, section);
            int nextSection = FindNextSectionIndex(lines, sectionIndex + 1);
            Regex keyExpression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            string replacement = key + " = " + literal;
            bool found = false;
            for (int i = sectionIndex + 1; i < nextSection; i++)
            {
                if (!keyExpression.IsMatch(lines[i]))
                {
                    continue;
                }
                if (!found)
                {
                    lines[i] = replacement;
                    found = true;
                }
                else
                {
                    lines.RemoveAt(i);
                    i--;
                    nextSection--;
                }
            }

            if (!found)
            {
                lines.Insert(nextSection, replacement);
            }
        }

        private static int FindSectionIndex(List<string> lines, string section)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Match match = Regex.Match(lines[i], @"^\s*\[([^\]]+)\]\s*$");
                if (match.Success && string.Equals(match.Groups[1].Value, section, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindNextSectionIndex(List<string> lines, int start)
        {
            for (int i = Math.Max(0, start); i < lines.Count; i++)
            {
                if (Regex.IsMatch(lines[i], @"^\s*\["))
                {
                    return i;
                }
            }
            return lines.Count;
        }

        private static void EnsureSectionExists(List<string> lines, string section)
        {
            if (FindSectionIndex(lines, section) >= 0)
            {
                return;
            }

            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
            {
                lines.Add(string.Empty);
            }
            lines.Add("[" + section + "]");
        }

        private static void RestoreTopLevelLine(List<string> lines, string key, string originalLine)
        {
            int index;
            string line;
            if (TryGetTopLevelLine(lines, key, out index, out line))
            {
                lines[index] = originalLine;
                while (TryGetTopLevelLineAfter(lines, key, index + 1, out index))
                {
                    lines.RemoveAt(index);
                }
                return;
            }

            int firstSection = lines.FindIndex(candidate => Regex.IsMatch(candidate, @"^\s*\["));
            if (firstSection < 0)
            {
                firstSection = lines.Count;
            }
            lines.Insert(firstSection, originalLine);
        }

        private static bool TryGetTopLevelLineAfter(List<string> lines, string key, int start, out int index)
        {
            int firstSection = lines.FindIndex(candidate => Regex.IsMatch(candidate, @"^\s*\["));
            if (firstSection < 0)
            {
                firstSection = lines.Count;
            }

            Regex expression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            for (int i = Math.Max(0, start); i < firstSection; i++)
            {
                if (expression.IsMatch(lines[i]))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private static void RestoreSectionLine(
            List<string> lines,
            string section,
            string key,
            string originalLine)
        {
            EnsureSectionExists(lines, section);
            int index;
            string line;
            if (TryGetSectionKeyLine(lines, section, key, out index, out line))
            {
                lines[index] = originalLine;
                while (TryGetSectionKeyLineAfter(lines, section, key, index + 1, out index))
                {
                    lines.RemoveAt(index);
                }
                return;
            }

            int sectionIndex = FindSectionIndex(lines, section);
            int nextSection = FindNextSectionIndex(lines, sectionIndex + 1);
            lines.Insert(nextSection, originalLine);
        }

        private static bool TryGetSectionKeyLineAfter(
            List<string> lines,
            string section,
            string key,
            int start,
            out int index)
        {
            bool inside = false;
            Regex keyExpression = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=");
            for (int i = 0; i < lines.Count; i++)
            {
                Match sectionMatch = Regex.Match(lines[i], @"^\s*\[([^\]]+)\]\s*$");
                if (sectionMatch.Success)
                {
                    inside = string.Equals(sectionMatch.Groups[1].Value, section, StringComparison.Ordinal);
                    continue;
                }

                if (i >= start && inside && keyExpression.IsMatch(lines[i]))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        private string ApplyThirdPartyCompatibilityMode(List<string> lines)
        {
            if (!File.Exists(compatibilityBackupPath))
            {
                SaveCompatibilityBackup(CaptureCompatibilityBackup(lines));
            }

            SetTopLevelLiteral(lines, "web_search", "\"disabled\"");
            SetTopLevelBoolean(lines, "tools_view_image", false);

            SetSectionBoolean(lines, "features", "image_generation", false);
            SetSectionBoolean(lines, "features", "imagegen", false);
            SetSectionBoolean(lines, "features", "imagegenext", false);
            SetSectionBoolean(lines, "features", "browser_use", false);
            SetSectionBoolean(lines, "features", "computer_use", false);
            SetSectionBoolean(lines, "features", "in_app_browser", false);

            SetSectionBoolean(lines, "tools", "view_image", false);

            foreach (string pluginSection in GetCompatibilityPluginSections())
            {
                if (SectionExists(lines, pluginSection))
                {
                    SetSectionBoolean(lines, pluginSection, "enabled", false);
                }
            }

            return "已启用第三方兼容模式：关闭 Codex 生图、图片查看、Web 搜索，并临时禁用浏览器/文档/表格/PPT/PDF 插件，减少中转站误报 Image generation 权限错误。";
        }

        private string RestoreThirdPartyCompatibilityMode(List<string> lines)
        {
            if (!File.Exists(compatibilityBackupPath))
            {
                return string.Empty;
            }

            List<CompatibilityBackupEntry> entries = LoadCompatibilityBackup();
            foreach (CompatibilityBackupEntry entry in entries)
            {
                if (entry.Scope == "top")
                {
                    if (entry.Existed)
                    {
                        RestoreTopLevelLine(lines, entry.Key, entry.Line);
                    }
                    else
                    {
                        RemoveTopLevelValue(lines, entry.Key);
                    }
                }
                else if (entry.Scope == "section")
                {
                    if (entry.Existed)
                    {
                        RestoreSectionLine(lines, entry.Section, entry.Key, entry.Line);
                    }
                    else
                    {
                        RemoveSectionKey(lines, entry.Section, entry.Key);
                    }
                }
            }

            return "已恢复第三方兼容模式之前的 Codex 工具和插件配置。";
        }

        private void DeleteCompatibilityBackup()
        {
            try
            {
                File.Delete(compatibilityBackupPath);
            }
            catch
            {
            }
        }

        private List<CompatibilityBackupEntry> CaptureCompatibilityBackup(List<string> lines)
        {
            List<CompatibilityBackupEntry> entries = new List<CompatibilityBackupEntry>();
            AddTopLevelCompatibilityBackup(entries, lines, "web_search");
            AddTopLevelCompatibilityBackup(entries, lines, "tools_view_image");

            AddSectionCompatibilityBackup(entries, lines, "features", "image_generation");
            AddSectionCompatibilityBackup(entries, lines, "features", "imagegen");
            AddSectionCompatibilityBackup(entries, lines, "features", "imagegenext");
            AddSectionCompatibilityBackup(entries, lines, "features", "browser_use");
            AddSectionCompatibilityBackup(entries, lines, "features", "computer_use");
            AddSectionCompatibilityBackup(entries, lines, "features", "in_app_browser");

            AddSectionCompatibilityBackup(entries, lines, "tools", "view_image");

            foreach (string pluginSection in GetCompatibilityPluginSections())
            {
                AddSectionCompatibilityBackup(entries, lines, pluginSection, "enabled");
            }

            return entries;
        }

        private static string[] GetCompatibilityPluginSections()
        {
            return new[]
            {
                "plugins.\"browser-use@openai-bundled\"",
                "plugins.\"documents@openai-primary-runtime\"",
                "plugins.\"spreadsheets@openai-primary-runtime\"",
                "plugins.\"presentations@openai-primary-runtime\"",
                "plugins.\"pdf@openai-primary-runtime\""
            };
        }

        private static void AddTopLevelCompatibilityBackup(
            List<CompatibilityBackupEntry> entries,
            List<string> lines,
            string key)
        {
            int index;
            string line;
            bool existed = TryGetTopLevelLine(lines, key, out index, out line);
            entries.Add(new CompatibilityBackupEntry("top", string.Empty, key, existed, line));
        }

        private static void AddSectionCompatibilityBackup(
            List<CompatibilityBackupEntry> entries,
            List<string> lines,
            string section,
            string key)
        {
            int index;
            string line;
            bool existed = TryGetSectionKeyLine(lines, section, key, out index, out line);
            entries.Add(new CompatibilityBackupEntry("section", section, key, existed, line));
        }

        private void SaveCompatibilityBackup(List<CompatibilityBackupEntry> entries)
        {
            List<string> lines = new List<string>();
            lines.Add("version=" + EncodeSetting("1"));
            foreach (CompatibilityBackupEntry entry in entries)
            {
                lines.Add(
                    "entry=" +
                    EncodeSetting(entry.Scope) + "|" +
                    EncodeSetting(entry.Section) + "|" +
                    EncodeSetting(entry.Key) + "|" +
                    EncodeSetting(entry.Existed ? "1" : "0") + "|" +
                    EncodeSetting(entry.Line));
            }
            WriteTextAtomically(compatibilityBackupPath, lines);
        }

        private List<CompatibilityBackupEntry> LoadCompatibilityBackup()
        {
            List<CompatibilityBackupEntry> entries = new List<CompatibilityBackupEntry>();
            foreach (string rawLine in File.ReadAllLines(compatibilityBackupPath, Encoding.UTF8))
            {
                int separator = rawLine.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }
                string name = rawLine.Substring(0, separator);
                if (name != "entry")
                {
                    continue;
                }

                string[] parts = rawLine.Substring(separator + 1).Split('|');
                if (parts.Length != 5)
                {
                    continue;
                }

                string scope = DecodeSetting(parts[0]);
                string section = DecodeSetting(parts[1]);
                string key = DecodeSetting(parts[2]);
                bool existed = DecodeSetting(parts[3]) == "1";
                string line = DecodeSetting(parts[4]);
                entries.Add(new CompatibilityBackupEntry(scope, section, key, existed, line));
            }
            return entries;
        }

        private static string EscapeToml(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void RemoveProviderSections(List<string> lines)
        {
            List<string> result = new List<string>();
            bool skip = false;
            foreach (string line in lines)
            {
                Match match = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
                if (match.Success)
                {
                    string section = match.Groups[1].Value;
                    skip = string.Equals(section, "model_providers." + ProviderId, StringComparison.Ordinal)
                        || section.StartsWith("model_providers." + ProviderId + ".", StringComparison.Ordinal);
                }
                if (!skip)
                {
                    result.Add(line);
                }
            }

            while (result.Count > 0 && string.IsNullOrWhiteSpace(result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }
            lines.Clear();
            lines.AddRange(result);
        }

        private void AddProviderSections(List<string> lines, string url)
        {
            string escapedExe = EscapeToml(stableHelperPath);
            string escapedRoot = EscapeToml(root);
            lines.Add(string.Empty);
            lines.Add("[model_providers." + ProviderId + "]");
            lines.Add("name = \"custom\"");
            lines.Add("wire_api = \"responses\"");
            lines.Add("base_url = \"" + EscapeToml(url) + "\"");
            lines.Add(string.Empty);
            lines.Add("[model_providers." + ProviderId + ".auth]");
            lines.Add("command = \"" + escapedExe + "\"");
            lines.Add("args = [\"--emit-token\", \"--root\", \"" + escapedRoot + "\"]");
            lines.Add("timeout_ms = 5000");
            lines.Add("refresh_interval_ms = 0");
        }
    }

    internal sealed class SessionMetadataChange
    {
        internal readonly string Path;
        internal readonly string BackupPath;
        internal readonly byte[] OriginalBytes;
        internal readonly byte[] UpdatedBytes;

        internal SessionMetadataChange(
            string path,
            string backupPath,
            byte[] originalBytes,
            byte[] updatedBytes)
        {
            Path = path;
            BackupPath = backupPath;
            OriginalBytes = originalBytes;
            UpdatedBytes = updatedBytes;
        }
    }

    internal sealed class HistorySyncOutcome
    {
        internal readonly int SkippedMissingCount;
        internal readonly string WarningMessage;
        internal readonly string Summary;

        private HistorySyncOutcome(
            int skippedMissingCount,
            string warningMessage,
            string summary)
        {
            SkippedMissingCount = skippedMissingCount;
            WarningMessage = warningMessage ?? string.Empty;
            Summary = summary ?? string.Empty;
        }

        internal bool HasNotice
        {
            get
            {
                return SkippedMissingCount > 0 || WarningMessage.Length > 0;
            }
        }

        internal static HistorySyncOutcome Success(int skippedMissingCount, string summary)
        {
            return new HistorySyncOutcome(skippedMissingCount, string.Empty, summary);
        }

        internal static HistorySyncOutcome Warning(
            int skippedMissingCount,
            string warningMessage)
        {
            return new HistorySyncOutcome(
                skippedMissingCount,
                warningMessage,
                string.Empty);
        }

        internal string ToUserNotice()
        {
            List<string> notices = new List<string>();
            if (SkippedMissingCount > 0)
            {
                notices.Add(string.Format(
                    "检测到 {0} 条会话文件路径已失效，可能是 Codex 更新后移动或清理了历史文件；已跳过这些记录，不影响本次 API 切换。",
                    SkippedMissingCount));
            }
            if (WarningMessage.Length > 0)
            {
                notices.Add(
                    "Codex 更新后可能改变了历史数据格式，本次历史同步未完全执行，但 API 切换已经完成。\n详情：" +
                    WarningMessage);
            }
            return notices.Count == 0
                ? string.Empty
                : "\n\n" + string.Join("\n\n", notices.ToArray());
        }

        internal string ToCommandLineNotice()
        {
            List<string> notices = new List<string>();
            if (SkippedMissingCount > 0)
            {
                notices.Add(string.Format(
                    "Skipped {0} missing or stale conversation JSONL file(s); Codex may have moved or cleaned them during an update.",
                    SkippedMissingCount));
            }
            if (WarningMessage.Length > 0)
            {
                notices.Add(
                    "History synchronization warning (the API switch still completed): " +
                    WarningMessage);
            }
            return string.Join(Environment.NewLine, notices.ToArray());
        }

        internal string ToLogMessage()
        {
            return ToCommandLineNotice();
        }
    }

    internal sealed class SwitchOutcome
    {
        internal readonly HistorySyncOutcome HistorySync;
        internal readonly bool CodexApiKeyOverrideCleared;
        internal readonly bool CodexApiKeyOverrideRestored;
        internal readonly bool ThirdPartyCompatibilityEnabled;
        internal readonly string CompatibilityNotice;

        internal SwitchOutcome(
            HistorySyncOutcome historySync,
            bool codexApiKeyOverrideCleared,
            bool codexApiKeyOverrideRestored)
            : this(
                historySync,
                codexApiKeyOverrideCleared,
                codexApiKeyOverrideRestored,
                false,
                string.Empty)
        {
        }

        internal SwitchOutcome(
            HistorySyncOutcome historySync,
            bool codexApiKeyOverrideCleared,
            bool codexApiKeyOverrideRestored,
            bool thirdPartyCompatibilityEnabled,
            string compatibilityNotice)
        {
            HistorySync = historySync;
            CodexApiKeyOverrideCleared = codexApiKeyOverrideCleared;
            CodexApiKeyOverrideRestored = codexApiKeyOverrideRestored;
            ThirdPartyCompatibilityEnabled = thirdPartyCompatibilityEnabled;
            CompatibilityNotice = compatibilityNotice ?? string.Empty;
        }

        internal bool HasNotice
        {
            get
            {
                return HistorySync.HasNotice
                    || CodexApiKeyOverrideCleared
                    || CodexApiKeyOverrideRestored
                    || CompatibilityNotice.Length > 0;
            }
        }

        internal string ToUserNotice()
        {
            List<string> notices = new List<string>();
            string historyNotice = HistorySync.ToUserNotice();
            if (!string.IsNullOrWhiteSpace(historyNotice))
            {
                notices.Add(historyNotice.Trim());
            }
            if (CodexApiKeyOverrideCleared)
            {
                notices.Add(
                    "检测到 CODEX_API_KEY 会覆盖 ChatGPT 官方登录。已加密暂存并清除该用户环境变量；切回第三方模式时会自动恢复。");
            }
            if (CodexApiKeyOverrideRestored)
            {
                notices.Add("已恢复先前暂存的 CODEX_API_KEY 用户环境变量。");
            }
            if (CompatibilityNotice.Length > 0)
            {
                notices.Add(CompatibilityNotice);
            }
            return notices.Count == 0
                ? string.Empty
                : "\n\n" + string.Join("\n\n", notices.ToArray());
        }

        internal string ToCommandLineNotice()
        {
            List<string> notices = new List<string>();
            string historyNotice = HistorySync.ToCommandLineNotice();
            if (!string.IsNullOrWhiteSpace(historyNotice))
            {
                notices.Add(historyNotice);
            }
            if (CodexApiKeyOverrideCleared)
            {
                notices.Add(
                    "Encrypted and cleared the user CODEX_API_KEY override so ChatGPT login is used; it will be restored in third-party mode.");
            }
            if (CodexApiKeyOverrideRestored)
            {
                notices.Add("Restored the previously saved user CODEX_API_KEY override.");
            }
            if (CompatibilityNotice.Length > 0)
            {
                notices.Add(CompatibilityNotice);
            }
            return string.Join(Environment.NewLine, notices.ToArray());
        }
    }

    internal sealed class CompatibilityBackupEntry
    {
        internal readonly string Scope;
        internal readonly string Section;
        internal readonly string Key;
        internal readonly bool Existed;
        internal readonly string Line;

        internal CompatibilityBackupEntry(
            string scope,
            string section,
            string key,
            bool existed,
            string line)
        {
            Scope = scope ?? string.Empty;
            Section = section ?? string.Empty;
            Key = key ?? string.Empty;
            Existed = existed;
            Line = line ?? string.Empty;
        }
    }

    internal sealed class CodexApiKeyEnvironmentChange
    {
        internal static readonly CodexApiKeyEnvironmentChange None =
            new CodexApiKeyEnvironmentChange(false, null);

        private readonly Action rollback;
        private bool committed;
        internal readonly bool WasCleared;

        internal CodexApiKeyEnvironmentChange(bool wasCleared, Action rollbackAction)
        {
            WasCleared = wasCleared;
            rollback = rollbackAction;
        }

        internal void Commit()
        {
            committed = true;
        }

        internal void Rollback()
        {
            if (!committed && rollback != null)
            {
                rollback();
            }
        }
    }

    internal sealed class SessionMetadataSyncResult
    {
        internal readonly int ChangedCount;
        internal readonly string BackupDirectory;
        internal readonly int SkippedMissingCount;
        private readonly List<SessionMetadataChange> changes;

        internal SessionMetadataSyncResult(
            int changedCount,
            string backupDirectory,
            List<SessionMetadataChange> metadataChanges,
            int skippedMissingCount)
        {
            ChangedCount = changedCount;
            BackupDirectory = backupDirectory;
            changes = metadataChanges;
            SkippedMissingCount = skippedMissingCount;
        }

        internal void Rollback()
        {
            foreach (SessionMetadataChange change in changes)
            {
                string temporary = change.Path + "." + Guid.NewGuid().ToString("N") + ".rollback.tmp";
                try
                {
                    File.WriteAllBytes(temporary, change.OriginalBytes);
                    File.Copy(temporary, change.Path, true);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }
        }
    }

    internal sealed class LaunchTarget
    {
        private readonly string fileName;
        private readonly string arguments;
        private readonly bool useShellExecute;

        internal readonly string DisplayName;

        private LaunchTarget(string displayName, string fileName, string arguments, bool useShellExecute)
        {
            DisplayName = displayName;
            this.fileName = fileName;
            this.arguments = arguments;
            this.useShellExecute = useShellExecute;
        }

        internal static LaunchTarget ForShellApp(string appId)
        {
            return new LaunchTarget(
                "shell:AppsFolder\\" + appId,
                "explorer.exe",
                "shell:AppsFolder\\" + appId,
                false);
        }

        internal static LaunchTarget ForExecutable(string executable)
        {
            return new LaunchTarget(executable, executable, string.Empty, true);
        }

        internal ProcessStartInfo CreateStartInfo()
        {
            ProcessStartInfo info = new ProcessStartInfo(fileName);
            info.Arguments = arguments;
            info.UseShellExecute = useShellExecute;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            return info;
        }
    }

    internal sealed class ThirdPartyProfile
    {
        internal readonly string Name;
        internal readonly string BaseUrl;
        internal readonly string Model;
        internal readonly string CredentialFileName;

        internal ThirdPartyProfile(
            string name,
            string baseUrl,
            string model,
            string credentialFileName)
        {
            Name = name ?? string.Empty;
            BaseUrl = baseUrl ?? string.Empty;
            Model = model ?? string.Empty;
            CredentialFileName = credentialFileName ?? string.Empty;
        }
    }

    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CodexApiSwitcher";
        private const string StartupArgument = "--startup-launch";

        internal static bool IsEnabled(string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null)
                {
                    return false;
                }

                string value = key.GetValue(ValueName) as string;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                string expected = BuildCommand(executablePath);
                return string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static void SetEnabled(bool enabled, string executablePath)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");
                }

                if (enabled)
                {
                    key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }

        private static string BuildCommand(string executablePath)
        {
            string cleanPath = Path.GetFullPath(executablePath ?? string.Empty);
            return Quote(cleanPath) + " " + StartupArgument;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class MouseButtonSetting
    {
        internal static readonly MouseButtonSetting None = new MouseButtonSetting(0);
        internal static readonly MouseButtonSetting XButton1 = new MouseButtonSetting(1);
        internal static readonly MouseButtonSetting XButton2 = new MouseButtonSetting(2);

        private readonly int xButton;

        private MouseButtonSetting(int xButtonNumber)
        {
            xButton = xButtonNumber;
        }

        internal bool IsEnabled
        {
            get { return xButton == 1 || xButton == 2; }
        }

        internal static MouseButtonSetting ParseOrDefault(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return None;
            }

            try
            {
                return Parse(value);
            }
            catch
            {
                return None;
            }
        }

        internal static MouseButtonSetting Parse(string value)
        {
            string clean = (value ?? string.Empty).Trim();
            if (clean.Length == 0 ||
                clean.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("关闭", StringComparison.OrdinalIgnoreCase))
            {
                return None;
            }
            if (clean.Equals("XButton1", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("MouseXButton1", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("侧键1", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("鼠标侧键1", StringComparison.OrdinalIgnoreCase))
            {
                return XButton1;
            }
            if (clean.Equals("XButton2", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("MouseXButton2", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("侧键2", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("鼠标侧键2", StringComparison.OrdinalIgnoreCase))
            {
                return XButton2;
            }

            throw new InvalidOperationException("无法识别鼠标侧键设置：" + value);
        }

        internal static MouseButtonSetting FromComboIndex(int index)
        {
            if (index == 1) return XButton1;
            if (index == 2) return XButton2;
            return None;
        }

        internal static MouseButtonSetting FromXButton(int xButtonNumber)
        {
            if (xButtonNumber == 1) return XButton1;
            if (xButtonNumber == 2) return XButton2;
            return None;
        }

        internal int ToComboIndex()
        {
            return xButton == 1 ? 1 : (xButton == 2 ? 2 : 0);
        }

        internal string ToDisplayString()
        {
            if (xButton == 1) return "XButton1";
            if (xButton == 2) return "XButton2";
            return "None";
        }

        public override bool Equals(object obj)
        {
            MouseButtonSetting other = obj as MouseButtonSetting;
            return other != null && other.xButton == xButton;
        }

        public override int GetHashCode()
        {
            return xButton;
        }
    }

    internal sealed class HotkeySetting
    {
        private const uint ModAltValue = 0x0001;
        private const uint ModControlValue = 0x0002;
        private const uint ModShiftValue = 0x0004;
        private const uint ModWinValue = 0x0008;

        internal static readonly HotkeySetting Default = new HotkeySetting(true, true, false, false, Keys.C);

        internal readonly bool Control;
        internal readonly bool Alt;
        internal readonly bool Shift;
        internal readonly bool Windows;
        internal readonly Keys KeyCode;

        private HotkeySetting(bool control, bool alt, bool shift, bool windows, Keys keyCode)
        {
            Control = control;
            Alt = alt;
            Shift = shift;
            Windows = windows;
            KeyCode = keyCode;
        }

        internal static HotkeySetting ParseOrDefault(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Default;
            }

            try
            {
                return Parse(value);
            }
            catch
            {
                return Default;
            }
        }

        internal static HotkeySetting Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("快捷键不能为空。");
            }

            bool control = false;
            bool alt = false;
            bool shift = false;
            bool windows = false;
            Keys keyCode = Keys.None;
            string[] parts = value.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    control = true;
                    continue;
                }
                if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    alt = true;
                    continue;
                }
                if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    shift = true;
                    continue;
                }
                if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                    part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    windows = true;
                    continue;
                }

                if (keyCode != Keys.None)
                {
                    throw new InvalidOperationException("快捷键只能包含一个主键。");
                }

                keyCode = ParseKeyCode(part);
            }

            return Create(control, alt, shift, windows, keyCode);
        }

        internal static HotkeySetting FromKeys(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            bool control = (keyData & Keys.Control) == Keys.Control;
            bool alt = (keyData & Keys.Alt) == Keys.Alt;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            return Create(control, alt, shift, false, keyCode);
        }

        internal uint ToModifierFlags()
        {
            uint modifiers = 0;
            if (Alt) modifiers |= ModAltValue;
            if (Control) modifiers |= ModControlValue;
            if (Shift) modifiers |= ModShiftValue;
            if (Windows) modifiers |= ModWinValue;
            return modifiers;
        }

        internal uint ToVirtualKey()
        {
            return (uint)KeyCode;
        }

        internal string ToDisplayString()
        {
            List<string> parts = new List<string>();
            if (Control) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Windows) parts.Add("Win");
            parts.Add(GetKeyDisplayName(KeyCode));
            return string.Join("+", parts.ToArray());
        }

        private static HotkeySetting Create(bool control, bool alt, bool shift, bool windows, Keys keyCode)
        {
            if (!control && !alt && !shift && !windows)
            {
                throw new InvalidOperationException("快捷键至少需要包含 Ctrl、Alt、Shift 或 Win 中的一个修饰键。");
            }
            if (!IsValidMainKey(keyCode))
            {
                throw new InvalidOperationException("请按一个有效主键，例如 Ctrl+Alt+C。");
            }

            return new HotkeySetting(control, alt, shift, windows, keyCode);
        }

        private static Keys ParseKeyCode(string value)
        {
            if (value.Length == 1 && char.IsLetter(value[0]))
            {
                return (Keys)Enum.Parse(typeof(Keys), value.ToUpperInvariant());
            }
            if (value.Length == 1 && char.IsDigit(value[0]))
            {
                return (Keys)Enum.Parse(typeof(Keys), "D" + value);
            }

            Keys key;
            if (Enum.TryParse(value, true, out key))
            {
                return key;
            }

            throw new InvalidOperationException("无法识别快捷键主键：" + value);
        }

        private static bool IsValidMainKey(Keys keyCode)
        {
            if (keyCode == Keys.None ||
                keyCode == Keys.ControlKey ||
                keyCode == Keys.Menu ||
                keyCode == Keys.ShiftKey ||
                keyCode == Keys.LWin ||
                keyCode == Keys.RWin)
            {
                return false;
            }

            return true;
        }

        private static string GetKeyDisplayName(Keys keyCode)
        {
            if (keyCode >= Keys.D0 && keyCode <= Keys.D9)
            {
                return ((int)(keyCode - Keys.D0)).ToString();
            }
            return keyCode.ToString();
        }
    }

    internal sealed class NativeSqlite : IDisposable
    {
        private const int SqliteOk = 0;
        private const int SqliteRow = 100;
        private const int SqliteDone = 101;
        private IntPtr handle;

        private NativeSqlite(IntPtr databaseHandle)
        {
            handle = databaseHandle;
        }

        internal static NativeSqlite Open(string path)
        {
            IntPtr database;
            int result = sqlite3_open16(path, out database);
            if (result != SqliteOk)
            {
                string message = GetError(database, result);
                if (database != IntPtr.Zero)
                {
                    sqlite3_close(database);
                }
                throw new InvalidOperationException("Unable to open SQLite database: " + message);
            }
            sqlite3_busy_timeout(database, 30000);
            return new NativeSqlite(database);
        }

        internal static void Backup(string sourcePath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                throw new IOException("Backup destination already exists: " + destinationPath);
            }

            using (NativeSqlite source = Open(sourcePath))
            using (NativeSqlite destination = Open(destinationPath))
            {
                IntPtr backup = sqlite3_backup_init(
                    destination.handle,
                    "main",
                    source.handle,
                    "main");
                if (backup == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "Unable to initialize SQLite backup: " +
                        GetError(destination.handle, sqlite3_errcode(destination.handle)));
                }

                int stepResult;
                try
                {
                    stepResult = sqlite3_backup_step(backup, -1);
                }
                finally
                {
                    int finishResult = sqlite3_backup_finish(backup);
                    if (finishResult != SqliteOk)
                    {
                        throw new InvalidOperationException(
                            "Unable to finish SQLite backup: " +
                            GetError(destination.handle, finishResult));
                    }
                }

                if (stepResult != SqliteDone)
                {
                    throw new InvalidOperationException(
                        "Unable to copy SQLite backup: " +
                        GetError(destination.handle, stepResult));
                }
            }
        }

        internal void Execute(string sql)
        {
            IntPtr errorPointer;
            int result = sqlite3_exec(handle, sql, IntPtr.Zero, IntPtr.Zero, out errorPointer);
            if (result == SqliteOk)
            {
                return;
            }

            string message = errorPointer == IntPtr.Zero
                ? GetError(handle, result)
                : Marshal.PtrToStringAnsi(errorPointer);
            if (errorPointer != IntPtr.Zero)
            {
                sqlite3_free(errorPointer);
            }
            throw new InvalidOperationException("SQLite command failed: " + message);
        }

        internal int ScalarInt(string sql)
        {
            IntPtr statement = Prepare(sql);
            try
            {
                int result = sqlite3_step(statement);
                if (result != SqliteRow)
                {
                    throw new InvalidOperationException(
                        "SQLite query did not return a row: " + GetError(handle, result));
                }
                return sqlite3_column_int(statement, 0);
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }

        internal string ScalarText(string sql)
        {
            IntPtr statement = Prepare(sql);
            try
            {
                int result = sqlite3_step(statement);
                if (result != SqliteRow)
                {
                    throw new InvalidOperationException(
                        "SQLite query did not return a row: " + GetError(handle, result));
                }
                IntPtr value = sqlite3_column_text16(statement, 0);
                return value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(value);
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }

        internal List<string> QueryTextColumn(string sql)
        {
            IntPtr statement = Prepare(sql);
            List<string> values = new List<string>();
            try
            {
                while (true)
                {
                    int result = sqlite3_step(statement);
                    if (result == SqliteDone)
                    {
                        return values;
                    }
                    if (result != SqliteRow)
                    {
                        throw new InvalidOperationException(
                            "SQLite query failed: " + GetError(handle, result));
                    }
                    IntPtr value = sqlite3_column_text16(statement, 0);
                    values.Add(value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(value));
                }
            }
            finally
            {
                sqlite3_finalize(statement);
            }
        }

        private IntPtr Prepare(string sql)
        {
            IntPtr statement;
            int result = sqlite3_prepare16_v2(handle, sql, -1, out statement, IntPtr.Zero);
            if (result != SqliteOk)
            {
                throw new InvalidOperationException(
                    "Unable to prepare SQLite query: " + GetError(handle, result));
            }
            return statement;
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                sqlite3_close(handle);
                handle = IntPtr.Zero;
            }
        }

        private static string GetError(IntPtr database, int result)
        {
            if (database == IntPtr.Zero)
            {
                return "SQLite error " + result;
            }
            IntPtr pointer = sqlite3_errmsg16(database);
            string message = pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(pointer);
            return string.IsNullOrWhiteSpace(message) ? "SQLite error " + result : message;
        }

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open16(
            [MarshalAs(UnmanagedType.LPWStr)] string filename,
            out IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int sqlite3_exec(
            IntPtr database,
            string sql,
            IntPtr callback,
            IntPtr callbackArgument,
            out IntPtr errorMessage);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr pointer);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare16_v2(
            IntPtr database,
            [MarshalAs(UnmanagedType.LPWStr)] string sql,
            int byteCount,
            out IntPtr statement,
            IntPtr remainingSql);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr statement);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr statement, int columnIndex);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text16(IntPtr statement, int columnIndex);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg16(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_errcode(IntPtr database);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr sqlite3_backup_init(
            IntPtr destinationDatabase,
            string destinationName,
            IntPtr sourceDatabase,
            string sourceName);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_backup_step(IntPtr backup, int pageCount);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_backup_finish(IntPtr backup);
    }

    internal sealed class ProviderStatus
    {
        internal readonly string Provider;
        internal readonly string Model;
        internal readonly string BaseUrl;
        internal readonly bool UsesCredentialHelper;
        internal readonly bool ReusesOpenAiLogin;
        internal readonly bool HasCodexApiKeyOverride;

        internal ProviderStatus(
            string provider,
            string model,
            string baseUrl,
            bool usesCredentialHelper,
            bool reusesOpenAiLogin,
            bool hasCodexApiKeyOverride)
        {
            Provider = provider ?? string.Empty;
            Model = model ?? string.Empty;
            BaseUrl = baseUrl ?? string.Empty;
            UsesCredentialHelper = usesCredentialHelper;
            ReusesOpenAiLogin = reusesOpenAiLogin;
            HasCodexApiKeyOverride = hasCodexApiKeyOverride;
        }

        internal bool IsThirdParty
        {
            get { return string.Equals(Provider, "custom", StringComparison.OrdinalIgnoreCase); }
        }

        internal string ToDisplayString()
        {
            if (!IsThirdParty)
            {
                string warning = HasCodexApiKeyOverride
                    ? " | 警告：CODEX_API_KEY 正在覆盖 ChatGPT 登录"
                    : string.Empty;
                return "官方 OpenAI 登录 | 模型 " + Model + warning;
            }

            string auth = UsesCredentialHelper
                ? "独立加密 Key"
                : (ReusesOpenAiLogin ? "复用 OpenAI 登录" : "未检测到认证");
            return "第三方 Responses API | " + BaseUrl + " | 模型 " + Model + " | " + auth;
        }
    }

    internal sealed class StoredSettings
    {
        internal string BaseUrl = string.Empty;
        internal string ThirdPartyModel = string.Empty;
        internal string OfficialModel = string.Empty;
        internal string OpenHotkey = string.Empty;
        internal string OpenMouseButton = string.Empty;
        internal bool ThirdPartyCompatibilityMode = false;

        internal string GetOpenHotkey()
        {
            return HotkeySetting.ParseOrDefault(OpenHotkey).ToDisplayString();
        }

        internal string GetOpenMouseButton()
        {
            return MouseButtonSetting.ParseOrDefault(OpenMouseButton).ToDisplayString();
        }

        internal bool GetThirdPartyCompatibilityMode()
        {
            return ThirdPartyCompatibilityMode;
        }
    }
}
