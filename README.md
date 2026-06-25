# Codex API Switcher

[中文](#中文) | [English](#english)

## 中文

Codex API Switcher 是一个 Windows 图形界面工具，用来在 Codex Desktop 的官方 OpenAI 登录和第三方 OpenAI 兼容 Responses API 之间切换。

### 功能

- 在官方 OpenAI 登录和自定义 Responses API provider 之间切换。
- 使用 Windows DPAPI 为当前 Windows 用户加密保存第三方 API Key。
- 支持保存多个第三方中转站档案，Base URL、模型和 Key 可一键切换。
- 支持启动/关闭 Codex 后台进程，切换前会提醒彻底退出 Codex。
- 支持键盘快捷键、鼠标侧键呼出/隐藏窗口。
- 支持可选开机启动；关闭窗口后可驻留托盘。
- 支持第三方兼容模式，用于部分中转站不支持 Codex 生图/图片工具时临时降级工具能力；默认关闭。
- 保留 MCP、插件、记忆文件、会话正文和 `auth.json`。
- 修改前自动备份 `config.toml`、检测到的新旧状态数据库和被改动的会话元数据。
- 兼容根目录旧库 `state_5.sqlite` 与新版活动库 `sqlite/state_5.sqlite`。
- 通过同步 provider 元数据，让官方模式和第三方模式看到一致的历史会话。
- 在模型/provider 配置损坏时，一键重建基础配置，同时保留无关配置。
- Codex 更新导致历史路径缺失时，会跳过失效路径并提示可能是 Codex 更新造成。
- Windows 10/11 单 EXE 运行，不需要安装 Python 或 SQLite。

### 文件

- `outputs/CodexApiSwitcher.exe`：可直接运行的图形界面程序。
- `outputs/使用说明.txt`：中文使用说明。
- `outputs/cas-logo.ico`：构建时生成的程序图标。
- `work/CodexApiSwitcher.cs`：WinForms 源码。
- `work/build-codex-api-switcher.ps1`：构建脚本。
- `work/test-codex-api-switcher-exe.ps1`：回归测试。

### 构建与测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work\build-codex-api-switcher.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File work\test-codex-api-switcher-exe.ps1
```


## English

Codex API Switcher is a Windows GUI tool for switching Codex Desktop between official OpenAI login and a third-party OpenAI-compatible Responses API provider.

### Features

- Switch between official OpenAI login and a custom Responses API provider.
- Store the third-party API key with Windows DPAPI for the current Windows user.
- Save multiple third-party relay profiles and switch Base URL, model, and key quickly.
- Start or close Codex background processes from the GUI.
- Open or hide the switcher with a configurable keyboard shortcut or mouse side button.
- Optional Windows startup integration and tray residency.
- Optional third-party compatibility mode for relay providers that do not support Codex image/image-view tooling; disabled by default.
- Preserve MCP, plugins, memory files, session content, and `auth.json`.
- Back up `config.toml`, detected legacy/current state databases, and changed session metadata before edits.
- Support both legacy `state_5.sqlite` and current `sqlite/state_5.sqlite` locations.
- Keep visible conversation history aligned across providers by syncing provider metadata.
- Rebuild a damaged model/provider section while preserving unrelated config.
- Skip stale history paths with a Codex-update notice when newer Codex versions move or remove session files.
- Single EXE on Windows 10/11; no Python or SQLite installation required.

### Files

- `outputs/CodexApiSwitcher.exe`: ready-to-run GUI.
- `outputs/使用说明.txt`: Chinese usage guide.
- `outputs/cas-logo.ico`: generated application icon.
- `work/CodexApiSwitcher.cs`: WinForms source.
- `work/build-codex-api-switcher.ps1`: build script.
- `work/test-codex-api-switcher-exe.ps1`: regression test.

### Build and test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work\build-codex-api-switcher.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File work\test-codex-api-switcher-exe.ps1
```
