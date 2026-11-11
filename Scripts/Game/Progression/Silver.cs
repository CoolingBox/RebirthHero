// 文件：Silver.cs
// 《轮回大侠》M2.2 银两 —— 货币账户（**击杀即时掉落**）
//
// 用户口径（2026-10-15）："击杀就可立即获得经验资源" + "敌方掉落的是修为(就是经验)和银两"。
//   每击杀一个敌人，银两立刻进账（整队共享一份）；
//   用途 = GDD §4.2 的 sink：客栈招募 / 武学商店 / 装备强化 / 境界突破。
//
// 2026-11-06：余额与累计改 **BigNum**（尾数+指数）。
//   产出是 1.16^层 的指数，跑上几百层后余额会越过 double 的 1e308 —— 那时 double 会变成
//   Infinity，存档写出去是 "Infinity"、读回来是 NaN，玩家会看到"银两凭空消失"。
//   BigNum 用 (尾数, 指数) 表示，能稳定到 1e(1e308)。

using System;
using TinyFramework;

public sealed class Silver
{
    /// <summary>当前银两余额。</summary>
    public BigNum Amount { get; private set; } = BigNum.Zero;

    /// <summary>本世累计获取（含已花掉的）。</summary>
    public BigNum TotalGained { get; private set; } = BigNum.Zero;

    /// <summary>本场累计掉落（战斗中实时显示，战斗开始时清零）。</summary>
    public BigNum BattleGained { get; private set; } = BigNum.Zero;

    /// <summary>战斗开始：清空"本场掉落"计数。</summary>
    public void BeginBattle()
    {
        BattleGained = BigNum.Zero;
    }

    /// <summary>某个敌人被击杀时调用：按层进账一份银两。</summary>
    public BigNum GainForKill(int layer)
    {
        return GainForKill(layer, 1.0);
    }

    /// <summary>
    /// 带产出的击杀结算（2026-11-02）：mul = 天赋「经营」之类的银两产出乘区。
    /// 为什么把这个乘区放在这里而不是改 `CultivationConfig.SilverPerKill`：
    /// 那个是**全局**公式，敌方掉落也走它；而天赋是玩家独有的加成，只能在玩家侧乘。
    /// </summary>
    public BigNum GainForKill(int layer, double mul)
    {
        if (mul <= 0)
        {
            mul = 1.0;
        }

        BigNum gain = CultivationConfig.SilverPerKill(layer) * mul;
        Amount += gain;
        TotalGained += gain;
        BattleGained += gain;
        return gain;
    }

    /// <summary>消费（客栈 / 装备强化 / 境界突破；不足则不扣并返回 false）。</summary>
    public bool TrySpend(BigNum amount)
    {
        if (amount <= BigNum.Zero || Amount < amount)
        {
            return false;
        }
        Amount -= amount;
        return true;
    }

    /// <summary>
    /// 直接进账（**非击杀来源**：装备回收等）。
    /// 不计入 BattleGained —— 那个字段表示"本场战斗掉落"，回收不是战斗产出。
    /// </summary>
    public void Add(BigNum amount)
    {
        if (amount <= BigNum.Zero)
        {
            return;
        }
        Amount += amount;
        TotalGained += amount;
    }

    /// <summary>读档：写回余额与本世累计（累计只用于展示，非正值夹成 0）。</summary>
    public void Restore(BigNum amount, BigNum total)
    {
        Amount = amount > BigNum.Zero ? amount : BigNum.Zero;
        TotalGained = total > BigNum.Zero ? total : BigNum.Zero;
        BattleGained = BigNum.Zero;   // "本场掉落"不跨进程
    }

    public void Reset()
    {
        Amount = BigNum.Zero;
        TotalGained = BigNum.Zero;
        BattleGained = BigNum.Zero;
    }
}
