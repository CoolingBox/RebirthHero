# 轮回大侠 / RebirthHero

武侠风**放置 / 增量**游戏：ATB 自动战斗为体，滚雪球式增量为骨，自走棋式乘区收集构筑为用。

- 引擎：**Godot 4.7 + C#（.NET 10）**
- 分辨率：1920×1080（`canvas_items` 拉伸，`expand` 适配）
- 渲染：Forward+ / d3d12（Windows 桌面）

---

## 快速开始

1. 安装 [Godot 4.7 .NET 版](https://godotengine.org/download/) 与 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
2. 用 Godot 打开本目录（`project.godot`），首次导入会自动还原 NuGet 包
3. F5 运行（主场景 `Resources/Scene/Game.tscn`）

命令行构建：`dotnet build RebirthHero.csproj`

---

## 目录结构

```
Scripts/
├── TinyFramework/            # 基础框架（UIManager / ConfigManager / Save / Audio ...），不改
├── GameRoot.cs               # Autoload 入口
└── Game/
    ├── Battle/
    │   ├── Engine/           # 纯 C# 战斗引擎（无 Godot 依赖）
    │   └── StatsTracker.cs   # 战报统计（订阅事件总线，按侠客/武学聚合）
    ├── Config/
    │   ├── ConfigEnums.cs    # 配置枚举
    │   ├── Generate/         # xlsx 生成的强类型 POCO（勿手改）
    │   └── Adapter/          # 配置 → 引擎 的装配层
    ├── Stage/                # 推图：StageRun / EnemyFactory
    ├── Progression/          # 修为（经验）/ 银两（货币）
    ├── Materials/            # 材料：品阶目录 / 库存 / 掉落常数
    ├── Inn/                  # 客栈：资质 / 词条 / 编队
    └── UI/                   # 表现层（Godot Control）
        ├── BattlePanel.cs    # 纯编排层（战斗循环 / 事件 / 音效 / 打开子界面）
        ├── StagePanel.cs     # 选关（BasePanel，UIManager 开关）
        ├── SkillsPanel.cs    # 武学装配（同）
        ├── StatsPanel.cs     # 战报（同，常驻侧栏）
        ├── SettingPanel.cs   # 设置（同）
        ├── InnPanel.cs       # 客栈（同）
        ├── Theme/            # UIThemeManager / UISignalColors（主题装载与信号色）
        ├── HeroCardLayer.cs / SummonStackLayer.cs / FloatLayer.cs / BackdropLayer.cs
        └── Generate/         # UI 绑定（GenUICode.py 生成）
Resources/
├── Config/                   # 运行期 JSON 配置（由 xlsx 生成）
├── Scene/  UI/               # 场景与 UI 外壳（.tscn）
addons/TinyTools/             # 配置生成 / UI 代码生成（编辑器插件）
.codebuddy/
├── tools/                    # 开发脚本（GenConfig.py / GenUICode.py）
└── docs/                     # 设计文档（GDD / 数值框架 / 武学清单 / 装备 / 境界 ...）
```

---

## 核心系统

### 战斗
- ATB 行动条驱动：双方按速度蓄势 → 行动 → 触发武学
- 三类伤害：**外功**（硬减 DEF）/ **内功**（比例化解，Kd=100）/ **真伤**
- 武学由 27 列配置表定义：触发时机、条件、效果、目标、结算基数与数值、重放、召唤等
- 召唤 4 档（肉盾 / 输出 / 替身 / 飞剑），有同时存活上限；飞剑为 2 层计数体
- 召唤代价：每携带 1 个召唤武学，所属侠客 **最大生命 −15%**（血条银灰段显示锁死部分）

### 推图
- 一层 = N 波小怪 + 1 波收尾（普通层用精英，每 10 层用层主）
- 波间血量延续（只回 10%），阵亡者不复活；跨层整队重建
- 失利 → 退回上一层转入挂机，点「继续推图」再冲

### 经济
- **修为 = 经验**：每侠客各得一份，攒满自动升级，不留作货币
- **银两 = 货币**：整队一份，sink 在客栈刷新 / 招募

### 客栈
- 资质：★1~5 × 成长率 + 0~2 条词条
- 4 槽编队 + 候补区；1 号位享受 ×1.3 气血与更高受击权重

### 战报
- 实时聚合伤害 / 治疗 / 行动 / 承伤 / 触发
- 两种排列（按侠客 / 按武学）× 五种排序；每层清零
- 召唤物的伤害与承伤归入「召唤者 + 召唤来源武学」

### 材料与选关
- 材料 5 档（凡铁 → 精铁 → 玄铁 → 寒铁 → 天外玄铁），**每 10 层一档**；档位由层决定，高层不掉低档
- 每次击杀掉 1 个当前档（确定性，不赌概率）；打穿整层额外 +5
- **选关**：按章节列出已解锁层，点某层 → 锁层挂机；想继续冲层 → 关面板点「继续推图」
- 设计动因：掉落与敌方 HP 同为 `1.16^层` → 收益/秒与层数几乎无关，必须靠「分层材料」给"选低层"制造理由
- 数值均为 `[PLACEHOLDER]`，待 M2.5 装备 sink 接入后按"多少层够一次升阶"校准

### 装备（M2.5）
四栏（武器/护甲/饰品/靴子）× 五档稀有度（白绿蓝紫橙，复用 `Rarity`），击杀概率掉落。

- **属性口径**：装备的固定数值**加算**到英雄基础属性（与等级成长同一条线）；
  套装/词条的**百分比**才走"装备乘区"乘算（P1 未实现）
- **强化**：每级 +10% 基础属性（上限 10 级 = 2 倍），消耗银两，费用 `100×1.8^级`
  （用银两不用修为 —— 修为是经验、不留存，没有余额可消费）
- **掉落**：每次击杀 8%；先按层段掷稀有度（1~10 层白装为主，31+ 层才见橙装），
  再在同稀有度里按权重抽具体一件。两层随机的理由：稀有度是玩家感知最强的信号，必须能单独调
- **生效时机**：换装与强化在**下一场战斗**生效（与武学装配同口径），不允许战斗中换装绕战力门槛
- **转世**：全部归零（GDD §3.3 账本）
- 入口：顶栏「装备 ⚒」→ `EquipPanel`
- 待做：6 套套装（2/4 件参数修正）与随机词条

### 存档（M2.6）
- **落盘归框架**：`TinyFramework.SaveManager.Save(data)` / `Load<T>()` —— user:// 路径、JSON、字段序列化、**缺文件时返回 `new T()`**；文件名默认取 `typeof(T).Name`
- **游戏侧三块**（与 `SettingData` + `SettingManager` 同一范式）：
  - `Scripts/Game/Save/GameSave.cs` —— `SaveData` 子类，扁平 DTO，可人肉读改
  - `Scripts/Game/Save/GameProgress.cs` —— `Capture(...)` 抓成存档 / `Apply(...)` 灌回状态机；**不叫 Save/Load、不碰文件系统**
  - `Scripts/Game/Save/SaveBackup.cs` —— 框架管不到的部分：备份目录列举、改名、复制到任意路径、按时间排序（`SaveManager` 只有单文件 Save/Load，没有 List/Rename/Copy）
  - 各状态机只暴露窄的 `Restore(...)`，存档格式不会渗进引擎
- **四个功能**（顶栏「存档」→ `SavePanel`）：
  1. **保存**：立即把当前进度写进主档；可开关「每 10 分钟自动保存」（开关存 `SettingData.AutoSave`，间隔是 `SaveBackup.AutoSaveMinutes`）
  2. **备份**：默认名 `v版本号_年月日_时分`（版本号取 `project.godot` 的 `application/config/version`），可改
     - 桌面：弹文件对话框选路径，同时复制一份进存档夹（否则玩家选了路径却在载入列表里找不到）
     - **移动端：只能改名**，存进 `user://Backups/`（沙盒内不能浏览外部路径）
  3. **载入**：列表展示主档 + 全部备份（层、最高层、银两、时间），点「载入」覆盖当前进度并重开一场；备份可删，主档不可删
  4. **启动默认载入主档**：`BattlePanel.LoadProgress` 在首次显示时执行
- ⚠️ **判别"有没有存档"不能靠 `File.Exists`**：`SaveManager.Load<T>` 无文件时返回 `new T()`（全默认值），直接灌会把初始编队和已掷好的客栈栏位清空。判据是数据自带的 `GameSave.Written`（`Capture` 时置 true）
- **账本口径（GDD §3.3）**：
  - 跨世（重开本世不清）：武学装配
  - 本世（重开本世清空，关游戏不丢）：推图层/波/最高层、修为等级与池、银两、材料、装备实例与穿戴、客栈（栏位/已招/编队/刷新次数）
  - 设置单独一份（`SettingData`），与游戏进度互不影响
- **主档存盘时机**：每场战斗结算 / 关闭客栈·武学·装备·选关·存档面板 / 选层 / 重开本世 / 自动保存计时器 / 退出游戏（`TreeExiting` 兜底）
  - 刻意**不做**每次掉落就存：一只怪写一次盘既慢又无意义，崩溃最多损失一场
- **不存战斗过程**（引擎状态、波内血量、飘字）：存档粒度 = 一场战斗的结算点，读档从该层该波满血重打
- `Version` 用于将来迁移：读到不认识的版本整份丢弃（宁可从第 1 层重开，也不要读半份脏数据）
- 待做：多存档槽 UI 化（当前靠备份名区分）/ 云同步 / 迁移函数（当前只有 v1）

### 音频
- **总线**：`Master → BGM / SFX（近场干声 + Limiter）/ SFX_Far（低通 4k + 混响）/ SFX_UI（纯干声）/ Ambient`
- **BGM**：`Resources/Audio/BGM/battle_01~03.mp3`（国风战斗，可试听后换），循环由 `.import` 的 `loop=true` + 代码兜底双保险
- **空间定位**：`AudioStreamPlayer2D` × 16 + `AudioListener2D`（钉在可视区中心，随窗口比例更新）
- **音源分级**：点事件走快照（`PlaySFXAt`）；UI 音与全屏事件音居中（`PlayUISFX` / `PlayEventSFX`），永不空间化
- **并发控制**：同时活跃预算 10、同素材 40ms 去重、优先级抢占（暴击 > 命中 > 死亡 > 召唤 > 环境）
- **随机音调** ±6%（`0.94~1.06`）：同一素材的微差，不是变声

**音色依据**：全部按真实振动体的模态比合成，不做通用"噪声+过载"打击音（那是西方动作游戏的原型）：

| 音效 | 时长 | f0 | 振动体模态比 | 听感 |
|---|---|---|---|---|
| `hit_light` | 0.28s | 1150Hz | 钢梁 1:2.76:5.40:8.93 | 剑击「锵」 |
| `hit_heavy` | 0.32s | 520Hz | 青铜厚板 1:1.62:2.83:4.51 | 钝击「咚」 |
| `crit` | 0.40s | 980Hz | 剑模态 + 7Hz 拍频 + 3~9kHz 锋利层 | 破防 |
| `death` | 0.40s | 120→45Hz | 低频下滑 + 布料摩擦（无金属高频） | 倒地 |
| `spawn` | 0.50s | 400→1250Hz | 剑鸣上扫 | 飞剑出鞘 |
| `ui_click` | 0.10s | 420Hz | 木块 1:2.34:3.93 | 木鱼轻叩 |
| `ui_confirm` | 0.70s | 880Hz | 铜铃圆板 1:1.51:2.32:3.62:4.85 | 「叮——」 |
| `ui_open` / `ui_close` | 0.35/0.25s | 150/120Hz | 竹木 + 摩擦 | 木门开合 |
| `ui_upgrade` | 0.85s | C5→G5 | Karplus-Strong 拨弦（宫→徵） | 古筝 |
| `clear_layer` / `boss_down` | 2.2/2.8s | 180/110Hz | 铜锣密集非谐波 | 锣声「当——」 |

- **素材替换**：`Resources/Audio/SFX/` 语义化命名，替换同名文件即可换音，零代码改动
- ⚠️ 远场总线在当前 1920×1080 布局下**不会触发**（卡片离中心最远约 1100px，阈值 1650），保留给屏外音源 / 镜头缩放场景

### 顶部导航条（2026-10-20 重构）
战斗界面顶部原本是 **7 个按钮各自绝对定位**，而 `TTitle` 是全宽居中 Label —— 标题一长（带"本世最高 N 层 · 已战 M 场"）就被右侧按钮压住。

改成真正的导航条结构：

```
TNavBar（Panel · BgPanel，高 72，横向撑满，底色不透明）
└── TNavRow（HBoxContainer，四周留 16/10 边距，separation 10）
    ├── TTitle（Label · 左对齐，size_flags_horizontal = 3 吃掉剩余宽度）
    └── 客栈 / 装备 / 武学 / 选关 / 战报 / 存档 / 设置（7 个 Button，各 min 112×38）
TExp（战况行，独立一行 y 78~106，居中）
```

- **按钮进容器后不再有绝对坐标**：加/删/换顺序只改 `TNavRow` 的子节点，不会撞位置
- **标题与按钮物理隔离**：标题只占左侧剩余空间，且加了 `text_overrun_behavior = 3`（超长显示省略号），永远不会画到按钮上
- **TExp 下移到导航条下方**（78 > 72），和标题不再同排；卡牌从 y=140 起，三层不交叠
- 措辞同步调整：标题只留"当前在哪"（`《轮回大侠》· 第 N 层 W/T 波`），最高层/场次这类统计挪到战况行

⚠️ `TNavBar` 用 `BgPanel` 变体而不是 ColorRect：底色归 Theme，四套主题自动跟随。

### UI 主题（4 套武侠调性）
顶栏「设置 ⚙」→ 外观，点即生效并写盘（`SettingData.ThemeId`）。

| 主题 | 语境 | 底色 | 强调 | 次强调 |
|---|---|---|---|---|
| **水墨** | 书卷 / 留白 / 文人江湖 | 宣纸米白 `#E9E0CB` | 朱砂 `#A32D1C` | 赭石 `#8A5A38` |
| **朱漆** | 门派 / 官府 / 厚重 | 近黑朱 `#1A0906` | 鎏金 `#E4BD47` | 朱红 `#B23A29` |
| **青瓷** | 宋瓷 / 清冷 / 雅 | 墨绿灰 `#111A18` | 淡金 `#D9C68C` | 釉青 `#7FA99B` |
| **夜行**（默认） | 江湖夜雨 / 杀伐 | 墨蓝黑 `#090D12` | 金 `#CBA52A` | 血 `#A8322A` |

- 实现：`Scripts/Game/UI/Theme/`
  - `UIThemeManager.cs` —— 装载 `.tres`、切换、持久化、取色、`Changed` 广播
  - `UISignalColors.cs` —— 读盘信号色（血条/飘字/胜负），不进资源
  - `Scripts/Game/UI/BackdropLayer.cs` —— 全屏山水背景（底图 + 主题叠色 + 暗角）
- **字体四套共用**：霞鹜臻楷（标题）+ 霞鹜文楷（正文），OFL 可商用可分发。
  ⚠️ 不用系统自带的华文行楷 / 隶书：系统字体不能随游戏打包，换平台即失效，
  一缺整套主题就退化成默认无衬线。字体是"武侠基底"，主题差异由配色承担。
- **配色归属**：氛围色（背景 / 面板底 / 描边 / 文字 / 按钮 / 卡牌底 / 分隔线 / 输入框）由 `Theme` 统一提供；`.tscn` 只声明 `theme_type_variation`（TitleLabel / HeadingLabel / AccentLabel / BodyLabel / HintLabel），不再写 `font_color`
- **信号色不随主题变**：血条绿黄红、护盾蓝、伤害/治疗飘字、胜负色（`UISignalColors`）—— 这些是玩家读盘的信息色，换肤换掉会让"红=危险"的认知失效。叫"信号色"而非"语义色"：它的作用是向玩家发信号（危险/安全/收益），不是表达语义
- 字号、位置、尺寸、锚点仍在 `.tscn`（属于布局，不是皮肤）

**大数显示（统一走 `BigNum`）**

UI 上所有大数一律 `BigNum.From(x).ToString(2)`，**不要自己拼万/亿后缀**。

- 单位覆盖到 1载（1e44）：万 / 亿 / 兆 / 京 / 垓 / 秭 / 穰 / 沟 / 涧 / 正 / 载，再往后自动回退科学串（`9.9E60`）
- 默认显示模式在 `SettingData.NumberFormat` = `Chinese`（武侠语境不要 `15.0K` 这种写法）；`BigNum.DisplayMode` 由 `SettingManager` 在启动时写入，切换即全局生效
- ⚠️ 曾经存在的 `UINumberText.Fmt()` 已删除：它只到 1兆（1e12），之后会把整段数字（含 double 精度垃圾）铺出来，例如 3.3e30 显示成 `3300000000000000000兆`

**真值只有一处：`.tres`**

```
Resources/Theme/wuxia_*.tres  ←──(唯一真值，编辑器双击可视化改)
        │
        ├─ 控件样式：Label / Button / Panel / HSlider 的字体、颜色、StyleBox
        └─ 自定义类型 "Wuxia"：15 个氛围色 + 2 个背景下沉参数
                │
        UIThemeManager.GetTheme() / Color(key)
                │
        ├─→ ApplyTo(Control)  → 控件继承换肤
        └─→ Color("bg_base")  → 极少数仍需代码的场合（自定义 _Draw、动态 Material）
```

### 两个背景 variation

`.tres` 里定义两个 Panel type variation，**不要让任何节点再去当纯色背景**：

| variation | 色值来源 | 用于 | 出现在 |
|---|---|---|---|
| `BgFull` | `bg_base` | 主屏最底层背景 | BattlePanel / InnPanel 的 `TBg` |
| `BgPanel` | `panel_fill` | 子面板背景（**不透明，alpha = 1.0**） | SettingPanel / SkillsPanel / StagePanel / StatsPanel / EquipPanel / SavePanel 的 `TBg` |

⚠️ **子面板背景必须完全不透明**（2026-10-20 用户反馈："面板不要半透，被后方面板干扰严重"）。曾经试过 0.94 让战场透出 6%，观感上"层次更好"，但战斗界面的卡牌、飘字、战报侧栏会透到面板文字底下，读起来很吵。现在弹窗不铺山水图（图会裁切放大），底也不透 —— 靠 `panel_fill` 的实色 + 边框 + 暗角承担层次。

写法：`[node name="TBg" type="Panel" parent="."]` + `theme_type_variation = &"BgFull"`。

**为什么不是 ColorRect**：`ColorRect.color` 是节点属性，不吃 Theme 样式。以前靠 `ApplyThemeColors()` 里 `_TBg.Color = UIThemeManager.BgBase` 逐处喂，于是编辑器预览是一块刺眼的白，和运行时完全对不上。改成 Panel + variation 之后，编辑器与运行时走同一条 Theme 路径，那几行赋值全部删除。

⚠️ 新增背景节点一律用 Panel + variation。若确实要用 ColorRect（卡牌/堆叠卡这类运行时动态生成的），记住它拿不到 Theme，颜色必须由代码喂——但那是动态内容，不参与编辑器预览。

- 4 份资源：`wuxia_ink.tres` / `wuxia_lacquer.tres` / `wuxia_celadon.tres` / `wuxia_nightblade.tres`
- **代码里没有任何色值**。改配色 = 改 `.tres`，不需要重新编译 C#
- **编辑器预览**：`project.godot` 的 `[gui] theme/custom` 指向 `wuxia_nightblade.tres`（与 `SettingData.ThemeId=3` 一致），
  使 `.tscn` 在编辑器里就直接显示武侠字体与控件配色，不再是引擎默认外观。
  它只作用于项目场景里实例化的 Control —— **编辑器自身的菜单 / Inspector 不受影响**（那由 `editor_settings` 控制）。
  运行时也不受影响：`UIThemeManager.ApplyTo()` 仍按存档把根节点 Theme 换成玩家选的那套，这里只是"无显式 Theme 时"的兜底。
  ⚠️ `project.godot` 里**不要写注释**：Godot 的 CFG 解析器会把注释文本粘连成下一个 key 的前缀，
  导致 `theme/custom` 变成空值而注释被吞进 key 名（实测踩过，表现为设置完全不生效）。
  想在配置里留说明就写在 README，Godot 每次保存 `project.godot` 也会抹掉注释
- 色板键（`Wuxia/colors/*`）：`bg_base` `panel_fill` `panel_border` `card_ally` `card_enemy` `text_primary` `text_secondary` `text_muted` `accent` `accent_alt` `button_fill` `button_hover` `button_pressed` `separator` `input_fill` `backdrop_tint` `backdrop_shade`
- 为什么色板也塞进 `.tres`：背景/卡牌底色若留在 C# 里就会出现"样式在资源、背景色在代码"两份真值。现在背景走 variation拿到了 Theme，只剩卡牌这类**运行时动态生成**的 ColorRect 还需要从 `.tres` 取色
- ⚠️ 读不到资源时返回**洋红**兜底色并打印错误 —— 宁可刺眼，也不要静默白屏

**背景下沉（BackdropLayer）**

- 三层结构：山水底图 → 主题叠色（`backdrop_tint`）→ 径向暗角（`backdrop_shade`）
- 底图：`Resources/UI/Background/bg_ink_light.png`（水墨主题）/ `bg_night_dark.png`（其余三套共用，靠叠色区分冷暖）
- 底图由生图模型生成，右下角 AI 水印已用"上方镜像 + 羽化"修补
- 为什么不各生成四张：三套暗色主题差异主要靠叠色即可达成，一张底图也便于后期统一换图

**配色三条硬规矩**（改色时必须守住）

1. 底色不用纯黑纯白 —— 宣纸是暖的、漆器是带红的黑、青瓷是压低饱和的青
2. 文字不用纯黑纯白 —— 纯黑在米白上发跳，纯白在朱底上发青
3. 描边承担"形"，强调色承担"神" —— 两者混用会满屏都是重点

**可读性校验**（WCAG 2.1 相对亮度，四套全部达标）

| 组合 | 实测最低 | 要求 |
|---|---|---|
| 主文字 / 面板底 | 12.77:1 | AAA 7:1 |
| 次要文字 / 面板底 | 6.11:1 | AA 4.5:1 |
| 弱化文字 / 面板底 | 3.57:1 | 3:1 |
| 强调色 / 面板底 | 6.06:1 | 3:1 |

---

## 配置管线

```
addons/TinyTools/ConfigGenerator/*.xlsx
        │
        ├─ (编辑器按钮 / GenConfig.py) ──→ Resources/Config/*.json        # 运行期数据
        └─ (编辑器按钮)                ──→ Scripts/Game/Config/Generate/*.cs  # 强类型 POCO
```

- 表结构：第 1 行字段名 / 第 2 行类型 / 第 3 行注释 / 第 4 行图例 / 第 5 行起数据
- **改数值改表**，不要在代码里加系数或乘数
- 全局系数集中在 `TuningConfig.xlsx`（带 Group / Source / Note，可审计），启动时由 `Tuning` 反射注入并做表↔代码双向校验

---

## 开发工具

| 脚本 | 用途 | 运行位置 |
|------|------|---------|
| `.codebuddy/tools/GenConfig.py` | xlsx → `Resources/Config/*.json` | 工程根目录 |
| `.codebuddy/tools/GenUICode.py` | `.tscn` → `Scripts/Game/UI/Generate/*Bind.cs` | 任意目录 |

依赖 `openpyxl`（`pip install openpyxl`）。

---

## 无头探针（数值验证）

在 Godot 工程**之外**的独立 .NET 工程里直接编译引擎与配置层源码，跑大样本战斗量化 TTK / 技能强度：

- **hero_probe**：各层 × 大样本胜率、武学强度隔离基准
- **summon_hp_test**：召唤武学生命代价回归

> 放在工程外是为了避免与 Godot 工程的入口点冲突。数值调参一律先跑探针，再把结论折进内容表。

---

## 工程约定

- **显示与逻辑分离**：引擎纯 C#、不 `using Godot`；表现层只读数据渲染，经 `BattleEventBus` 解耦
- **表现层按组件拆分**：面板膨胀时拆成独立 Godot 节点组件（`Control` 子类脚本 + `.tscn` 挂载），不用 partial 把一个类摊成多文件
- **子界面一律做成 `BasePanel`**：`XxxPanel.tscn`（根节点不挂脚本）+ `XxxPanel.cs`（BasePanel 子类）+ `Generate/XxxPanelBind.cs`（`_T*` 字段）。由 `UIManager.ShowPanel<T>()` 开关，依赖用静态 `BindData(...)` 注入。**不要写成 `XxxView` 塞进父场景**——那样 BattlePanel 会越养越胖，且子界面无法独立复用
  - 常驻型（战报）用 `ShowPanel`/`HidePanel` 切换（不销毁、复用实例），模态型用 `ClosePanel`（销毁重建）
  - 静态入口（如 `RefreshCurrent`）必须判 `GameObject` 是否 `IsInstanceValid` —— `ClosePanel` 会 `QueueFree`，静态引用不清就会在下一次调用抛 `ObjectDisposedException`
- **静态 UI 进 `.tscn`**：位置 / 尺寸 / 锚点 / 字号写在场景里，可在编辑器直接调；代码只管动态内容与事件接线
- **配色进 `Theme`（不是 `.tscn`）**：`theme_override_*` 优先级高于 Theme，写死在场景里会让换肤失效。层级用 `theme_type_variation` 声明，颜色由 `UIThemeManager` 提供；信号色（血条/飘字/胜负）集中在 `UISignalColors`
- **命名**：游戏脚本不加 namespace；仅 `TinyFramework` 保留
- 未经 playtest 的数值一律视为待验证

---

## 进度

| 里程碑 | 状态 |
|--------|------|
| M0 战斗模拟器 | ✅ |
| M1 Godot 原型 | ✅ |
| M2.1 推图结构 | ✅ |
| M2.2 修为经济 | ✅ |
| M2.3 客栈 | ✅ |
| M2.4 武学装配 | ✅ |
| M2.5 装备 | ✅ 最小实现（套装/词条待做） |
| M2.6 存档 | ✅ 单槽自动存 |
