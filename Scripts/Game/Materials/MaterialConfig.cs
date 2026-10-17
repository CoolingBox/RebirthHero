// 文件：MaterialConfig.cs
// 《轮回大侠》材料系统 —— 掉落与档位常数（由 TuningConfig 总账注入）
//
// 为什么要有"分层材料"（用户 2026-10-17 选定方向 A）：
//   掉落随层指数增长（1.16^层），敌方 HP 也是同底数 1.16 → **刷任何层的收益/秒几乎相同**。
//   于是"推层"本身没有经济回报，"选关"也就没有意义（选哪都一样/都选最高层）。
//   分层材料让"某品阶只在特定层段产出"——要精铁就得回 11~20 层刷，
//   选层于是重新变成一个真实决策："我缺哪档材料 → 去哪个层段"。
//
// 口径：
//   · 档位 = clamp((层−1)/MatTierEveryLayer + 1, 1, 5)，每 10 层一档
//   · 每次普通击杀掉 **固定数量**（确定性，不赌概率 —— 赌运气留给装备词条那一层）
//   · 打穿整层（收尾波获胜）额外给 MatClearBonus
//
// ⚠️ 数值由 `TuningConfig` 注入；下列 public static 字段 = 兜底默认值（表缺失时仍能跑）。
//   全部为 [PLACEHOLDER · 待 M2.5 装备 sink 接入后，按"多少层够一次升阶"反推校准]。

public static class MaterialConfig
{
    /// <summary>每多少层一个品阶档（10 → 层 1~10 一档）。</summary>
    public static int MatTierEveryLayer = 10;

    /// <summary>每次普通击杀掉落的材料数（确定性）。</summary>
    public static int MatMobDrop = 1;

    /// <summary>打穿整层的额外奖励（收尾波获胜时结算一次）。</summary>
    public static int MatClearBonus = 5;
}
