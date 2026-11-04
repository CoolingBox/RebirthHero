// 文件：DajieConfig.cs
// 《轮回大侠》M3 大劫 —— 定时上门的 Boss 战参数
//
// 口径（用户 2026-11-01）：
//   · **每 2 分钟强制一场 Boss 战**
//   · **失败直接结算重开**（= 转世：发道行 + 归零，见 BattlePanel.OnNext）
//   · **Boss 战不算入时间**（打大劫期间倒计时暂停）
//
// 强度：本世**打赢过几次**决定下一场有多强（GDD §3.1 的 k^n，n = 已胜次数）。
// 打赢 = 可以继续这一世（但下一场更难），打输 = 转世重来。
// 数学上必输、设计上不锁死 —— 构筑够妖就能多撑几轮，这是 GDD 明写的"不设剧本必输"。

using System;

public static class DajieConfig
{
    /// <summary>每多少秒来一场。Tuning: DajieInterval</summary>
    public static double DajieInterval = 120;

    /// <summary>每次打赢后下一场强度 ×本值。Tuning: DajieK</summary>
    public static double DajieK = 1.8;

    /// <summary>第 wins 场大劫的强度倍率（wins = 本世已打赢次数，从 0 起）。</summary>
    public static double PowerOf(int wins)
    {
        if (wins <= 0)
        {
            return 1;
        }
        return Math.Pow(DajieK, wins);
    }

    /// <summary>距离下一场还要多少秒（用于 UI 倒计时）。</summary>
    public static double Remain(double clock)
    {
        double left = DajieInterval - clock;
        return left > 0 ? left : 0;
    }

    /// <summary>实际间隔（秒）—— 乘天赋「沉着」的间隔乘区（mul &lt; 1 = 来得更勤）。</summary>
    /// <para>
    /// 下限 30 秒：天赋可以叠到 −30%，再不设限玩家会把大劫刷成"每 20 秒一场"，
    /// 那已经不是战斗节奏而是读条。
    /// </para>
    public static double IntervalWith(double mul)
    {
        if (mul <= 0)
        {
            mul = 1;
        }
        double v = DajieInterval * mul;
        return v < 30 ? 30 : v;
    }

    /// <summary>带天赋乘区的倒计时。</summary>
    public static double RemainWith(double clock, double mul)
    {
        double left = IntervalWith(mul) - clock;
        return left > 0 ? left : 0;
    }
}
