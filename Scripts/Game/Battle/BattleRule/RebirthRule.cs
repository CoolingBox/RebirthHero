// 文件：RebirthRule.cs
// 《轮回大侠》**战斗规则层 · 转世** —— 这一世怎么结算、下一世从什么状态开始
//
// 口径（GDD §3.3 账本 + 用户 2026-11-01）：
//   · 够门槛（`DaoConfig.MinLayer`）→ 按**最高层数**折道行，道行**不清零**
//   · 不够门槛 → 只是普通重开，不发道行（否则玩家会在第 3 层反复刷 sqrt(3) 点）
//   · 归零的是"此生资产"：层数 / 修为等级 / 银两 / 材料 / 装备 / 境界
//   · 道行与武学装配、天赋、装备蓝图**不归零**（跨世资产）
//
// 为什么"发道行"和"归零"分成两步：
//   发奖要点余额（Dao 是跨世货币），归零要动六本账；中间还有"关掉所有子界面"这类
//   表现层动作要插进来。合成一个方法会让面板没法在正确的时机做它那部分。
//
// 本文件不引用 Godot（无头探针可直接编译）。

public static class RebirthRule
{
    /// <summary>够不够转世门槛（按本世最高层数）。</summary>
    public static bool CanRebirth(int bestLayer)
    {
        return DaoConfig.CanRebirth(bestLayer);
    }

    /// <summary>这一次转世能换多少道行（含天赋「天机」的乘区）。不够门槛返回 0。</summary>
    public static double DaoGain(int bestLayer)
    {
        if (!CanRebirth(bestLayer))
        {
            return 0;
        }

        double mul = Talent.Bonus().DaoMul;
        if (mul <= 0)
        {
            mul = 1;
        }
        return DaoConfig.ForLayer(bestLayer) * mul;
    }

    /// <summary>
    /// 发放转世道行并返回实发数额（0 = 不够门槛，什么也没发）。
    /// 只碰 Dao 一本账 —— 归零走 <see cref="ResetLife"/>。
    /// </summary>
    public static double GrantDao(BattleRuleContext ctx, int bestLayer)
    {
        if (ctx == null || ctx.Dao == null)
        {
            return 0;
        }

        double gain = DaoGain(bestLayer);
        if (gain <= 0)
        {
            return 0;
        }

        ctx.Dao.Add(gain);
        return gain;
    }

    /// <summary>
    /// 清零"此生资产"（层数 / 修为 / 银两 / 材料 / 装备 / 境界）。
    /// ⚠️ 刻意不动 Dao、Talent、SkillLoadout —— 那三样是跨世的。
    /// </summary>
    public static void ResetLife(BattleRuleContext ctx)
    {
        if (ctx == null)
        {
            return;
        }

        if (ctx.Run != null)
        {
            ctx.Run.Reset();
        }
        if (ctx.Cult != null)
        {
            ctx.Cult.Reset();
        }
        if (ctx.Money != null)
        {
            ctx.Money.Reset();
        }
        if (ctx.Materials != null)
        {
            ctx.Materials.Reset();
        }
        if (ctx.Equips != null)
        {
            ctx.Equips.Reset();
        }

        // 境界是"这一世的纵向乘区"（GDD §4.2 五乘区之②）→ 跟着归零
        Realm.Reset();
    }
}
