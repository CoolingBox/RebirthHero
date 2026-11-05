// 文件：BattleSettle.cs
// 《轮回大侠》**战斗规则层 · 结算** —— 一场打完要回答的三个问题
//
//   ① 还剩几个人能续战？（面板拿它出"3/4 人续战"的提示）
//   ② 这层打穿了，多给什么？（清层材料在 DropRule，这里只做境界突破）
//   ③ 境界够不够跨？（双门槛：银两 + 已达层数）
//
// 为什么境界突破放在"结算"而不是"Progression/Realm"：
//   `Realm.CanBreak / TryBreak` 是**纯判定与扣费**，谁都能调；
//   "什么时候该自动破"是**玩法决策**（本作的口径：清层时自动破，不给玩家按钮）—— 
//   那条口径属于战斗规则的编排，不属于境界数据本身。
//
// 本文件不引用 Godot（无头探针可直接编译）。

using System.Collections.Generic;

/// <summary>一场打完的存活读数（只统计真正的侠客：召唤物与空槽不算人头）。</summary>
public struct SurvivorCount
{
    public int Alive;
    public int Total;
}

public static class BattleSettle
{
    /// <summary>
    /// 数"还能续战的人"。
    /// 排除召唤物（它们是武学的产物，不算阵容）与 Slot &lt; 1 的空槽（阵亡后被引擎清空）。
    /// </summary>
    public static SurvivorCount CountSurvivors(BattleEngine engine)
    {
        SurvivorCount count = new SurvivorCount();
        if (engine == null || engine.A == null)
        {
            return count;
        }

        foreach (BattleUnit u in engine.A)
        {
            if (u == null)
            {
                continue;
            }
            if (u.IsSummon)
            {
                continue;
            }
            if (u.Slot < 1)
            {
                continue;
            }

            count.Total++;
            if (u.Alive)
            {
                count.Alive++;
            }
        }

        return count;
    }

    /// <summary>
    /// 自动突破境界（清层时调用）。
    /// 返回**突破后的新境界行**；没突破（门槛不到 / 已满境 / 扣费失败）返回 null。
    /// <para>
    /// 为什么要先判 <see cref="Realm.Next"/> 再 <c>TryBreak</c>：
    /// 满境时 Next 为 null，但 CanBreak 仍可能为 true —— 直接 TryBreak 会白扣银两。
    /// 这个顺序就是"能不能跨"与"跨到哪"的分离。
    /// </para>
    /// </summary>
    public static RealmConfig TryBreakRealm(Silver money, int bestLayer)
    {
        if (!Realm.CanBreak(money, bestLayer))
        {
            return null;
        }

        RealmConfig next = Realm.Next;
        if (next == null)
        {
            return null;
        }

        if (!Realm.TryBreak(money, bestLayer))
        {
            return null;
        }

        return next;
    }
}
