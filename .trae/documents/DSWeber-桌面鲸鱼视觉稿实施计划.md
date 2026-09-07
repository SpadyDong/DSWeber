# DSWeber 桌面鲸鱼挂件 — 高保真视觉稿实施计划

## 一、概要

DSWeber 是一款 Windows 桌面悬浮宠物应用：桌面最上层常驻一只鲸鱼，点击展示 DeepSeek 余额与当前计费时段（"峰"/"谷"），悬停唤出 Chat、控制台、充值、设置四个入口。

本次交付**高保真视觉稿**（静态 HTML 页面，非可运行 Electron 应用），产出一个 `.design` canvas 项目，包含 5 个页面节点与 2 个图片节点，用纯 CSS 完整复刻桌面悬浮宠物的全部视觉状态与交互质感。

视觉方向：**深色深海玻璃拟态** —— 深蓝黑背景 + 豆包蓝 `#0065fd` 为主调，玻璃折射面板悬浮于模拟的 Windows 桌面壁纸之上。

## 二、当前状态分析

- 工作区 `d:\WorkBuddy\DSWeber` 为空，属全新项目（CREATE 模式）。
- 参考设计系统：豆包设计库（read-only），位于 `c:\Users\89887\.trae-cn\design_libraries\Doubao`，含 55 个 CSS 变量、6 个组件规格、40 个 Lucide 风格 SVG 图标。
- 豆包库为 library-bound 约束源，CSS 引用其 `colors_and_type.css`，本地不复制副本以避免 token 漂移。
- 豆包库 `.dark` 作用域关键 token（本项目主场）：`--background: #0e1115`、`--popover: #22252a`、`--primary: #0065fd`、`--border: #333942`、`--muted-foreground: #afbbcb`、`--accent: #00266b`、`--accent-foreground: #bfdbfe`、`--radius: 1.2rem`、`--spacing: 0.24rem`、`--font-mono: JetBrains Mono`。
- 豆包库字体 `Stack Sans Text` / `Source Serif 4` 均不含中文字形，需外部引入中文展示字体。

### 已核实的外部事实

我直接核对了 DeepSeek 官方定价文档（中英文两版），确认：

- **峰谷规则**：峰时为 UTC `01:00-04:00` 与 `06:00-10:00`，即北京时间 `09:00-12:00` 与 `14:00-18:00`；其余时段全部为谷时。谷时价格为峰时的一半。**官方文档现行版本无"周一至周五"限定**，规则每天生效，周末同样按时段判定。
- **余额接口**：`GET https://api.deepseek.com/user/balance`，`Authorization: Bearer <TOKEN>`。返回 `is_available` 与 `balance_infos[]`，字段含 `currency`、`total_balance`、`granted_balance`、`topped_up_balance`。本次视觉稿据此字段结构设计余额展示。

## 三、拟实施变更

### 3.1 项目结构

```
d:\WorkBuddy\DSWeber\design\desktop-pet\
├── DSWeber-桌面挂件.design       # canvas 入口
├── generation-tree.json
├── partials\
│   ├── desktop-stage.html        # 桌面壁纸舞台 + 任务栏（全页共享）
│   ├── whale-dock.html           # 鲸鱼挂件本体 + 呼吸浮动动效
│   └── glass-panel.html          # 深海玻璃面板基座
├── pages\
│   ├── pet-idle.html
│   ├── pet-hover-menu.html
│   ├── pet-balance-peak.html
│   ├── pet-balance-offpeak.html
│   └── pet-settings.html
└── assets\
    ├── whale-mascot.png          # 透明底鲸鱼插画
    └── desktop-wallpaper.png     # 深海渐层桌面壁纸
```

结构决策：

- **不创建本地 `colors_and_type.css`**：library-bound 模式下 CSS 源指向豆包库绝对路径，由 head 契约脚本内联进 `<style id="theme-vars">`。
- **`partials/` 先于 `pages/` 生成**：五个页面共享同一套桌面舞台与鲸鱼挂件，抽成共享片段可避免五页的鲸鱼位置、投影参数、壁纸各自漂移。
- **`.design.config`**：`autoLayout: true`、`deviceType: "desktop"`、`projectName: "DSWeber 桌面挂件"`、`designLibrary` 镜像豆包库 identity。所有页面 `canvasData.group` 统一为 `0`。

### 3.2 共享片段设计

**`partials/desktop-stage.html`** — 三层舞台，是"骗过眼睛"的关键：

1. 底层：`desktop-wallpaper.png` 铺满（`background-size: cover`），深蓝黑深海渐层，与 `--background: #0e1115` 同调，为玻璃面板的折射色提供可信来源。
2. 中层：模拟桌面语境元素 —— 左上角两三个半透明桌面图标轮廓、底部一条任务栏。透明度控制在 8–14%，仅提供"这是桌面而非网页"的线索，不抢焦点。
3. 顶层：鲸鱼挂件及其弹出物，定位于屏幕右下偏上（贴近真实桌宠习惯位置）。

`<html>` 静态硬编码 `class="dark"`，无 JS 主题切换。

**`partials/whale-dock.html`** — 鲸鱼容器三层视觉：`whale-mascot.png` 本体、身下椭圆柔光投影（`filter: blur()` + `--primary` 低透明度）、外围极淡水波光晕。持续动效 `whale-float` 上下 6px 缓动循环（4.5s）+ 投影同步缩放，包裹在 `@media (prefers-reduced-motion: reduce)` 保护中。

**`partials/glass-panel.html`** — 玻璃面板基座：`backdrop-filter: blur() saturate()` + 1px 内描边（`--border`）+ 双层投影。豆包库本身是"边框主导、弱投影"的技术冷调，深海玻璃拟态是在此基础上的暗场增强。

### 3.3 页面清单

| 页面 | 节点 ID | 设计内容 |
|---|---|---|
| 待机态 | `page-pet-idle` | 只有鲸鱼在桌面呼吸浮动。全页最干净一帧，建立"平时不打扰你"的印象。鲸鱼是唯一焦点，周围大量壁纸负空间。 |
| 悬停菜单态 | `page-pet-hover-menu` | 四个按钮（Chat / 控制台 / 充值 / 设置）以**环形弧线**围绕鲸鱼上半侧展开，而非竖排列表 —— 竖列菜单是最平庸解法，弧形环绕符合"生物身边浮起气泡"的隐喻。每个按钮为独立玻璃胶囊 + 豆包库图标，逐个 stagger 入场（40ms 递增）。 |
| 余额态·峰时 | `page-pet-balance-peak` | 鲸鱼上方浮出主玻璃卡。内容：① 总余额 `¥38.62`（`--font-mono` JetBrains Mono，全页最大字号，`whitespace-nowrap`）；② "峰"字徽章 —— 水滴形玻璃章，内含大号"峰"字，用 `--primary` 系高亮，配一行小字说明当前计费口径；③ 一行辅助信息"更新于 3 分钟前"。此页硬编码固定为峰时呈现。 |
| 余额态·谷时 | `page-pet-balance-offpeak` | 与上页同构，"谷"字徽章改用降饱和静谧色（`--muted-foreground` / `--accent` 系），整卡光效收敛，小字说明改为"谷时计费，单价为峰时一半"。此页硬编码固定为谷时呈现。峰/谷两页同 group 并列，形成可直接对比的一组。 |
| 设置面板 | `page-pet-settings` | 尺寸更大的玻璃面板，三个配置区：① API Key 输入区 —— 遮罩显示 `sk-••••••••••••3f2a`、右侧眼睛切换图标、下方绿色连接状态指示点 + "已连接"文案；② "开机自启"开关（默认开）；③ "余额刷新间隔"分段选择器（5 / 15 / 30 分钟，默认 15 分钟）。底部"保存 / 取消"信息-操作双列布局，操作侧字号明显小于面板主标题，圆角用 `rounded-lg`。 |

### 3.4 图片资产方案

**`assets/whale-mascot.png`** — 透明背景插画风格鲸鱼。Prompt 方向：圆润友好的鲸鱼吉祥物，侧四分之三视角，插画/矢量渐层风格；配色锁定深蓝黑基底 + 品牌蓝 `#0065fd` 高光，腹部浅色；柔和边缘光模拟屏幕反光；透明背景、居中构图、边缘留白以便叠加投影；`no text, no typography, no letters, no words`。

**`assets/desktop-wallpaper.png`** — 深海渐层桌面壁纸底图，建立桌面语境。深蓝黑渐层 + 微弱水下光柱，低对比、无焦点，不与前景挂件竞争。

生成纪律：图像生成由主流程负责，页面生成子任务不得自行调用生图工具；生图返回成功即接受，不做画质复检、不因水印担忧重生成。两张图生成后各自在 `.design` 的 `data` 数组追加 `type: "image"` 节点（`image-001` 鲸鱼吉祥物、`image-002` 深海桌面壁纸），`canvasData` 不含 `group` 字段。

若透明底 PNG 无法产出，停止并回报 —— 不降级为方形底图，也不用 DOM/SVG 绘制假鲸鱼替代。

### 3.5 中文字体

引入 **Noto Serif SC**（Google Fonts CDN），作为中文展示字体，让"峰""谷"两个大字具备笔画质感。字体栈：

```css
--font-display-cn: "Noto Serif SC", "Source Serif 4", serif;
```

拉丁文与数字仍走豆包库的 `--font-sans` / `--font-mono`，中文标题与"峰/谷"徽章走 `--font-display-cn`。

### 3.6 交互模拟方案

**页内 —— 纯 CSS：**

| 行为 | 实现 | 要点 |
|---|---|---|
| 鲸鱼常驻浮动 | `@keyframes whale-float` + 投影同步缩放 | 缓动用 `cubic-bezier` 而非 `linear`，避免机械感 |
| 悬停唤出菜单 | `.whale-dock:hover .orbit-btn` 改 `opacity` / `transform`，配 `transition-delay` 实现 stagger | 菜单始终存在于 DOM，只改视觉属性，从不增删节点 |
| 按钮悬停反馈 | `transform: translateY(-2px)` + 光晕增强 + 图标微旋 | 纯 CSS，不引入动画库 |
| 点击态 | `:active` 压缩 + 内投影 | 提供物理触感 |
| 面板入场 | `@keyframes panel-emerge`（scale 0.94→1 + blur 淡入 + Y 轴微升） | 玻璃面板从鲸鱼身上"浮出水面"，方向感朝上 |

用 CSS `:hover` 而非 JS 状态机：悬停展开是真实产品的原生行为，`:hover` 是它最忠实的表达，同时避开 DOM 结构变更。

**跨页 —— `.design` interactions 连线：**

```
page-pet-idle         --[鲸鱼被唤醒]-->    page-pet-hover-menu
page-pet-hover-menu   --[查看余额]-->      page-pet-balance-peak
page-pet-hover-menu   --[打开设置]-->      page-pet-settings
page-pet-balance-peak --[切换到谷时]-->    page-pet-balance-offpeak
page-pet-settings     --[返回桌面]-->      page-pet-idle          (hideEdge: true)
page-pet-balance-*    --[收起]-->          page-pet-idle          (hideEdge: true)
```

返回类连线带 `hideEdge: true`，画布只呈现正向主流程。连线对应元素带 `data-dom-id` 属性（写入即为运行时关键字段，禁止后续改动）。

**外部链接**（不登记 interactions，写成真实 `<a target="_blank">`）：

- Chat → `https://chat.deepseek.com/`
- 控制台 → `https://platform.deepseek.com/`
- 充值 → `https://platform.deepseek.com/top_up`

### 3.7 峰谷判断逻辑

**规则**：峰时 = 北京时间 `09:00-12:00` 或 `14:00-18:00`（左闭右开，即 12:00:00 与 18:00:00 整点归谷时），每天生效，无工作日限定。谷时价格为峰时一半。

**实现 —— DOM 双态静态渲染 + class 切换**：峰、谷两个徽章节点在 HTML 中同时静态存在，JS 只在容器上切换 `is-peak` / `is-offpeak` class，由 CSS 决定谁可见。完全不触碰 DOM 树结构；即使 JS 被禁用，静态默认 class 也保证有一态可见，不出空白。

**时区换算**：不依赖运行机器本地时区（计费口径固定为北京时间），统一走 UTC 取值再偏移 +8：

```js
// 以 UTC 为基准换算北京时间，避免依赖本机时区
const bj = new Date(Date.now() + 8 * 60 * 60 * 1000);
const hour = bj.getUTCHours();
const isPeak = (hour >= 9 && hour < 12) || (hour >= 14 && hour < 18);
```

**画布截图稳定性**：`pet-balance-peak` 与 `pet-balance-offpeak` 两页各自硬编码钉死状态（分别静态写入 `is-peak` / `is-offpeak`），不挂自动判断脚本 —— 否则夜间截图时两页都显示"谷"，对比组失效。真实判断逻辑以注释形式随附在页面内，标明生产环境接入位置。

## 四、假设与决策记录

| 项 | 决策 | 来源 |
|---|---|---|
| 交付形态 | 高保真视觉稿（静态 HTML），非 Electron 应用 | 用户确认 |
| 峰谷规则 | 北京 09:00-12:00 / 14:00-18:00 为峰，无工作日限定 | 官方文档核实 + 用户确认"现行规则" |
| 整点归属 | 左闭右开，12:00 与 18:00 归谷时 | 计划决策 |
| 鲸鱼形象 | AI 生成插画风格透明底 PNG | 用户确认 |
| 视觉基调 | 深色深海玻璃拟态 | 用户确认 |
| 余额示例 | `¥38.62`，只显示总余额 | 用户确认 |
| 中文字体 | 引入 Noto Serif SC | 用户确认 |
| 设置项 | API Key + 开机自启 + 余额刷新间隔 | 用户确认 |
| 充值 URL | `https://platform.deepseek.com/top_up` | 用户确认 |
| 页面数量 | 5 个状态页，不做状态总览页 | 计划决策（总览页易滑向"设计说明书"而非产品页） |
| 项目目录 | `design/desktop-pet` | 仓库分层决策 |

## 五、执行顺序

1. 提取豆包库上下文一次：读 `colors_and_type.css`、`css.json`、`components/index.json`、相关 `preview/*.html`、`ui_kits/dashboard/index.html`，蒸馏为上下文缓存（`brandPrefix: "doubao"`、真实 token 名清单、可用组件清单）。之后只用缓存。
2. 创建目录结构，写 `.design` 骨架（一次性写入全部 5 个页面节点 + 完整 `config`，含 `designLibrary` identity）。
3. 生成两张图片资产，随后立即在 `.design` 追加 image 节点，核对节点总数（5 页 + 2 图 = 7）与 ID 唯一性。
4. 写 `generation-tree.json`，生成三个 `partials/*.html` 共享片段。
5. 并行派发页面子任务，仅生成 `pages/*.html`。子任务不读写 `.design`、不跑校验脚本、不调生图工具。
6. 登记 `interactions`，应用 HTML head 契约。
7. 执行 `validate-design-workspace.mjs` 全量校验。

## 六、验证步骤

1. **校验脚本通过**：`validate-design-workspace.mjs` 零错误。
2. **Token 纯度**：全项目搜索硬编码色值（`#` 开头的十六进制，除图片路径外）与 Tailwind 调色板类（`bg-blue-*`、`text-slate-*` 等），应为零命中；所有颜色走 `var(--*)`。
3. **节点完整性**：`.design` 中 5 个 page 节点 + 2 个 image 节点，ID 无重复，`assets/` 下每个文件都有对应 image 节点。
4. **悬浮感自检**：逐页确认呈现的是"桌面壁纸 + 悬浮挂件"，而非居中的网页卡片；挂件无页面边框轮廓，投影落在壁纸上。
5. **峰谷双态**：`pet-balance-peak` 与 `pet-balance-offpeak` 分别稳定显示"峰"/"谷"，不随打开时间变化。
6. **外链正确性**：三个外部按钮的 `href` 与 `target="_blank"` 逐一核对。
7. **动效降级**：开启系统"减少动态效果"后，浮动与入场动画停止，布局不塌。
8. **首屏兜底**：关键布局有静态 `critical-layout` CSS，不依赖 Tailwind 运行时编译。

## 七、风险与停止条件

- 透明底 PNG 无法产出 → 停止回报，不降级为方形底图或 SVG 假鲸鱼。
- 豆包库缺少所需图标（尤其"设置"齿轮，库内 40 个图标中未见 `settings`/`gear`）→ 按图标优先级回落 Lucide 官方 SVG，并在交付时说明。
- library-bound 模式下 token 编造、组件结构编造、Library identity 不匹配均为阻断级错误，不降级为警告。
