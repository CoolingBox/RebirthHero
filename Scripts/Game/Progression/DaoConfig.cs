// 文件：DaoConfig.cs
// 《轮回大侠》M3 道行 —— 转世折算的参数表（数值全部登记在 TuningConfig 总账里）
//
// 口径（用户 2026-11-01 改版）：
//   · **3 层起就能转世**（不再要求 15 层）
//   · **每高一层叠乘 1.5**：道行 = 1.5^(层数 − 3) —— 层 3 = 1 点、层 4 = 1.5、层 10 ≈ 11、层 20 ≈ 657
//
// ⚠️ 这条公式**不是亚线性**（GDD §3.2 原本写的是 sqrt，理由是"转世收益递减，逼玩家多转几次"）。
// 用户明确要指数，取舍变了：早转 = 稳但少，晚转 = 高收益但可能被大劫打断。
// 指数道行 + 推图本身也是指数（敌方 1.16^层），正反馈很强 —— 通胀风险由天赋树成本曲线吸收。
//
// ⚠️ 字段名必须与 TuningConfig 的 Key 完全一致（Tuning 按字段名反射注入 + 双向校验）。

using System;
using TinyFramework;

public static class DaoConfig
{
    /// <summary>每高一层的道行倍率。Tuning: DaoPerLayer</summary>
    public static double DaoPerLayer = 1.5;

    /// <summary>可转世的最低最高层数。Tuning: DaoMinLayer</summary>
    public static int DaoMinLayer = 3;

    /// <summary>整体倍率（调全局收益用；默认 1.0 不改公式形状）。Tuning: DaoRebirthCoef</summary>
    public static double DaoRebirthCoef = 1.0;

    /// <summary>
    /// 某个最高层数能换多少道行（不到门槛返回 0）。
    /// 返回 BigNum：1.5^(层−3) 在层数破千时会超出 double 上限，
    /// 直接 Math.Pow 会得到 Infinity，而 BigNum.From(Infinity) 会静默归零 ——
    /// 于是用"取 10 的对数再拆尾数/指数"，永不溢出。
    /// </summary>
    public static BigNum ForLayer(int bestLayer)
    {
        if (bestLayer < DaoMinLayer)
        {
            return BigNum.Zero;
        }

        int n = bestLayer - DaoMinLayer;
        double coef = DaoRebirthCoef > 0 ? DaoRebirthCoef : 1.0;
        double log10 = Math.Log10(DaoPerLayer) * n + Math.Log10(coef);
        long exp = (long)Math.Floor(log10);
        double mant = Math.Pow(10, log10 - exp);
        return BigNum.FromParts(mant, exp);
    }

    /// <summary>够不够格转世（门槛只看层数，不看别的）。</summary>
    public static bool CanRebirth(int bestLayer)
    {
        return bestLayer >= DaoMinLayer;
    }
}
