// 文件：BattleConfig.cs
// 《轮回大侠》M1/M2 战斗引擎 —— 全局常量类
//
// ⚠️ 2026-10-16 起：**这里的数不再由本文件定义，而是由 `TuningConfig` 表注入。**
//   数据源：addons/TinyTools/ConfigGenerator/TuningConfig.xlsx → Resources/Config/TuningConfig.json
//   注入器：Scripts/Game/Config/Adapter/Tuning.cs（启动时调用 Tuning.Load，并做"表 ↔ 代码"双向一致性校验）
//
//   本类里的 `= 值` 是**兜底默认值**，只在表缺失/未加载时生效（保证引擎不会因为读不到表就归零）。
//   出货值一律以 **TuningConfig 表** 为准 —— 改数值改表，别改这里；若两者不一致，
//   Tuning.Load 会在控制台打印 "[Tuning] 兜底已更新: ..." 提醒同步。
//
//   为什么保留代码兜底而不是"只留表"：引擎大量热路径直接读字段（如 DealDamage 每次命中都读
//   ExtMinRatio / IntCurveK），走字典查找会拖慢；保留字段 = 零开销。而"唯一事实来源"靠
//   启动期注入 + 双向校验来保证，而不是靠删掉兜底值。

public static class BattleConfig
{
    // —— 时间 / 速度条 ——
    public static double GaugeMax = 100;     // 行动条满值
    public static double Limit = 120;        // 单场超时上限（秒），超时判负
    public static double DtCap = 0.5;        // 确定性 Run() 的最大步进（秒）
    public static int ChainMax = 8;          // 受击触发链最大深度（防递归失控）

    // —— 战斗疲劳（软狂暴，M0 第四刀引入） ——
    public static double FatigueStart = 30;  // 30s 后开始
    public static double FatigueRate = 0.03; // 每秒承伤 +3%

    // —— 重放封顶（框架 §9-1） ——
    public static double ReplayPMax = 0.9;   // 重放概率上限（防期望发散）
    public static int ReplayCMax = 25;       // 重放上限封顶

    // —— 全局攻防预算（2026-10-16 全部归位为 1；用户口径："atkscale,hpscale,defscale 都改回 1"） ——
    // 溯源：这三个是我在 M0/M1 **自己叠上去的拐杖**，从来不是文档或用户规定的值 ——
    //   · AtkScale 2.2  ← M0 第 1 轮我为压死局单方面加的攻预算（血预算没同步）
    //   · HpScale  1.7  ← 我反算"把 HP:ATK 校回《侠客与资质》基线 20:1"，用来抵消上面那个 2.2
    //   · DefScale 0.25 ← 我设的"配置设计刻度 → 伤害刻度"换算
    // 三者都对**玩家与敌方同时生效**（HeroBuilder / EnemyFactory / SampleBuilds 共用），
    // 因此它们从不是敌我平衡杠杆，只是"节奏旋钮"——改它们只改单场时长与面板数字。
    // 归 1 后引擎直接使用 HeroConfig / EnemyConfig 的设计刻度：配置表上的数字 = 屏幕上的数字，无隐藏乘数。
    //
    // ⚠️ 归 1 的两种性质完全不同，别混为一谈：
    //   · AtkScale / HpScale 归 1 = 拆掉两层拐杖，回到文档原始轴（HP:ATK = 1000:60 ≈ 16.7:1）。
    //     代价：此前 TTK 是靠"伤害被放大 2.2 倍"压到 ~44s 的，归 1 后伤害只剩 45%，TTK 会显著拉长。
    //     （实测：纯普攻镜像 43.6s → 70.9s；含武学镜像超时率 46.6% → 100%。）
    //   · DefScale 归 1 = **DEF 刻度 ×4**（7.5 → 30），把设计刻度直接当伤害刻度用：
    //     标准普攻 60 打标准 DEF 30 → 外功硬减 **50%**（原 ~13%）。实测：首次卡关层从 10 前移到 6。
    //     → 已按用户口径（2026-10-16："确保战斗时数值是多少就是多少，不要突然搞个谁都不知道的系数，
    //       不然玩家无从算数"）**把 0.25 折进内容表**：HeroConfig.BaseDEF / EnemyConfig.BaseDef 的值
    //       一律 ×0.25（由生成脚本在构建期完成），标准轴随之改为 **DEF 7.5 / 每级 +0.5**。
    //       于是「表里的 DEF 值 = 战斗里参与外功硬减/内功化解的值」，玩家可用卡面数字直接算伤害。
    public static double AtkScale = 1;
    public static double HpScale = 1;
    public static double DefScale = 1;

    // —— 受击权重基础（概率仇恨，M0.2 验证 1号位≈50%） ——
    // 2026-10-15：权重轴整体改为 10 基准（原 1.0 基准的 ×10；纯单位换算，比例不变，平衡不变）
    public static double Slot1W = 30;        // 1 号位基础权重（10 × 3.0 系数）
    public static double OtherW = 10;        // 2~4 号位基础权重
    public static double WFloor = 1.0;       // 权重下限（原 0.1 的 ×10；不能归零隐身）
    public static double Slot1HpBonus = 1.3; // 1 号位血量加成

    // —— DoT ——
    public static double PoisonTick = 2.0;    // 叠毒每 2s 一跳
    public static double PoisonPerStack = 0.2;// 每栈吃来源 EffAtk 的 20%
    public static int PoisonMaxStack = 5;
    public static double BurnTick = 1.0;      // 灼烧每 1s 一跳
    public static double BurnRate = 0.15;     // 吃来源 EffAtk 的 15%
    public static double BurnDur = 10;

    // —— 伤害类型减免（框架 §2） ——
    public static double ExtMinRatio = 0.15;  // 外功硬减下限：max(基础×15%, 基础−DEF)
    // 内功比例化解系数 Kd = 100（2026-10-15：400 → 110 → **100**，本次由用户直接指定）。
    //   规则历史："Kd ≈ 标准单次伤害"（ATK60 × AtkScale2.2 = 132）；本次用户明确取 **100**，
    //   即内功比该规则**更强一档**（Kd 越小、化解越高）。内功减伤率 = DEF/(DEF+Kd)，
    //   下限 ratio 0.25（最多减 75%）。与外功（硬减 DEF）的交叉点：**基础 = Kd + DEF**。
    //   ⚠️ 注：AtkScale 归 1 后标准单次普攻已从 132 变为 60，Kd=100 相对变"大"了一档（内功变弱）。
    public static double IntCurveK = 100;
    public static double IntMinRatio = 0.25;  // 内功化解下限

    // —— 侠客等级成长（1~40 级，框架 §11 v4：标准轴 + 直接加算）——
    // 【加算模型】 属性(L) = 基础值(L1) + 每级成长 × (L − 1)
    //   每级成长由 HeroConfig 的 HpGrowth / AtkGrowth / DefGrowth **直接给出绝对值**（三项各自独立随机，
    //   均不为 0），不再用"百分比偏差 × 标准增量"的间接表达——配置表上一眼能看出每级加多少血/攻/防。
    //   三属性独立 → HP:ATK 侧重比会随等级漂移（刻意性质，换成长多样性）。
    // 标准轴（**配置生成器 gen_hero_xlsx.py 用的是同一组值，改这里必须同步改那边**）：
    public static int HeroLevelMax = 40;
    public static double StdHp = 1000, StdHpGrowth = 100;    // 标准轴：HP 基准 1000，每级 +100
    public static double StdAtk = 60, StdAtkGrowth = 5;      // 标准轴：ATK 基准 60，每级 +5（v4.4：50 → 60）
    public static double StdDef = 7.5, StdDefGrowth = 0.5;   // 标准轴：DEF 基准 7.5（= 原口径 30 × 0.25 折表），每级 +0.5
    public static double StdSpd = 50;                        // 标准轴：SPD 50（速度占模的基准，见生成器）

    // —— 护盾衰减（2026-10-21 用户设定："护盾会在自身开始行动时减半"）——
    // 基准值不在这里：它是**侠客属性** BattleUnit.ShieldKeep 的字段默认值（0.5），
    // 由资质/装备/武学在装配期改写。本文件只放规则上限。
    // 上限 0.85 的 rationale：无论怎么堆，行动时至少要泄漏 15% —— 护盾的定位是"撑过一个
    // 行动周期的临时缓冲"，不是第二血条；若允许到 1.0，铁布衫（每 2 次受击 8% 气血）
    // 会重新变成永久墙，前面那条衰减规则就白定了。
    public static double ShieldKeepCap = 0.85;

    // —— 召唤物上限 ——
    // 携带召唤类武学的代价：每带 1 个，所属侠客最大生命 −本值（叠加；N 个 = N×本值）。
    // 锁死部分记入 BattleUnit.LockedHp，在血条 UI 上以银灰色显示。
    public static double SummonHpPenaltyPerSkill = 0.15;   // 每个召唤武学 −15% 最大生命（用户指定）
    // 2026-10-16 v5.7：4 → **8**（口径"全队召唤上限提高"）。
    //   理由：4 侠客 × 2 = 8，让"召唤流"（每人带一张以上召唤卡）真能同时铺开；
    //   同时它仍是**外层闸** —— 单张召唤卡还受类型档位（1/2/3/2）与武学覆盖约束，
    //   不会出现一张卡吃满全场（那个病在 v5.3/v5.4 已由"上限归属"修掉）。
    //   表现层配套：同类召唤物**堆叠显示**（一个类型一张卡），否则 8 个单位会把战场 UI 塞爆。
    public static int SummonCap = 8;
    public static double SummonTtl = 20;
    // 召唤物受"群体伤害"(Tgt=全体)减伤：2026-10-15 新增——防一发 AoE 清场（AoE 仍是召唤克星，只是不再秒杀）
    public static double SummonAoeDr = 0.5;
}
