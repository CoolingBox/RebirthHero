// 文件：Silver.cs
// 《轮回大侠》M2.2 银两 —— 货币账户（**击杀即时掉落**）
//
// 用户口径（2026-10-15）："击杀就可立即获得经验资源" + "敌方掉落的是修为(就是经验)和银两"。
//   每击杀一个敌人，银两立刻进账（整队共享一份）；
//   用途 = GDD §4.2 的 sink：客栈招募 / 武学商店 / 装备强化 / 境界突破（M2.2 阶段只攒不花）。

using System;

public sealed class Silver
{
    /// <summary>当前银两余额。</summary>
    public double Amount { get; private set; }

    /// <summary>本世累计获取（含已花掉的）。</summary>
    public double TotalGained { get; private set; }

    /// <summary>本场累计掉落（战斗中实时显示，战斗开始时清零）。</summary>
    public double BattleGained { get; private set; }

    /// <summary>战斗开始：清空"本场掉落"计数。</summary>
    public void BeginBattle() => BattleGained = 0;

    /// <summary>某个敌人被击杀时调用：按层进账一份银两。</summary>
    public double GainForKill(int layer)
    {
        double g = CultivationConfig.SilverPerKill(layer);
        Amount += g;
        TotalGained += g;
        BattleGained += g;
        return g;
    }

    /// <summary>消费（M2.3 客栈等出口接入；不足则不扣并返回 false）。</summary>
    public bool TrySpend(double amount)
    {
        if (amount <= 0 || Amount < amount) return false;
        Amount -= amount;
        return true;
    }

    /// <summary>读档：写回余额与本世累计（累计只用于展示，负数会夹成 0）。</summary>
    public void Restore(double amount, double total)
    {
        Amount = amount > 0 ? amount : 0;
        TotalGained = total > 0 ? total : 0;
        BattleGained = 0;   // "本场掉落"不跨进程
    }

    public void Reset()
    {
        Amount = 0;
        TotalGained = 0;
        BattleGained = 0;
    }
}
