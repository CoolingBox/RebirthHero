# 轮回大侠 / RebirthHero

武侠风**放置 / 增量**游戏：ATB 自动战斗为体，滚雪球式增量为骨，自走棋式乘区收集构筑为用。

- 引擎：**Godot 4.7 + C#（.NET 8）**
- 分辨率：1920×1080（`canvas_items` 拉伸，`expand` 适配）
- 渲染：Forward+ / d3d12（Windows 桌面）

---

## 快速开始

1. 安装 [Godot 4.7 .NET 版](https://godotengine.org/download/) 与 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
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
        ├── BattlePanel.cs    # 纯编排层
        ├── HeroCardLayer.cs / SummonStackLayer.cs / FloatLayer.cs / StatsPanelView.cs
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
- **静态 UI 进 `.tscn`**：位置 / 尺寸 / 锚点 / 字号 / 配色写在场景里，可在编辑器直接调；代码只管动态内容与事件接线
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
| M2.4 武学装配 | ⏳ |
| M2.5 装备 | ⏳ |
| M2.6 存档 | ⏳ |
