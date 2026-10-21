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

    // —— 血条"先变色后加减"的拖尾色（2026-10-21）——
    // 掉血拖尾偏橙红（比 Damage 亮，压在血条上要能看清）、回血拖尾偏绿松石。
    public static readonly Color HpTrailDamage = new Color(1.00f, 0.45f, 0.25f, 0.90f);
    public static readonly Color HpTrailHeal = new Color(0.45f, 1.00f, 0.55f, 0.90f);

    // —— 护盾在血条上的金色蒙层与文字（用户口径：护盾要金色，不用蓝）——
    // 保留蓝色的 Shield 给"护盾数值/飘字"（盾=蓝是跨游戏共识），血条上的**占比**用金色，
    // 两者不冲突：蓝色说"有多少盾"，金色说"盾占血条多少"。
    public static readonly Color ShieldGoldFill = new Color(1.00f, 0.82f, 0.30f, 0.55f);
    public static readonly Color ShieldGoldText = new Color(1.00f, 0.88f, 0.45f);

    // 装备稀有度（白/绿/蓝/紫/橙）—— 玩家对这套配色有跨游戏的共识，
    // 换主题也不该动：玩家是靠颜色而不是文字在扫"出没出好货"。
    public static readonly Color RarityCommon = new Color(0.85f, 0.85f, 0.85f);
    public static readonly Color RarityFine = new Color(0.35f, 0.95f, 0.45f);
    public static readonly Color RarityEpic = new Color(0.35f, 0.65f, 1.00f);
    public static readonly Color RarityLegend = new Color(0.75f, 0.45f, 1.00f);
    public static readonly Color RarityMyth = new Color(1.00f, 0.65f, 0.20f);

    public static Color RarityOf(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Fine: return RarityFine;
            case Rarity.Epic: return RarityEpic;
            case Rarity.Legend: return RarityLegend;
            case Rarity.Myth: return RarityMyth;
            default: return RarityCommon;
        }
    }
}
