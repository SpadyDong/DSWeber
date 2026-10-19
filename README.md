# DshTray

把 `dsh web` 收进 Windows 托盘的小工具。后台默默跑着 `dsh web`，不再占一个前台终端窗口；状态、余额、配额一眼可见。

- 运行时只依赖系统自带的 .NET Framework 4.0，无需安装任何运行时
- 常驻内存小、几乎不占 CPU（无轮询渲染，界面按需重绘）
- 单文件 exe，拷到哪都能跑，配置就放在 exe 旁边

## 功能

- **托管 dsh web**：从托盘启动/停止，端口冲突提前拦截，进程异常退出自动重启（最多 5 次）
- **连带退出**：托盘退出时会一并结束它启动的 dsh web（Windows Job Object），不留孤儿进程
- **单实例**：重复双击不会起第二个托盘，只会弹出提示气泡
- **外部进程巡检**：每 60 秒检查一次是否有人在终端里手动跑了 dsh web，避免抢端口
- **开机自启**：写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `DshTray` 项
- **桌面悬浮球**：可拖动、位置持久化、状态配色，左键查余额、右键出菜单
- **余额气泡**：DeepSeek 余额（含峰/谷）、Codex 配额、火山方舟 Coding Plan
- **闹钟**：多条目定时提醒
- **高 DPI 适配**：按显示器逐个感知 DPI，多屏混合缩放不会糊

## 环境要求

- Windows（自带 .NET Framework 4.0+ 即可）
- **Node.js**：`node.exe`，用于拉起 `dsh web`
- **dsh**：已全局安装，探测路径 `%APPDATA%\npm\node_modules\@deepseek-ai\dsh\lib\bin.js`
- 可选 **Codex CLI**：探测路径 `%APPDATA%\npm\node_modules\@openai\codex\bin\codex.js`，用于显示 Codex 配额
- 可选 **火山方舟 CLI**：安装 `npm i @volcengine/ark-cli -g`，探测路径 `%APPDATA%\npm\node_modules\@volcengine\ark-cli\bin\arkcli*windows-amd64*.exe`

## 编译

仓库不含 exe（`dist/*.exe` 与 `dist/config.json` 已被 `.gitignore` 排除），需要本机编译：

```cmd
build.cmd
```

脚本用系统自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 直接编译 `src\desktop-host\*.cs`，产物为 `dist\DshTray.exe`。不需要 Visual Studio 或 MSBuild。

## 首次运行

双击 `dist\DshTray.exe`。程序以自己所在目录作为数据目录：

- 找不到 `config.json`，或配置缺失/无效时，会弹出配置窗口，填好保存后开始运行
- 配置窗口里的路径、Host、端口都有默认值，一般只需要填 DeepSeek API Key（也可以留空）
- 端口被其他程序占用时会直接提示并退出，不会硬起

`config.json` 与 `DshTray.exe` 必须在同一目录。

## 配置项

| 键 | 含义 | 默认值 |
| --- | --- | --- |
| `NodePath` | `node.exe` 的完整路径 | 从 `PATH` 里找到的第一个 `node.exe` |
| `DshScript` | dsh 的 `lib\bin.js` 完整路径 | `%APPDATA%\npm\node_modules\@deepseek-ai\dsh\lib\bin.js` |
| `WorkingDirectory` | `dsh web` 的工作目录 | `DshTray.exe` 所在目录 |
| `Host` | 监听地址 | `127.0.0.1` |
| `Port` | 监听端口 | `3080` |
| `NoOpen` | 启动 `dsh web` 时附加 `--no-open` | `false` |
| `AutoRestart` | 进程异常退出后自动重启（最多 5 次） | `true` |
| `AutoStart` | 开机自动启动本托盘程序 | `true` |
| `ShowBall` | 显示桌面悬浮球 | `true` |
| `BallX` / `BallY` | 悬浮球坐标，拖动后自动保存 | `0` / `0`（左上角） |
| `ShowCodexQuota` | 在余额气泡里显示 Codex 配额 | `true` |
| `ApiKey` | DeepSeek API Key | 空 |
| `Alarms` | 闹钟列表，每项 `{ Enabled, Time, Message }` | 空列表 |

实际执行的命令等价于：

```cmd
"<NodePath>" "<DshScript>" web --host <Host> --port <Port> [--no-open]
```

Web 地址为 `http://<Host>:<Port>/`（`Host` 为 `0.0.0.0` 或 `::` 时按 `127.0.0.1` 显示）。

`ApiKey` 保存时会用 Windows DPAPI（当前用户）加密，以 `dpapi:` 前缀写回 `config.json`。

## 托盘菜单

| 项 | 说明 |
| --- | --- |
| 状态：xxx | 点开看状态详情：地址、PID、已运行时长、自动重启次数、最近错误 |
| 打开 Web 界面 | 用默认浏览器打开 `http://host:port/`（双击托盘图标同效） |
| 启动 / 停止 | 启停 `dsh web` |
| 开机启动 | 勾选即写入当前用户的启动项 |
| 桌面悬浮球 | 勾选即显示/隐藏悬浮球，设置会被保存 |
| 闹钟 | 打开闹钟设置（添加/编辑/删除，勾选启用） |
| 重新登录火山方舟 | 打开 `arkcli auth login volc-sso` 走一遍登录，完成后自动重查配额 |
| 设置 | 改路径、Host、端口、API Key、自启与 Codex 显示等，保存后会重启服务并重建悬浮球 |
| 退出 | 结束托盘，同时结束它拉起的 dsh web |

## 悬浮球

- **左键拖动**：移动位置，松手后坐标写入配置；拖到屏幕外会自动拉回工作区
- **左键单击**：弹出余额气泡
- **右键**：弹出托盘菜单（这里**不含**「退出」，避免误触）

配色（不使用绿色）：

| 状态 | 颜色 |
| --- | --- |
| 运行中 | 蓝 `#4D6BFE` |
| 启动中 | 蓝与灰每 500ms 交替闪烁 |
| 已停止 | 灰 `#969696` |
| 已异常退出 / 端口冲突 | 红 `#D64545` |

## 余额气泡

点击悬浮球弹出，**不会自动消失**，点一下卡片才关闭。三块内容按需显示，用分隔线隔开。

### DeepSeek 余额

- 数据源 `GET https://api.deepseek.com/user/balance`，超时 10 秒
- 标题右侧带 `（峰）` 或 `（谷）`
- 峰谷规则（北京时间）：工作日 `09:00–12:00`、`14:00–18:00` 为峰，其余时间为谷；周末与法定节假日全天为谷
- 内置 2026 年法定节假日与调休表；**其它年份不显示峰谷标识**
- 余额数值 ≥ 10 显示绿色，< 10 显示红色

### Codex 配额

- 通过 `node "<codex.js>" app-server --listen stdio://` 走 JSON-RPC（`initialize` → `initialized` → `account/rateLimits/read`），超时 8 秒
- 显示的是 `/status` 里的 **left**（即 `100 - usedPercent`），≥ 10 绿、< 10 红
- 标题带套餐类型（Free / Go / Plus / Pro / Pro Lite / Team / Business / Enterprise …）
- 行标签按窗口时长推导：`5 小时`、`7 天`，或 `N 天` / `N 小时` / `N 分钟`。Pro 这类没有 5 小时窗口的账户会少一行，不会硬凑
- 标签后跟灰色重置时间：当天显示 `（HH:mm重置）`，其它日期显示 `（M月d日重置）`
- 可在「设置」里关掉

### 火山方舟 Coding Plan

- 数据源 `arkcli usage plan --product coding-plan --format json`，超时 8 秒
- 显示 `5 小时`（session）与 `一个月`（monthly）剩余额度，≥ 10 绿、< 10 红
- **未订阅 Coding Plan 时整栏不显示**，不会出现「未订阅」这种字样
- 登录过期时显示「登录已过期」，从托盘菜单点「重新登录火山方舟」处理

### 刷新策略

托盘每 60 秒在后台刷新一次三路数据，缓存有效期 2 分钟。点击悬浮球时优先直接显示缓存，秒开；没有缓存才发起实时查询。

## API Key 来源

按优先级：

1. 「设置」里填写的 `ApiKey`
2. `%USERPROFILE%\.dsh\.credentials.yaml` 里形如 `DEEPSEEK_API_KEY: xxx` 的配置行

两者都没有时，气泡会提示「未找到 API Key」。

## 闹钟

- 在托盘菜单「闹钟」里维护多条提醒，每条含启用开关、时间（HH:mm）和提醒内容
- 每 30 秒检查一次，**只在设定到的那一分钟触发，错过不补发**
- 触发时在屏幕右下角弹出提醒窗

## 目录结构

```
build.cmd                  编译脚本（csc.exe）
src/desktop-host/          全部 C# 源码
  Program.cs               入口：单实例、DPI 感知、配置加载
  TrayContext.cs           托盘图标与菜单、后台刷新与巡检定时器
  DshSupervisor.cs         dsh web 进程托管（Job Object、自动重启）
  FloatingBall.cs          桌面悬浮球
  BalancePopup.cs          余额气泡（DeepSeek / Codex / 火山方舟）
  BalanceService.cs        DeepSeek 余额查询与缓存
  CodexQuotaService.cs     Codex 配额查询与缓存
  ArkPlanService.cs        火山方舟 Coding Plan 查询与缓存
  PricingCalendar.cs       峰/谷判定（2026 年节假日表）
  AlarmSchedule.cs         闹钟到点判定
  AlarmDialog.cs           闹钟设置窗
  AlarmPopup.cs            提醒弹窗
  SettingsForm.cs          配置窗
  Config.cs                config.json 读写、默认值、DPAPI 加密
  Probe.cs                 自动探测 node / dsh / codex / arkcli 路径
  Dpi.cs                   进程 DPI 感知与按显示器缩放
  AutoStart.cs             开机自启
  PortGuard.cs             端口占用检测
  IconFactory.cs / BrandGlyph.cs / IconFactory.cs  图标绘制
tests/desktop-host/        回归测试与验证脚本
design/desktop-pet/        视觉稿（未参与编译）
tools/                     图标路径生成等辅助脚本
```

## 已知限制

- 峰/谷表只覆盖 2026 年，其它年份不显示峰谷标识
- 端口被占用时不接管、不复用，直接拒绝启动
- 只允许运行一个托盘实例

## 许可

[Apache-2.0](LICENSE)
