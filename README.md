# Codex API Switcher 使用说明

Codex API Switcher（CAS）用来在 Codex 的官方 OpenAI 登录和第三方 OpenAI 兼容 Responses API 之间切换。它会自动备份配置和历史索引，API Key 使用系统安全存储保存，不会写进 `config.toml`。

## 下载安装

请到 GitHub Releases 下载对应系统版本：

- macOS Apple Silicon / M 系列：[CodexApiSwitcher-macos-arm64.zip](https://github.com/jiali9974-ai/CodexApiSwitcher/releases/download/v2.1.0/CodexApiSwitcher-macos-arm64.zip)
- macOS Intel：[CodexApiSwitcher-macos-x64.zip](https://github.com/jiali9974-ai/CodexApiSwitcher/releases/download/v2.1.0/CodexApiSwitcher-macos-x64.zip)
- Release 页面：[v2.1.0](https://github.com/jiali9974-ai/CodexApiSwitcher/releases/tag/v2.1.0)

Windows 和 Linux 版本可以从源码构建，或使用本地构建产物发布到 Releases。

## macOS 怎么打开

1. 下载与你电脑架构对应的 zip。
   - M 系列芯片选 arm64。
   - Intel Mac 选 x64。
2. 在 macOS 上解压 zip。
3. 双击解压出来的 `.app`。
4. 如果系统提示“无法验证开发者”：
   - 打开“系统设置”。
   - 进入“隐私与安全性”。
   - 在底部找到 CAS 的拦截提示，点击“仍要打开”。
5. 如果快捷键或鼠标侧键没有反应：
   - 打开“系统设置”。
   - 进入“隐私与安全性”。
   - 进入“辅助功能”。
   - 给 Codex API Switcher 授权。
6. 如果仍打不开，可以在终端查看日志：

```sh
tail -n 80 ~/Library/Logs/CodexApiSwitcher.log
```

如需手动放行隔离属性：

```sh
xattr -dr com.apple.quarantine ./CodexApiSwitcher-macos-arm64.app
# 或
xattr -dr com.apple.quarantine ./CodexApiSwitcher-macos-x64.app
```

当前发布包使用 ad-hoc 签名，不是 Apple Developer ID 公证包，所以首次运行可能仍需要手动允许。

## Windows 怎么打开

1. 双击 `CodexApiSwitcher-win-x64.exe`。
2. 选择包含 `config.toml` 的 Codex 根目录。
3. 如果 Windows 安全提示，请确认来源是你的仓库发布包后再允许运行。

## Linux 怎么打开

1. 给文件加执行权限：

```sh
chmod +x ./CodexApiSwitcher-linux-x64
# 或
chmod +x ./CodexApiSwitcher-linux-arm64
```

2. 运行：

```sh
./CodexApiSwitcher-linux-x64
# 或
./CodexApiSwitcher-linux-arm64
```

Linux 桌面需要基础系统库，例如 glibc、libstdc++、ICU、fontconfig、X11/AppIndicator 相关库。全局快捷键和鼠标侧键以 X11 支持最好，Wayland 下取决于桌面环境权限策略。

## 第一次使用

1. 先彻底退出 Codex。
2. 打开 CAS。
3. 选择 Codex 根目录。
   - 如果设置过 `CODEX_HOME`，通常选择该目录。
   - 否则通常是用户目录下的 `.codex`。
   - 目录里应该能看到 `config.toml`。
4. CAS 会读取当前状态，并显示现在是官方模式还是第三方模式。

切换前请一定先退出 Codex。这样可以避免 Codex 正在写入数据库或会话文件时发生冲突。

## 切换到第三方 API

1. 填写“第三方 Base URL”。
   - 示例：`https://api.example.com`
   - 工具会自动规范为 `https://api.example.com/v1`。
2. 填写“第三方模型”。
   - 示例：`gpt-5.5`、`claude-sonnet-4`，按你的中转站实际模型名填写。
3. 首次填写“第三方 API Key”。
4. 点击“切换到第三方 API”。
5. 切换完成后重新打开 Codex。

API Key 会加密保存：

- Windows：DPAPI
- macOS：Keychain
- Linux：Secret Service；无密钥环时使用当前用户 0600 权限的本地加密保险库

## 切换回官方登录

1. 先彻底退出 Codex。
2. 在“官方模型”里填写你想使用的官方模型名。
3. 点击“切换到官方登录”。
4. 重新打开 Codex。

切回官方登录不会删除第三方 Key，也不会修改 `auth.json`。

## 保存和使用中转站档案

如果你有多个第三方中转站，可以用档案管理：

1. 填写 Base URL、模型和 API Key。
2. 点击“保存档案”。
3. 下次从“中转站档案”下拉框选择即可。
4. 不想保留时，选择档案后点击“删除档案”。

删除档案会删除本机保存的对应加密 Key。

## 兼容模式什么时候开

如果第三方中转站报类似错误：

```text
Image generation is not enabled for this group
```

可以勾选“兼容模式”，再切换到第三方 API。

兼容模式会临时关闭 Codex 生图、图片查看、Web 搜索和部分高级插件能力，减少中转站因为不支持这些工具而报错。切回官方或关闭兼容模式后，工具会恢复之前的配置。

## 会话列表不见了怎么办

如果 Codex 左侧历史会话为空，先彻底退出 Codex，然后点击：

```text
修复会话列表
```

CAS 会备份状态数据库，然后恢复顶层用户会话的可见标记。它不会改动会话正文。

## 配置坏了怎么办

如果误删了 `model_provider`、`model`，或者第三方 provider 配置损坏，点击：

```text
恢复基础配置
```

CAS 会先备份 `config.toml`，再恢复官方基础模型配置。MCP、插件、沙箱、记忆、会话和 `auth.json` 会保留。

## 启动和关闭 Codex

CAS 提供两个按钮：

- “启动 Codex”：尝试打开 Codex 桌面应用或 `codex` 命令。
- “关闭 Codex 后台”：结束 Codex 相关后台进程，方便你切换配置。

切换 API 前建议使用“关闭 Codex 后台”，确保 Codex 已经完全退出。

## 快捷键、鼠标侧键和开机启动

- “设置快捷键”：设置打开/隐藏 CAS 的全局快捷键。
- “鼠标侧键”：可选择侧键 1 或侧键 2 呼出/隐藏 CAS。
- “开机启动”：登录系统后 CAS 自动驻留后台。

Windows、macOS、Linux 都有单实例保护。重复打开 CAS 不会多开后台进程，而是唤醒已有窗口。

## 自动备份在哪里

CAS 会自动备份关键文件：

- `config-switcher-backups`：配置备份
- `history_sync_backups`：状态库和会话元数据备份

备份目录位于你选择的 Codex 根目录下。

## CAS 不会修改什么

CAS 不会修改：

- 会话正文
- `memories`
- `session_index.jsonl`
- `auth.json`
- MCP 配置主体
- 插件配置主体

它主要修改的是：

- `config.toml` 中的模型/provider 配置
- 状态数据库中的会话 provider 元数据
- 会话 JSONL 第一行的 provider 元数据
- 侧栏可见标记（仅在修复会话列表时）

## 构建源码

需要 .NET 10 SDK。

macOS / Linux：

```sh
sh work/build-cross-platform.sh osx-arm64
sh work/build-cross-platform.sh osx-x64
```

Windows PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work\build-cross-platform.ps1
```

运行测试：

```sh
CAS_TEST_REAL_KEYCHAIN=1 sh work/test-macos.sh
```

Windows：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work\test-cross-platform.ps1
```

## 常见问题

### 为什么 Git 仓库里没有直接放 .app？

macOS `.app` 内部的自包含主程序超过 GitHub 普通 Git 单文件 100 MB 限制，所以不放进 Git 历史。请从 GitHub Releases 下载 zip，zip 解压后就是 `.app`。

### 为什么 macOS 首次运行要手动允许？

当前发布包是 ad-hoc 签名，不是 Apple Developer ID 公证包。macOS Gatekeeper 会要求你在“隐私与安全性”里确认一次。

### 为什么切换后要重启 Codex？

Codex 启动时读取配置。切换配置后必须重新打开 Codex，新的 provider 才会生效。

### Base URL 要填到哪里？

填站点根地址即可，例如：

```text
https://api.example.com
```

CAS 会自动补成：

```text
https://api.example.com/v1
```

如果你填了 `/v1/responses` 或 `/v1/chat/completions`，CAS 也会规范回 `/v1`。
