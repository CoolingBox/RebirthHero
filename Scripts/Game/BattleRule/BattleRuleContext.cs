// 文件：BattleRuleContext.cs
// 《轮回大侠》**战斗规则层** —— 面板状态的一份只读视图（引用只读，方法可改账）
//
// 为什么规则方法不直接收十个参数：
//   一次掉落要同时碰修为池、银两、材料、装备四本账，外加随机源与两张配置表。
//   把它们平铺进参数表，调用点会变成一屏 `_` 开头的实参，review 时看不出谁是谁；
//   而少传一个（比如漏了 RarityBoost）编译器也不会报 —— 这是最容易埋下"某个乘区没生效"的形状。
//
// 这不是服务定位器：
//   · 字段全部由 `BattlePanel.Init` 末尾**显式**绑定一次，谁依赖什么一眼能查；
//   · 规则只**调用**这些引用上的方法，从不替换引用本身（所以面板重建状态机时要重新绑）；
//   · 规则文件一律不 `using Godot`，无头探针可以直接编译。
//
// 目录约定：`Scripts/Game/BattleRule/**` 只放"数值怎么算、状态怎么改"，不出现任何
// Control/Color/字体/音效。面板负责把这些结果翻成人话（飘字、结算栏文案）。

using System;
using System.Collections.Generic;

/// <summary>规则层可见的面板状态。字段可写 = 面板在 Init 时绑定；规则只读再用。</summary>
public sealed class BattleRuleContext
{
    // —— 进度状态机 ——
    public StageRun Run;              // 层 / 波 / 最高层 / 挂机开关
    public Cultivation Cult;          // 修为（=经验）：每人一份池子，升级要等结算
    public Silver Money;              // 银两：整队一份账户
    public MaterialBag Materials;     // 材料：按品阶分桶
    public EquipBag Equips;           // 装备实例（本世资产）
    public Dao Dao;                   // 道行：唯一跨世货币

    /// <summary>掉落随机源。与战斗随机分开，便于复现某一串掉落。</summary>
    public Random Rng;

    // —— 配置表（面板 Init 时从 ConfigManager 取好，规则不自己查表）——
    public List<EquipConfig> EquipPool;
    public List<EquipAffixConfig> AffixPool;

    // —— 查询委托 ——
    /// <summary>当前阵容的侠客 Id 列表（每场可能变，所以是委托不是快照）。</summary>
    public Func<List<int>> RosterIds;
    /// <summary>装备模板查表。</summary>
    public Func<int, EquipConfig> EquipById;
    /// <summary>套装查表。</summary>
    public Func<int, EquipSetConfig> SetById;

    /// <summary>状态机是否都已就位（读档/初始化中间态不要跑规则）。</summary>
    public bool Ready
    {
        get
        {
            if (Run == null)
            {
                return false;
            }
            if (Cult == null || Money == null)
            {
                return false;
            }
            if (Materials == null || Equips == null)
            {
                return false;
            }
            return true;
        }
    }
}
