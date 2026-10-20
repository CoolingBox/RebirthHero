// 文件：UISignalColors.cs
// 《轮回大侠》读盘信号色 —— 所有主题共用，**刻意不随主题变化**
//
// 为什么不进 .tres：
//   这些不是皮肤，是逻辑。血条绿黄红、护盾蓝、伤害/治疗飘字、胜负色是玩家
//   "一眼读懂战况"的语言，换肤换掉会让"红色=危险"的认知失效。
//   它们与主题无关，因此留在代码里（改这里等于改规则，不该在资源里被随手改掉）。
//
// 叫"信号色"而不是"语义色"：作用是向玩家发信号（危险 / 安全 / 收益），不是表达语义。

using Godot;

public static class UISignalColors
{
    public static readonly Color HpGood = new Color(0.25f, 0.85f, 0.35f);   // 血量 > 50%
    public static readonly Color HpWarn = new Color(0.95f, 0.85f, 0.20f);   // 25%~50%
    public static readonly Color HpBad = new Color(0.90f, 0.25f, 0.20f);    // < 25%
    public static readonly Color HpBack = new Color(0.30f, 0.10f, 0.10f);   // 血条槽
    public static readonly Color HpLocked = new Color(0.50f, 0.52f, 0.55f); // 被召唤锁死的生命
    public static readonly Color GaugeBack = new Color(0.10f, 0.10f, 0.32f);
    public static readonly Color GaugeFill = new Color(0.35f, 0.55f, 1.00f);
    public static readonly Color Shield = new Color(0.40f, 0.90f, 1.00f);
    public static readonly Color Damage = new Color(1.00f, 0.40f, 0.30f);
    public static readonly Color Heal = new Color(0.40f, 1.00f, 0.50f);
    public static readonly Color Reward = new Color(1.00f, 0.92f, 0.45f);   // 掉落飘字
    public static readonly Color Win = new Color(0.40f, 1.00f, 0.50f);
    public static readonly Color Lose = new Color(1.00f, 0.45f, 0.35f);
}
