# DshTray

Windows 桌面余额悬浮球。右下角一个小球，左键点开就能看到 DeepSeek 余额、Codex 配额和火山方舟 Coding Plan 剩余额度，不用再开网页或终端。

- 运行时只依赖系统自带的 .NET Framework 4.0，无需安装任何运行时
- 常驻内存小、几乎不占 CPU（无轮询渲染，界面按需重绘）
- 单文件 exe，拷到哪都能跑，配置就放在 exe 旁边
- 不托管、不占用任何端口，也不依赖 dsh 本体

## 功能

- **桌面悬浮球**：可拖动、位置持久化，左键查余额、右键出菜单
- **余额气泡**：DeepSeek 余额（含峰/谷）、Codex 配额、火山方舟 Coding Plan
- **后台刷新**：每 60 秒刷新一次余额数据，点击时优先用缓存，秒开
- **开机自启**：写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下的 `DshTray` 项
- **闹钟**：多条目定时提醒
- **单实例**：重复双击不会起第二个实例，只会弹出提示气泡
- **高 DPI 适配**：按显示器逐个感知 DPI，多屏混合缩放不会糊

## 环境要求

- Windows（自带 .NET Framework 4.0+ 即可）
- 可选 **Node.js**：仅 Codex 配额需要，`node.exe` 从 `PATH` 里自动查找
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

- 找不到 `config.json` 时会弹出配置窗口，填好保存后开始运行
- 一般只需要填 DeepSeek API Key（也可以留空，见下文「API Key 来源」）

`config.json` 与 `DshTray.exe` 必须在同一目录。

## 配置项

| 键 | 含义 | 默认值 |
| --- | --- | --- |
| `AutoStart` | 开机自动启动本程序 | `true` |
| `ShowBall` | 显示桌面悬浮球 | `true` |
| `BallX` / `BallY` | 悬浮球坐标，拖动后自动保存 | `0` / `0`（左上角，启动时会自动挪进工作区） |
| `ShowCodexQuota` | 在余额气泡里显示 Codex 配额 | `true` |
| `ApiKey` | DeepSeek API Key | 空 |
| `Alarms` | 闹钟列表，每项 `{ Enabled, Time, Message }` | 空列表 |

`ApiKey` 保存时会用 Windows DPAPI（当前用户）加密，以 `dpapi:` 前缀写回 `config.json`。

## 托盘菜单

| 项 | 说明 |
| --- | --- |
| 开机启动 | 勾选即写入当前用户的启动项 |
| 桌面悬浮球 | 勾选即显示/隐藏悬浮球，设置会被保存 |
| 闹钟 | 打开闹钟设置（添加/编辑/删除，勾选启用） |
| 重新登录火山方舟 | 打开 `arkcli auth login volc-sso` 走一遍登录，完成后自动重查配额 |
| 设置 | 改 API Key、自启与 Codex 显示等，保存后重建悬浮球并重新查询 |
| 退出 | 结束程序 |

## 悬浮球

- **左键拖动**：移动位置，松手后坐标写入配置；拖到屏幕外会自动拉回工作区
- **左键单击**：弹出余额气泡
- **右键**：弹出托盘菜单（这里**不含**「退出」，避免误触）

小球与托盘图标统一使用品牌蓝 `#4D6BFE`。

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

后台每 60 秒刷新一次三路数据，缓存有效期 2 分钟。点击悬浮球时优先直接显示缓存，秒开；没有缓存才发起实时查询。

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
  TrayContext.cs           托盘图标与菜单、后台刷新定时器
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
  Config.cs                config.json 读写、DPAPI 加密
  Probe.cs                 自动探测 node / codex / arkcli 路径
  Dpi.cs                   进程 DPI 感知与按显示器缩放
  AutoStart.cs             开机自启
  IconFactory.cs           图标绘制
  BrandGlyph.cs            品牌图标路径
tests/desktop-host/        回归测试与验证脚本
design/desktop-pet/        视觉稿（未参与编译）
tools/                     图标路径生成等辅助脚本
```

## 已知限制

- 峰/谷表只覆盖 2026 年，其它年份不显示峰谷标识
- 只允许运行一个实例

## 许可

[Apache-2.0](LICENSE)
