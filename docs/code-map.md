# Codex API Switcher Code Map

本文件索引官方登录与第三方 Responses API 切换工具。

最近完整验收时间：2026-06-25，本目录已同步当前可编译源码与发布 EXE。

## 先看这里

| 目标 | 主要文件 | 配套测试 | 验证命令 |
| --- | --- | --- | --- |
| 使用 GUI 切换模式 | `outputs/CodexApiSwitcher.exe` | `work/test-codex-api-switcher-exe.ps1` | `powershell -ExecutionPolicy Bypass -File work/test-codex-api-switcher-exe.ps1` |
| 修改 GUI、切换或基础配置恢复逻辑 | `work/CodexApiSwitcher.cs` | `work/test-codex-api-switcher-exe.ps1` | `powershell -ExecutionPolicy Bypass -File work/build-codex-api-switcher.ps1` |

## End-To-End Flow

```text
用户选择 Codex 根目录和模式
-> 读取 <CODEX_HOME>\config.toml
-> 创建 config-switcher-backups 时间戳备份
-> 第三方 Key 以当前 Windows 用户 DPAPI 密文保存到 api-switcher
-> 根 URL 自动规范为 Codex 所需的 /v1 API 基址
-> 通过 Windows winsqlite3.dll 备份 state_5.sqlite
-> 自动识别根目录旧库与 sqlite 子目录活动库
-> 从 threads.rollout_path 定位顶层用户会话 JSONL
-> 逐文件备份并原子更新 JSONL 第一行 payload.model_provider
-> 同步所有已发现状态库中的 model_provider 与 has_user_event
-> 修改 model/model_provider/model_providers.custom
-> 同目录临时文件覆盖 config.toml
-> 重启 Codex 后生效
```

## Code Map

### GUI 与 API 模式切换

`outputs/CodexApiSwitcher.exe`

面向用户的 WinForms 单文件程序。支持选择 Codex 根目录、填写 URL/Key/模型、查看当前状态、切换官方/第三方模式、恢复最近备份，以及在模型配置损坏时一键重建官方基础配置。
当前版本还支持保存多个第三方中转站档案、键盘快捷键/鼠标侧键呼出或隐藏窗口、可选开机启动、关闭到托盘、启动/关闭 Codex 后台进程，以及可选第三方兼容模式。第三方兼容模式默认关闭；只有用户勾选或命令行显式传入 `--compat-mode` 时才会临时降级 Codex 工具能力。
每次切换都会先确认 Codex 已退出，自动识别并分别备份根目录旧库 `state_5.sqlite` 与新版活动库 `sqlite/state_5.sqlite`，再合并两套数据库的 `threads.rollout_path` 定位 `vscode`/`cli` 顶层用户会话。工具逐文件备份并原子修改 JSONL 第一行的 `payload.model_provider`，随后同步每套数据库的 `model_provider` 与 `has_user_event`。会话正文和后续 JSONL 行保持不变。独立的“修复会话列表”会修复所有已发现状态库的可见标记。
“一键恢复基础配置”会先备份 `config.toml`，补回顶层 `model_provider`/`model`，移除损坏的 `model_providers.custom` 段，并保留 MCP、插件、沙箱等其他配置；第三方切换时会重新生成 custom provider。
当 Codex 更新导致数据库中的 JSONL 路径失效时，工具会跳过缺失路径、保留切换结果，并在界面/命令行提示“可能是 Codex 更新导致路径变化”。

`work/CodexApiSwitcher.cs`

GUI 与切换核心源码。EXE 同时提供 `--emit-token` 凭据助手模式，供 Codex 获取 DPAPI 解密后的第三方 Key。SQLite 操作通过 P/Invoke 调用 Windows 自带 `winsqlite3.dll`，不启动 Python 或外部 SQLite 程序。

`work/build-codex-api-switcher.ps1`

使用 Windows PowerShell 自带 C# 编译器构建 EXE，不依赖额外 SDK。

### 测试

`work/test-codex-api-switcher-exe.ps1`

验证 EXE 在 PATH 不含 Python 时仍可双向切换，同时同步 SQLite 与 JSONL provider 元数据，确认会话正文不变，并覆盖侧栏修复、基础配置重建、Base URL `/v1` 规范化、DPAPI 凭据助手、第三方档案加密保存、快捷键保存、开机启动注册表开关、第三方兼容模式显式启用/默认关闭、CODEX_API_KEY 环境变量清理与恢复、自动备份、TOML 解析和无关配置保留。

## Known Runtime Notes

- EXE 只允许修改所选根目录内的 `config.toml`、`config-switcher-backups`、`api-switcher`、`history_sync_backups`、根目录 `state_5.sqlite` 和 `sqlite/state_5.sqlite`。
- 运行依赖仅为 Windows 10/11 自带的 .NET Framework 与 `winsqlite3.dll`；不需要 Python 或另装 SQLite。
- 每次切换会更新顶层用户会话 JSONL 第一行的 `payload.model_provider`，并更新根目录旧库和 `sqlite` 子目录活动库中对应线程的 `model_provider` 与 `has_user_event`；每套数据库和每个 JSONL 修改前都会备份。
- 新版 Codex 可能把活动状态库写入 `<CODEX_HOME>\sqlite\state_5.sqlite`；只修改根目录旧库会出现配置切换成功但两种模式侧栏历史不同。
- JSONL 备份使用“原路径哈希 + 原文件名”的扁平命名，避免长 Codex 根目录触发 Windows 路径长度限制。
- Codex 数据库中的 `rollout_path` 可能使用 `\\?\` Windows 扩展路径前缀；工具会在安全校验和文件访问前移除该前缀，避免把 `?` 误判为非法字符。
- 不修改 JSONL 后续会话正文、`memories*`、`session_index.jsonl` 或 `auth.json`。
- 切换前必须彻底退出 Codex；否则工具会中止，避免数据库并发写入。
- DPAPI 密文只能由生成密文的当前 Windows 用户在本机解密。
- 切换配置后必须重启 Codex。
- `wire_api = "responses"` 的自定义 provider 需要 API 基址包含 `/v1`；站点根 URL 会导致 Codex 请求到网页路由并表现为 `stream closed before response.completed`。
- 部分中转站不支持 Codex 请求中的 `image_generation` 等工具能力，可能返回 `403 Image generation is not enabled for this group`。此时可勾选“兼容模式”；该模式会临时关闭生图、图片查看、Web 搜索和部分高级插件，切回官方或关闭兼容模式时自动恢复。
- 兼容模式默认关闭，旧设置中保存过开启也不会继续作为默认值继承。
- 开机启动通过当前用户注册表 Run 项实现，启动参数为 `--startup-launch`。
- 快捷键和鼠标侧键只负责打开/隐藏切换器窗口，不会自动执行切换操作。
- 侧栏文件仍在但列表为空时，先检查 `threads.has_user_event`；修复前必须退出 Codex并使用 SQLite backup API 备份数据库。
