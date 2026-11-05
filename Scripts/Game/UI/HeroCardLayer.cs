// 文件：HeroCardLayer.cs
// 《轮回大侠》表现层 —— **侠客单位卡**层（Godot 节点：Control 子类，挂在 MainPanel.tscn 的 TCards 上）
//
// 2026-11-04：卡牌从 500×176 横条换成 **7:9 竖版 4 层视差卡**（BattleParallaxCard）。
// 2026-11-05：站位改 **1 前 3 后 · 左右对战**；卡的显示尺寸再按**受击权重**微调。
//
// 本类只做三件事，**不含任何绘制细节**：
//   ① 按阵营与位次算坐标并建卡；
//   ② 把引擎数据转给每张卡刷新；
//   ③ 给飘字/音频提供锚点。
// 卡内部（分层视差、血条拖尾、信息带）全在 BattleParallaxCard 里，改视觉不用碰这里。

using System;
using System.Collections.Generic;
using Godot;

public partial class HeroCardLayer : Control
{
    /// <summary>
    /// 受击权重 → 卡面尺寸的映射强度。
    /// 权重是隐藏数值（1 号位 30 / 其余 10），玩家看不到"谁更容易挨打"；
    /// 用尺寸把它变成能一眼读出的线索 —— 大一号的那张就是对面在集火的那张。
    /// 0.10 配 clamp −7% / +8%：标称 4 人队实际落在 **前排 1.08 / 后排 0.967**（差 11.3%），
    /// 一眼能看出但不破坏网格 —— 后排三张同权重同尺寸，不会互相挤到（行距只对同列生效）。
    /// </summary>
    const double WeightScaleK = 0.10;
    const float ScaleMin = 0.93f;
    const float ScaleMax = 1.08f;

    readonly Dictionary<BattleUnit, BattleParallaxCard> _cards = new Dictionary<BattleUnit, BattleParallaxCard>();

    public void Clear()
    {
        foreach (KeyValuePair<BattleUnit, BattleParallaxCard> kv in _cards)
        {
            if (kv.Value != null && IsInstanceValid(kv.Value))
            {
                kv.Value.QueueFree();
            }
        }
        _cards.Clear();
    }

    /// <summary>
    /// A 队（我方，左）/ B 队（敌方，右）按队内顺序摆位；召唤物跳过（归 SummonStackLayer）。
    /// 双方都是 **1 前 3 后**：1 号位独占前排，2~4 号位在后排一列（格位见 BattleLayout.PosOf）。
    /// 卡的尺寸再按队内受击权重微调 —— 权重高的卡略大。
    /// </summary>
    public void Build(List<BattleUnit> a, List<BattleUnit> b, float areaX)
    {
        for (int i = 0; i < BattleLayout.RowCount; i++)
        {
            if (i < a.Count && !a[i].IsSummon)
            {
                BuildOne(a[i], BattleLayout.PosOf(false, i), false, ScaleOf(a, a[i]));
            }
            if (i < b.Count && !b[i].IsSummon)
            {
                BuildOne(b[i], BattleLayout.PosOf(true, i), true, ScaleOf(b, b[i]));
            }
        }
    }

    /// <summary>
    /// 队内受击权重 → 显示倍率。
    /// 用**队内平均权重**做基准而不是绝对值：队伍构成变了（少人 / 带召唤武学加权重）也不用改参数，
    /// "谁比队友更容易挨打"自动反映在尺寸上。
    /// 队伍只剩 1 人时返回 1（没有比较对象，不该凭空变大）。
    /// </summary>
    static float ScaleOf(List<BattleUnit> team, BattleUnit u)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < team.Count; i++)
        {
            BattleUnit t = team[i];
            if (t.IsSummon)
            {
                continue;
            }
            sum += Math.Max(1.0, t.BaseW);
            n++;
        }

        if (n <= 1 || sum <= 0)
        {
            return 1f;
        }

        double avg = sum / n;
        double r = Math.Max(1.0, u.BaseW) / avg;
        double s = 1.0 + (r - 1.0) * WeightScaleK;
        return (float)Math.Clamp(s, ScaleMin, ScaleMax);
    }

    void BuildOne(BattleUnit u, Vector2 cell, bool enemy, float scale)
    {
        Vector2 baseSize = new Vector2(BattleLayout.CardW, BattleLayout.CardH);
        Vector2 size = baseSize * scale;

        BattleParallaxCard card = new BattleParallaxCard();
        // 中心对齐格位中心：尺寸变化时四周均匀伸缩，不会向某一侧偏
        card.Position = cell + (baseSize - size) * 0.5f;
        card.Size = size;
        AddChild(card);

        // Setup 必须在 AddChild 之后：卡面节点是在 _Ready 里建的
        card.Setup(u, enemy, BattleArt.KeyOf(u.Name));
        _cards[u] = card;
    }

    /// <summary>把引擎数据推给每张卡（10Hz，由面板的 Tick 调用）。</summary>
    public void Refresh(BattleEngine engine)
    {
        foreach (KeyValuePair<BattleUnit, BattleParallaxCard> kv in _cards)
        {
            kv.Value.Refresh(engine);
        }
    }

    /// <summary>飘字锚点：卡面上部（人物头顶一带）。</summary>
    public bool TryGetAnchor(BattleUnit u, out Vector2 pos)
    {
        BattleParallaxCard c;
        if (_cards.TryGetValue(u, out c))
        {
            // 用卡的实际尺寸算中心（卡会按受击权重缩放，写死 CardW 会让飘字偏）
            pos = c.Position + new Vector2(c.Size.X * 0.5f - 60f, 40f);
            return true;
        }

        pos = Vector2.Zero;
        return false;
    }

    /// <summary>同上，但换算到**世界坐标**（供音频定位用；飘字仍用局部坐标）。</summary>
    public bool TryGetWorldAnchor(BattleUnit u, out Vector2 world)
    {
        if (!TryGetAnchor(u, out Vector2 local))
        {
            world = Vector2.Zero;
            return false;
        }

        world = GetGlobalTransform() * local;
        return true;
    }
}
