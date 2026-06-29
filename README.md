# Codex API Switcher

[中文](#中文) | [English](#english)

## 中文

Codex API Switcher 是一个 Windows、macOS、Linux 图形界面工具，用来在 Codex 的官方 OpenAI 登录和第三方 OpenAI 兼容 Responses API 之间切换。跨平台版使用 Avalonia，在三个桌面系统上保持与原 Windows 版一致的布局、色板和操作流程。

### 功能

- 在官方 OpenAI 登录和自定义 Responses API provider 之间切换。
- 使用系统安全存储保护第三方 API Key：Windows DPAPI、macOS Keychain、Linux Secret Service；Linux 无密钥环时使用权限为 `0600` 的本地加密保险库。
- 支持保存多个第三方中转站档案，Base URL、模型和 Key 可一键切换。
- 支持启动/关闭 Codex 后台进程，切换前会提醒彻底退出 Codex。
- 支持键盘快捷键、鼠标侧键呼出/隐藏窗口。
- 支持可选开机启动；关闭窗口后可驻留托盘/菜单栏。
- Windows、macOS、Linux 都启用单实例保护；重复双击会唤醒后台已有窗口，不会多开后台进程。
- 支持第三方兼容模式，用于部分中转站不支持 Codex 生图/图片工具时临时降级工具能力；默认关闭。
- 保留 MCP、插件、记忆文件、会话正文和 `auth.json`。
- 修改前自动备份 `config.toml`、检测到的新旧状态数据库和被改动的会话元数据。
- 兼容根目录旧库 `state_5.sqlite` 与新版活动库 `sqlite/state_5.sqlite`。
- 通过同步 provider 元数据，让官方模式和第三方模式看到一致的历史会话。
- 在模型/provider 配置损坏时，一键重建基础配置，同时保留无关配置。
- Codex 更新导致历史路径缺失时，会跳过失效路径并提示可能是 Codex 更新造成。
- Windows、macOS、Linux 自包含单文件运行，不需要安装 Python、.NET 或 SQLite；Linux 桌面仍需系统基础库（如 glibc、libstdc++、ICU、fontconfig、X11/AppIndicator 相关库）。
- 全局键盘快捷键和鼠标侧键使用跨平台原生钩子；macOS 首次使用可能需要“辅助功能”权限，Linux 当前以 X11 为主，Wayland 下由桌面环境决定。
- macOS `.app` 使用原生 Mach-O 入口、Keychain、LaunchAgent、菜单栏和跨进程文件锁；Apple Silicon 版已覆盖真实 Keychain、全部主要按钮和重复启动唤醒回归。

### 文件

- `outputs/cross-platform/<RID>/`：Windows、macOS、Linux 自包含构建产物。
- `CodexApiSwitcher-win-x64.exe`、`CodexApiSwitcher-linux-*`、`CodexApiSwitcher-macos-*.zip/.app`：构建后放在工作区最外层、发布时上传到 GitHub Releases 的分发产物；它们不进入 Git 历史。macOS 推荐分发 zip，避免裸 `.app` 跨系统复制时丢失可执行位。
- `使用说明-跨平台.txt`：最外层跨平台运行说明。
- `outputs/CodexApiSwitcher.exe`：原 WinForms 兼容版本。
- `outputs/使用说明.txt`：中文使用说明。
- `outputs/cas-logo.ico`：构建时生成的程序图标。
- `src/CodexApiSwitcher/`：Avalonia 跨平台源码。
- `work/CodexApiSwitcher.cs`：原 WinForms 兼容源码。
- `work/build-cross-platform.ps1` / `.sh`：跨平台发布脚本。
- `work/test-cross-platform.ps1`：跨平台核心、Windows UI 按钮和单实例回归测试。
- `work/build-codex-api-switcher.ps1`：构建脚本。
- `work/test-codex-api-switcher-exe.ps1`：回归测试。

### 构建与测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work\build-cross-platform.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File work\test-cross-platform.ps1
```

macOS / Linux 可在安装 .NET 10 SDK 后执行：

```sh
sh work/build-cross-platform.sh osx-arm64
sh work/build-cross-platform.sh osx-x64
CAS_TEST_REAL_KEYCHAIN=1 sh work/test-macos.sh
```

macOS 构建会执行 plist 校验和 ad-hoc 签名，并生成保留 Unix 可执行位的 zip。ad-hoc 签名不等同于 Apple Developer ID 公证，首次下载后系统仍可能要求在“隐私与安全性”中确认打开。


## English

Codex API Switcher is a Windows, macOS, and Linux GUI tool for switching Codex between official OpenAI login and a third-party OpenAI-compatible Responses API provider. The Avalonia UI preserves the layout, colors, and workflow of the original Windows version.

### Features

- Switch between official OpenAI login and a custom Responses API provider.
- Protect third-party API keys with Windows DPAPI, macOS Keychain, or Linux Secret Service; Linux falls back to a `0600` local encrypted vault when no keyring is available.
- Save multiple third-party relay profiles and switch Base URL, model, and key quickly.
- Start or close Codex background processes from the GUI.
- Open or hide the switcher with a configurable keyboard shortcut or mouse side button.
- Optional OS-native startup integration and tray/menu-bar residency.
- Single-instance protection on Windows, macOS, and Linux; launching again opens the existing background instance instead of starting another one.
- Optional third-party compatibility mode for relay providers that do not support Codex image/image-view tooling; disabled by default.
- Preserve MCP, plugins, memory files, session content, and `auth.json`.
- Back up `config.toml`, detected legacy/current state databases, and changed session metadata before edits.
- Support both legacy `state_5.sqlite` and current `sqlite/state_5.sqlite` locations.
- Keep visible conversation history aligned across providers by syncing provider metadata.
- Rebuild a damaged model/provider section while preserving unrelated config.
- Skip stale history paths with a Codex-update notice when newer Codex versions move or remove session files.
- Self-contained single-file builds for Windows, macOS, and Linux; no Python, .NET, or SQLite installation required at runtime. Linux desktops still need baseline system libraries such as glibc, libstdc++, ICU, fontconfig, and X11/AppIndicator-related packages.
- Cross-platform global keyboard and side-button hooks. macOS may request Accessibility permission; Linux support is X11-first and depends on the desktop environment under Wayland.
- The macOS app uses a native Mach-O bundle entry, Keychain, LaunchAgent, menu-bar integration, and a cross-process file lock. The Apple Silicon build is regression-tested against the real Keychain, all primary UI actions, and second-launch activation.

### Files

- `outputs/cross-platform/<RID>/`: self-contained Windows, macOS, and Linux builds.
- `CodexApiSwitcher-win-x64.exe`, `CodexApiSwitcher-linux-*`, and `CodexApiSwitcher-macos-*.zip/.app`: top-level build outputs intended for GitHub Releases rather than Git history; use the macOS zip so executable bits survive cross-OS transfer.
- `使用说明-跨平台.txt`: top-level cross-platform usage guide.
- `outputs/CodexApiSwitcher.exe`: legacy WinForms-compatible build.
- `outputs/使用说明.txt`: Chinese usage guide.
- `outputs/cas-logo.ico`: generated application icon.
- `src/CodexApiSwitcher/`: cross-platform Avalonia source.
- `work/CodexApiSwitcher.cs`: legacy WinForms source.
- `work/build-cross-platform.ps1` / `.sh`: cross-platform publish scripts.
- `work/test-cross-platform.ps1`: cross-platform core, Windows UI-button, and single-instance regression test.
- `work/build-codex-api-switcher.ps1`: build script.
- `work/test-codex-api-switcher-exe.ps1`: regression test.

### Build and test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work\build-cross-platform.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File work\test-cross-platform.ps1
```

On macOS or Linux, install the .NET 10 SDK and run:

```sh
sh work/build-cross-platform.sh osx-arm64
sh work/build-cross-platform.sh osx-x64
CAS_TEST_REAL_KEYCHAIN=1 sh work/test-macos.sh
```

The macOS build validates the plist, applies an ad-hoc signature, and creates a zip that preserves executable modes. Ad-hoc signing is not Apple Developer ID notarization, so macOS may still require first-launch approval under Privacy & Security.
