// 文件：Dao.cs
// 《轮回大侠》M3 道行 —— 永久货币（GDD §4.1：唯一来源 = 转世折算，唯一出口 = 天赋树）
//
// 它和银两/修为的关键差别：**转世不清零**。
//   · 银两 = 此生主货币 → 转世归零
//   · 修为 = 此生经验 → 转世归零
//   · 道行 = 永久货币 → 只在"转世"这一刻增加，之后跟着玩家走完所有世
//
// 所以这个类刻意**没有 Reset()** —— 唯一能减道行的只有花在天赋树上（后续接 Talent）。

using System;

public sealed class Dao
{
    /// <summary>当前可用道行（点天赋树会把这里扣掉）。</summary>
    public double Amount { get; private set; }

    /// <summary>历世累计获得的道行（含已花掉的，用于 UI 展示"这一世一共攒了多少"）。</summary>
    public double TotalGained { get; private set; }

    /// <summary>转世时调用：按最高层数折算并进账，返回本次获得的道行。</summary>
    public double GainForRebirth(int bestLayer)
    {
        double gain = DaoConfig.ForLayer(bestLayer);
        if (gain <= 0)
        {
            return 0;
        }
        Amount += gain;
        TotalGained += gain;
        return gain;
    }

    /// <summary>直接进账（天赋树回退 / 调试用）。</summary>
    public double Add(double amount)
    {
        if (amount <= 0)
        {
            return 0;
        }
        Amount += amount;
        TotalGained += amount;
        return amount;
    }

    /// <summary>消费（天赋树点节点；不足则不扣并返回 false）。</summary>
    public bool TrySpend(double amount)
    {
        if (amount <= 0)
        {
            return false;
        }
        if (Amount < amount)
        {
            return false;
        }
        Amount -= amount;
        return true;
    }

    /// <summary>读档：写回余额与累计（负数夹成 0）。</summary>
    public void Restore(double amount, double total)
    {
        Amount = amount > 0 ? amount : 0;
        TotalGained = total > 0 ? total : 0;
    }
}
