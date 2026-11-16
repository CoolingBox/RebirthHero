// 文件：HeroCardLayer.cs
// 《轮回大侠》表现层 —— **侠客单位卡**层（Godot 节点：Control 子类，挂在 MainPanel.tscn 的 TCards 上）
//
// 2026-11-04：卡牌从 500×176 横条换成 **7:9 竖版 4 层视差卡**（BattleParallaxCard）。
// 2026-11-05：站位改 **1 前 3 后 · 左右对战**；卡的显示尺寸再按**仇恨**微调。
//
// 本类只做三件事，**不含任何绘制细节**：
//   ① 按阵营与位次算坐标并建卡；
//   ② 把引擎数据转给每张卡刷新；
//   ③ 给飘字/音频提供锚点。
// 卡内部（分层视差、血条拖尾、信息带）全在 BattleParallaxCard 里，改视觉不用碰这里。

using System;
using System.Collections.Generic;
using Godot;

public partial class HeroCardLayer : Node2D
{
    /// <summary>
    /// 仇恨 → 卡面尺寸的映射强度。
    /// 权重是隐藏数值（1 号位 30 / 其余 10），玩家看不到"谁更容易挨打"；
    /// 用尺寸把它变成能一眼读出的线索 —— 大一号的那张就是对面在集火的那张。
    /// 0.10 配 clamp −7% / +8%：标称 4 人队实际落在 **前排 1.08 / 后排 0.967**（差 11.3%），
    /// 一眼能看出但不破坏网格 —— 后排三张同权重同尺寸，不会互相挤到（行距只对同列生效）。
    /// </summary>
    const double AggroScaleK = 0.10;
    const float ScaleMin = 0.93f;
    const float ScaleMax = 1.08f;

    readonly Dictionary<BattleUnit, BattleParallaxCard> _cards = new Dictionary<BattleUnit, BattleParallaxCard>();

    /// <summary>点某张卡（交给主面板开详情）。本层不知道详情长什么样。</summary>
    public Action<BattleUnit> OnCardClicked;

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
    /// 卡的尺寸再按队内仇恨微调 —— 权重高的卡略大。
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
    /// 队内仇恨 → 显示倍率。
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
            sum += Math.Max(1.0, t.BaseAggro);
            n++;
        }

        if (n <= 1 || sum <= 0)
        {
            return 1f;
        }

        double avg = sum / n;
        double r = Math.Max(1.0, u.BaseAggro) / avg;
        double s = 1.0 + (r - 1.0) * AggroScaleK;
        return (float)Math.Clamp(s, ScaleMin, ScaleMax);
    }

    void BuildOne(BattleUnit u, Vector2 cell, bool enemy, float scale)
    {
        Vector2 baseSize = new Vector2(BattleLayout.CardW, BattleLayout.CardH);
        Vector2 raw = baseSize * scale;

        // 吸附到整数像素。仇恨缩放 0.93~1.08 会让卡尺寸和位置都带小数，
        // 边框贴图（406×522 对应 203×261 的 2:1 整数缩小）因此落在半像素上，
        // 每帧被双线性重采样，金线边缘发虚、发毛。取整后纹理采样点对齐像素网格。
        Vector2 size = new Vector2(Mathf.Round(raw.X), Mathf.Round(raw.Y));
        Vector2 rawPos = cell + (baseSize - size) * 0.5f;
        Vector2 pos = new Vector2(Mathf.Round(rawPos.X), Mathf.Round(rawPos.Y));

        BattleParallaxCard card = new BattleParallaxCard();
        // 中心对齐格位中心：尺寸变化时四周均匀伸缩，不会向某一侧偏
        card.Position = pos;
        card.BasePos = pos;      // 微对撞的归位基准，动画只动 Position
        card.Size = size;
        AddChild(card);

        // Setup 必须在 AddChild 之后：卡面节点是在 _Ready 里建的
        card.Setup(u, enemy, BattleArt.KeyOf(u.Name));
        _cards[u] = card;
    }

    /// <summary>
    /// 2026-11-16 迁到 Node2D 后，卡根节点收不到 gui_input（先验探针实测：
    /// Control 挂在 Node2D 父链下不参与 GUI 输入传播），
    /// 所以点击改由本层按卡的全局矩形统一命中，再转给面板。
    ///
    /// 用 _Input 而不是 _UnhandledInput：上层有全屏 Control（BattlePanel 根），
    /// 实机验收时点击在 GUI 阶段就被吞掉，_UnhandledInput 根本收不到
    /// （手动直调能开详情，注入事件不能，差异就在这里）。
    /// 只有命中卡矩形才 SetInputAsHandled，等价于旧版 _GuiInput + AcceptEvent()；
    /// 点在 HUD 按钮上不命中，事件照常传给 GUI。
    /// 只认 Pressed 不认 Released：按住拖到别处松手不该弹详情。
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        InputEventMouseButton mb = @event as InputEventMouseButton;
        if (mb == null || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        // 用事件自带的 GlobalPosition，不要读 GetGlobalMousePosition()：
        // 后者是 Viewport 的鼠标状态，注入事件 / 触屏 / 窗口焦点变化时未必同步，
        // 迁移后第一次验收就是因为这个导致命中失败（点卡不开详情）。
        Vector2 mp = mb.GlobalPosition;
        foreach (KeyValuePair<BattleUnit, BattleParallaxCard> kv in _cards)
        {
            BattleParallaxCard c = kv.Value;
            if (c == null || !IsInstanceValid(c))
            {
                continue;
            }

            // 用 GlobalTransform 换算卡的两个对角，不要写 GlobalPosition + Size：
            // 对撞期间卡有轻微 Scale、枢轴在卡中心，GlobalPosition 已不等于渲染左上角，
            // 直接加 Size 会让命中框整体偏掉（越放大偏得越多）。
            Transform2D xf = c.GlobalTransform;
            Vector2 corner0 = xf * Vector2.Zero;
            Vector2 corner1 = xf * c.Size;
            Rect2 r = new Rect2(corner0, corner1 - corner0);
            if (r.HasPoint(mp))
            {
                GetViewport().SetInputAsHandled();
                OnCardClickedInternal(kv.Key);
                return;
            }
        }
    }

    /// <summary>卡的点击回调 → 转给面板（本层只是"转发"，不判断点开之后做什么）。</summary>
    void OnCardClickedInternal(BattleUnit u)
    {
        if (OnCardClicked != null)
        {
            OnCardClicked(u);
        }
    }

    /// <summary>
    /// 每次伤害都让攻击方卡做一次微前冲（2026-11-16 用户选「全部攻击微对撞」）。
    /// 方向取双方基准位的 X 差：本作左右对战，x 即冲锋方向。
    /// 召唤物没有独立卡（只有堆叠卡），这类攻击跳过，不凭空造位移。
    /// </summary>
    public void PlayAttackLunge(BattleUnit attacker, BattleUnit target)
    {
        if (attacker == null || target == null)
        {
            return;
        }

        BattleParallaxCard fromCard;
        if (!_cards.TryGetValue(attacker, out fromCard))
        {
            return;
        }
        if (fromCard == null || !IsInstanceValid(fromCard))
        {
            return;
        }

        BattleParallaxCard toCard;
        if (!_cards.TryGetValue(target, out toCard))
        {
            return;
        }

        float dir = Mathf.Sign(toCard.BasePos.X - fromCard.BasePos.X);
        if (dir == 0f)
        {
            dir = 1f;
        }
        fromCard.PlayLunge(dir);
    }

    /// <summary>把引擎数据推给每张卡（10Hz，由面板的 Tick 调用）。</summary>
    public void Refresh(BattleEngine engine)
    {
        foreach (KeyValuePair<BattleUnit, BattleParallaxCard> kv in _cards)
        {
            kv.Value.Refresh(engine);
        }
    }

    /// <summary>
    /// 某单位出手：把武学名（含段数）报到它的卡牌头部。
    /// 没有卡的单位（召唤物归堆叠层、已清场的单位）直接忽略 —— 不给它们凭空造一个锚点。
    /// </summary>
    public void ShowCast(BattleUnit u, string skill, int hits)
    {
        if (u == null)
        {
            return;
        }

        BattleParallaxCard c;
        if (!_cards.TryGetValue(u, out c))
        {
            return;
        }
        if (c == null || !IsInstanceValid(c))
        {
            return;
        }

        c.ShowCast(skill, hits);
    }

    /// <summary>飘字锚点：卡面上部（人物头顶一带）。</summary>
    public bool TryGetAnchor(BattleUnit u, out Vector2 pos)
    {
        BattleParallaxCard c;
        if (_cards.TryGetValue(u, out c))
        {
            // 用卡的实际尺寸算中心（卡会按仇恨缩放，写死 CardW 会让飘字偏）
            pos = c.BasePos + new Vector2(c.Size.X * 0.5f - 60f, 40f);
            return true;
        }

        pos = Vector2.Zero;
        return false;
    }

    /// <summary>
    /// 特效锚点：**卡面中心偏上**（人物胸口一带）。
    /// 与飘字锚点（<see cref="TryGetAnchor"/>，取头顶偏左）刻意分开：
    /// 飘字要往上浮所以从头边起，打击特效要落在"人身上"。
    /// </summary>
    public bool TryGetFxAnchor(BattleUnit u, out Vector2 pos)
    {
        BattleParallaxCard c;
        if (_cards.TryGetValue(u, out c))
        {
            pos = c.BasePos + new Vector2(c.Size.X * 0.5f, c.Size.Y * 0.45f);
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
