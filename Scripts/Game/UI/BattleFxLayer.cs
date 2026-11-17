// 文件：BattleFxLayer.cs
// 《轮回大侠》战斗特效层 —— **单个 CanvasItem 自绘**，不做节点、不用粒子
//
// 为什么是自绘（2026-11-14）：
//   ① 项目没有任何特效素材（全是 ColorRect / Label / 立绘），靠素材就得先出图；
//   ② 8 张卡同屏，若每个特效一个节点 + Tween，一帧能开合几十个节点，GC 与树开销都不划算；
//   ③ 同一套代码要画出"线 / 环 / 裂 / 泡"四种语言 —— 程序化几何比贴图灵活得多。
//
// 语言（形状先于颜色，玩家是扫视的）：
//   外功 = 线（斜向斩过去）　内功 = 环（由目标向外扩）　真伤 = 裂（竖撕 + 白闪）
//   P0 只实现：斩线 / 火花 / 内功环 / 死亡碎片。
//
// 硬约束（见 .codebuddy/docs/轮回大侠-战斗特效设计-v0.1.md）：
//   · 不进信息带（锚点取卡面上 2/3，卡底 84px 留给血条）
//   · 同时最多 64 条，超了丢最旧
//   · 必须画在飘字**下面**：BattlePanel 里本层在 TFxLayer 之前

using System;
using System.Collections.Generic;
using Godot;

public partial class BattleFxLayer : Node2D
{
    /// <summary>特效种类。几何与节奏都由它决定，颜色只是附加信息。</summary>
    public enum FxKind
    {
        Slash = 0,   // 外功斩线
        Ring = 1,    // 内功扩散环
        Crack = 2,   // 真伤撕裂
        Spark = 3,   // 命中火花
        Death = 4,   // 死亡碎片
        Bubble = 5,  // 毒：上浮的泡
        Ember = 6,   // 火：上窜的火星
        Miss = 7,    // 闪避：掠过的空挥弧
        Bolt = 8,    // 投射物：从释放者飞向目标（v0.2，按相性分形）
        Cast = 9,    // 起手：释放者卡面一闪（v0.2）
        Impact = 10, // 相性命中形（v0.2，由投射物消亡时自动带出）
    }

    /// <summary>一条特效记录。字段全用 float，避免每帧装箱。</summary>
    internal struct FxItem
    {
        public FxKind Kind;
        public Vector2 A;
        public Vector2 B;
        public Color C;
        public float T;
        public float Dur;
        public float Power;   // 0~1：伤害占比，决定长度/条数/亮度
        public int Seed;      // 随机形状（火花角度、碎片方向）用固定种子，播片可复现
        public Category Cat;  // 武学相性：投射物形态 / 命中形 / 轨迹都由它决定（v0.2）
    }

    const int MaxItems = 64;

    readonly List<FxItem> _items = new List<FxItem>();

    /// <summary>HDR 发光源子节点（独立 CanvasItem，才能有自己的 blend_add shader）。</summary>
    BattleFxGlow _glow;

    /// <summary>发光源层只读共享同一份特效记录，不复制、不另开定时器。</summary>
    internal List<FxItem> Items
    {
        get { return _items; }
    }

    public override void _Ready()
    {
        // 像素特效要放大，线性过滤会把像素块糊成一团；最近邻保持硬边。
        // 程序化几何（DrawLine / DrawArc）不受纹理过滤影响。
        TextureFilter = TextureFilterEnum.Nearest;
        SetupGlow();
        SetProcess(true);
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }
        _items.Clear();
        QueueRedraw();
        if (_glow != null)
        {
            _glow.QueueRedraw();
        }
    }

    /// <summary>放一条特效。dur 由调用方给（不同语言节奏不同：线快、环慢）。</summary>
    public void Play(FxKind kind, Vector2 a, Vector2 b, Color color, float dur, float power)
    {
        if (dur <= 0.01f)
        {
            dur = 0.16f;
        }

        FxItem it = new FxItem();
        it.Kind = kind;
        it.A = a;
        it.B = b;
        it.C = color;
        it.T = 0f;
        it.Dur = dur;
        it.Power = Mathf.Clamp(power, 0f, 1f);
        it.Seed = (int)(a.X * 31f + b.Y * 17f + _items.Count * 7f);

        if (_items.Count >= MaxItems)
        {
            _items.RemoveAt(0);
        }
        _items.Add(it);
    }

    public override void _Process(double delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        float dt = (float)delta;
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            FxItem it = _items[i];
            it.T += dt;
            if (it.T >= it.Dur)
            {
                // 投射物飞完不是直接消失，而是在目标点带出相性命中形。
                // 这样调用方只放一次投射物，"飞→爆"两拍自然衔接，不用手动对齐时间。
                if (it.Kind == FxKind.Bolt)
                {
                    SpawnImpact(it);
                }
                _items.RemoveAt(i);
                continue;
            }
            _items[i] = it;
        }

        QueueRedraw();
        if (_glow != null)
        {
            _glow.QueueRedraw();
        }
    }

    // ============ v0.2：三段式攻击 ============

    /// <summary>
    /// 投射物：从释放者锚点 a 飞到目标锚点 b，形态/轨迹/时长由相性决定。
    /// 到达后自动播放对应命中形（见 _Process 的消亡联动）。
    /// </summary>
    public void PlayBolt(Category cat, Vector2 a, Vector2 b, float power)
    {
        float dist = a.DistanceTo(b);

        // 暗器三连发：用负 T 当延迟，比开定时器省一层状态
        int shots = 1;
        if (cat == Category.Hidden)
        {
            shots = 3;
        }

        for (int k = 0; k < shots; k++)
        {
            FxItem it = new FxItem();
            it.Kind = FxKind.Bolt;
            it.Cat = cat;
            it.A = a;
            it.B = b;
            it.C = CategoryColor(cat);
            it.T = -k * 0.03f;
            it.Dur = BoltDuration(cat, dist);
            it.Power = Mathf.Clamp(power, 0f, 1f);
            it.Seed = (int)(a.X * 31f + b.Y * 17f + k * 13f);
            Push(it);
        }
    }

    /// <summary>起手：释放者卡面一小团相性色亮起即收。只做"谁出手了"的指认，不做大动作。</summary>
    public void PlayCast(Category cat, Vector2 pos)
    {
        FxItem it = new FxItem();
        it.Kind = FxKind.Cast;
        it.Cat = cat;
        it.A = pos;
        it.B = pos;
        it.C = CategoryColor(cat);
        it.T = 0f;
        it.Dur = 0.08f;
        it.Power = 0.5f;
        it.Seed = (int)(pos.X * 7f + pos.Y * 3f);
        Push(it);
    }

    void SpawnImpact(FxItem bolt)
    {
        FxItem it = new FxItem();
        it.Kind = FxKind.Impact;
        it.Cat = bolt.Cat;
        it.A = bolt.B;          // 命中形长在目标点上
        it.B = bolt.B;
        it.C = bolt.C;
        it.T = 0f;
        it.Dur = ImpactDuration(bolt.Cat);
        it.Power = bolt.Power;
        it.Seed = bolt.Seed;
        Push(it);
    }

    void Push(FxItem it)
    {
        if (_items.Count >= MaxItems)
        {
            _items.RemoveAt(0);
        }
        _items.Add(it);
    }

    /// <summary>
    /// 像素特效整体放大倍率（2026-11-16）。原 64px 精灵表在 1920 画面里偏小，
    /// 放大后用最近邻过滤保持像素锐利，见 _Ready。
    /// </summary>
    public const float FxPixelScale = 1.6f;

    // 相性节奏表之外的两个全局倍率，只调这里，不用改 13 相性各自的表。
    // 2026-11-15 首次放慢到 1.45；2026-11-16 用户反馈「投射物飞行过快」再提到 2.0。
    // 配套：BoltPos 的缓动同时由 ease-out 改成匀速，见 BattleFxProjectile.cs。
    const float BoltTimeScale = 2.0f;
    const float ImpactTimeScale = 1.35f;

    /// <summary>
    /// 投射物绘制尺寸（像素）。**核心层与发光源层必须都调这里**，
    /// 两边各写一遍公式很容易只改一处，结果辉光和主体错位。
    ///
    /// 暗器比其它相性小一圈（0.55）：暗器本来就是「小而快」的东西，
    /// 而且它的源素材只有 16×16，按常规尺寸画会被放大 5 倍、糊成一块。
    /// 缩小后既贴语义，又让源像素接近 1:1。
    /// </summary>
    internal static float BoltSize(FxItem it)
    {
        float size = (46f + it.Power * 14f) * FxPixelScale;
        if (it.Cat == Category.Hidden)
        {
            size *= 0.55f;
        }
        return size;
    }

    /// <summary>
    /// 投射物从 a 飞到 b 需要多少**真实秒**（2026-11-17）。
    ///
    /// 用途：伤害飘字要等弹到了再出（之前是 OnDamage 当拍就出，数字比命中先到，
    /// 读起来像「隔空扣血」）。飞行时长只由相性与距离决定、不吃战斗速度
    /// （特效层用的是真实 delta），所以调用方直接用这个值做延迟即可。
    /// </summary>
    public static float BoltFlightTime(Category cat, Vector2 a, Vector2 b)
    {
        return BoltDuration(cat, a.DistanceTo(b));
    }

    /// <summary>飞行时长 = 相性基准 × 距离微增 × 全局放慢。近身不闪没、远距不拖沓。</summary>
    static float BoltDuration(Category cat, float dist)
    {
        float baseDur = 0.18f;
        switch (cat)
        {
            case Category.Hidden:
                baseDur = 0.12f;
                break;
            case Category.Sword:
            case Category.Leg:
            case Category.Beast:
            case Category.Lightness:
                baseDur = 0.16f;
                break;
            case Category.Saber:
                baseDur = 0.18f;
                break;
            case Category.Fist:
                baseDur = 0.20f;
                break;
            case Category.Whip:
                baseDur = 0.22f;
                break;
            case Category.Palm:
            case Category.Qimen:
                baseDur = 0.24f;
                break;
            case Category.Poison:
                baseDur = 0.26f;
                break;
            case Category.Medicine:
            case Category.Zen:
                baseDur = 0.30f;
                break;
        }

        float k = Mathf.Clamp(dist / 420f, 0.85f, 1.35f);
        return baseDur * k * BoltTimeScale;
    }

    static float ImpactDuration(Category cat)
    {
        float baseDur = 0.22f;
        switch (cat)
        {
            case Category.Hidden:
                baseDur = 0.14f;
                break;
            case Category.Sword:
            case Category.Leg:
            case Category.Beast:
            case Category.Lightness:
                baseDur = 0.20f;
                break;
            case Category.Saber:
            case Category.Whip:
                baseDur = 0.24f;
                break;
            case Category.Fist:
                baseDur = 0.26f;
                break;
            case Category.Palm:
            case Category.Qimen:
                baseDur = 0.28f;
                break;
            case Category.Poison:
                baseDur = 0.30f;
                break;
            case Category.Medicine:
            case Category.Zen:
                baseDur = 0.32f;
                break;
        }
        return baseDur * ImpactTimeScale;
    }

    // ============ 序列帧绘制（像素特效包） ============

    /// <summary>
    /// 按进度从横向精灵表里取一帧画到指定位置。
    /// 帧数由纹理宽度推出（生成时固定 64px/帧），不依赖外部元数据。
    /// </summary>
    void DrawSheetFrame(Texture2D sheet, float p, Vector2 center, float size, float alpha)
    {
        int frames = sheet.GetWidth() / FxPixel.FrameSize;
        if (frames < 1)
        {
            frames = 1;
        }

        int idx = (int)(p * frames);
        if (idx >= frames)
        {
            idx = frames - 1;
        }
        if (idx < 0)
        {
            idx = 0;
        }

        Rect2 src = new Rect2(idx * FxPixel.FrameSize, 0f, FxPixel.FrameSize, FxPixel.FrameSize);
        Rect2 dst = new Rect2(center.X - size * 0.5f, center.Y - size * 0.5f, size, size);
        DrawTextureRectRegion(sheet, dst, src, new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>相性色。只作辅助信息 —— 形状才是识别依据（挂机扫视来不及辨色）。</summary>
    public static Color CategoryColor(Category cat)
    {
        switch (cat)
        {
            case Category.Sword: return new Color(0.812f, 0.902f, 0.949f);
            case Category.Saber: return new Color(0.949f, 0.706f, 0.353f);
            case Category.Fist: return new Color(0.847f, 0.655f, 0.369f);
            case Category.Palm: return new Color(0.498f, 0.769f, 0.910f);
            case Category.Leg: return new Color(0.847f, 0.867f, 0.886f);
            case Category.Whip: return new Color(0.769f, 0.416f, 0.416f);
            case Category.Hidden: return new Color(0.659f, 0.722f, 0.753f);
            case Category.Lightness: return new Color(0.886f, 0.910f, 0.925f);
            case Category.Medicine: return new Color(0.561f, 0.847f, 0.659f);
            case Category.Poison: return new Color(0.498f, 0.659f, 0.353f);
            case Category.Qimen: return new Color(0.753f, 0.627f, 0.878f);
            case Category.Zen: return new Color(0.910f, 0.847f, 0.659f);
            case Category.Beast: return new Color(0.753f, 0.541f, 0.416f);
        }
        return new Color(1f, 0.78f, 0.42f);
    }

    public override void _Draw()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            FxItem it = _items[i];
            float p = it.T / it.Dur;          // 0→1 线性进度
            if (p < 0f)
            {
                continue;                     // 负 T 是延迟（暗器连发 / 多段错峰），还没到出场时间
            }
            switch (it.Kind)
            {
                case FxKind.Slash:
                    DrawSlash(it, p);
                    break;
                case FxKind.Ring:
                    DrawRing(it, p);
                    break;
                case FxKind.Crack:
                    DrawCrack(it, p);
                    break;
                case FxKind.Spark:
                    DrawSpark(it, p);
                    break;
                case FxKind.Death:
                    DrawDeath(it, p);
                    break;
                case FxKind.Bubble:
                    DrawBubble(it, p);
                    break;
                case FxKind.Ember:
                    DrawEmber(it, p);
                    break;
                case FxKind.Miss:
                    DrawMiss(it, p);
                    break;
                case FxKind.Bolt:
                    DrawBolt(it, p);
                    break;
                case FxKind.Cast:
                    DrawCast(it, p);
                    break;
                case FxKind.Impact:
                    DrawImpact(it, p);
                    break;
            }
        }
    }

    // ============ 各种画法 ============

    /// <summary>
    /// 斩线：从攻击者向目标"扫"过去 —— 线段长度随进度增长（前 45% 长满），
    /// 之后整条淡出。外层宽线给"刀风"，内层亮线给"刃口"。
    /// </summary>
    void DrawSlash(FxItem it, float p)
    {
        float grow = Mathf.Min(1f, p / 0.45f);
        grow = 1f - (1f - grow) * (1f - grow);          // ease-out：出手快、收尾慢

        Vector2 dir = it.B - it.A;
        Vector2 head = it.A + dir * grow;

        float alpha = 1f;
        if (p > 0.45f)
        {
            alpha = 1f - (p - 0.45f) / 0.55f;
        }

        float wide = 6f + it.Power * 6f;
        Color outer = it.C;
        outer.A = 0.34f * alpha;
        DrawLine(it.A, head, outer, wide, true);

        Color core = new Color(1f, 0.97f, 0.90f, 0.92f * alpha);
        DrawLine(it.A, head, core, 3f, true);

        // 刀尖高光：跟着头部走的小亮点，让"扫过去"有方向感
        if (grow < 1f)
        {
            Color tip = new Color(1f, 1f, 1f, 0.9f * alpha);
            DrawCircle(head, 2.5f + it.Power * 2f, tip);
        }
    }

    /// <summary>内功环：三圈同心圆向外扩，越外越淡（节奏比斩线慢，这是内功的"余韵"）。</summary>
    void DrawRing(FxItem it, float p)
    {
        float r0 = 6f;
        float r1 = 34f + it.Power * 22f;

        for (int k = 0; k < 3; k++)
        {
            float kp = p - k * 0.12f;
            if (kp <= 0f || kp >= 1f)
            {
                continue;
            }
            float r = Mathf.Lerp(r0, r1, kp);
            Color c = it.C;
            c.A = 0.55f * (1f - kp) * (1f - k * 0.25f);
            DrawArc(it.A, r, 0f, Mathf.Tau, 28, c, 2f, true);
        }
    }

    /// <summary>真伤裂：竖直撕开的折线 + 一次白闪（白闪由调用方给一张纯白 Crack 实现）。</summary>
    void DrawCrack(FxItem it, float p)
    {
        float open = Mathf.Min(1f, p / 0.3f);
        float len = (24f + it.Power * 26f) * open;
        float alpha = 1f - Mathf.Max(0f, (p - 0.3f) / 0.7f);

        Color c = it.C;
        c.A = 0.9f * alpha;
        Vector2 top = it.A + new Vector2(0f, -len * 0.5f);
        Vector2 mid = it.A + new Vector2(2.5f, 0f);
        Vector2 bot = it.A + new Vector2(-1.5f, len * 0.5f);
        DrawPolyline(new Vector2[] { top, mid, bot }, c, 2f, true);

        Color halo = new Color(c.R, c.G, c.B, c.A * 0.35f);
        DrawPolyline(new Vector2[] { top, mid, bot }, halo, 6f, true);
    }

    /// <summary>命中火花：4 条短射线向外，角度由固定种子决定（同一场重播朝向一致）。</summary>
    void DrawSpark(FxItem it, float p)
    {
        float alpha = 1f - p;
        int n = 3 + (int)(it.Power * 3f);
        float baseAng = (it.Seed % 360) * Mathf.Pi / 180f;

        for (int k = 0; k < n; k++)
        {
            float ang = baseAng + k * (Mathf.Tau / n) + (it.Seed % 7) * 0.1f;
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float len = (8f + it.Power * 8f) * (0.5f + p);
            Color c = new Color(1f, 0.95f, 0.85f, 0.85f * alpha);
            DrawLine(it.A + d * 2f, it.A + d * len, c, 2f, true);
        }
    }

    /// <summary>死亡碎片：8 条线从卡中心向四周散开并淡出，越长越"重"。</summary>
    void DrawDeath(FxItem it, float p)
    {
        float alpha = 1f - p;
        float baseAng = (it.Seed % 360) * Mathf.Pi / 180f;

        for (int k = 0; k < 8; k++)
        {
            float ang = baseAng + k * (Mathf.Tau / 8f) + (k % 3) * 0.12f;
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float r0 = 10f + p * 18f;
            float r1 = r0 + 14f * (0.6f + it.Power * 0.6f);
            Color c = it.C;
            c.A = 0.55f * alpha;
            DrawLine(it.A + d * r0, it.A + d * r1, c, 2f, true);
        }
    }

    /// <summary>
    /// 毒泡：两三个空心小圆从锚点往上飘并淡出。
    /// 毒是"从里往外渗"，所以往上、慢、不成线 —— 与斩线的"横扫"、火星的"上窜"区分开。
    /// </summary>
    void DrawBubble(FxItem it, float p)
    {
        float alpha = 1f - p;
        int n = 2 + (int)(it.Power * 2f);
        float rise = 18f + it.Power * 14f;

        for (int k = 0; k < n; k++)
        {
            float kp = Mathf.Clamp(p - k * 0.14f, 0f, 1f);
            float x = (k - (n - 1) * 0.5f) * 9f;
            Vector2 pos = it.A + new Vector2(x, -kp * rise);
            float r = 2.6f + it.Power * 1.8f - kp * 1.1f;
            if (r <= 0.4f)
            {
                continue;
            }
            Color c = it.C;
            c.A = 0.72f * (1f - kp) * alpha;
            DrawArc(pos, r, 0f, Mathf.Tau, 12, c, 1.6f, true);
        }
    }

    /// <summary>火星：几道向上的短线，越往上越淡（火是"往上窜"，节奏比毒快）。</summary>
    void DrawEmber(FxItem it, float p)
    {
        float alpha = 1f - p;
        int n = 3 + (int)(it.Power * 3f);

        for (int k = 0; k < n; k++)
        {
            float ang = -Mathf.Pi * 0.5f + (k - (n - 1) * 0.5f) * 0.30f;
            Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float len = (11f + it.Power * 12f) * (0.45f + p);
            Vector2 from = it.A + d * (4f + p * 11f);
            Color c = it.C;
            c.A = 0.8f * alpha * (1f - k * 0.10f);
            DrawLine(from, from + d * len, c, 2f, true);
        }
    }

    /// <summary>
    /// 闪避：一道从锚点外侧掠过的短弧 + 一道淡残影，**不画火花** —— 没打中就不该炸。
    /// 弧线起点偏上再往下扫，读起来是"人往旁边让了一下"。
    /// </summary>
    void DrawMiss(FxItem it, float p)
    {
        float alpha = 1f - p;
        Vector2 center = it.A + new Vector2(0f, 6f);
        float r = 22f + it.Power * 8f;
        float a0 = -0.9f + p * 0.55f;

        Color main = it.C;
        main.A = 0.58f * alpha;
        DrawArc(center, r, a0, a0 + 1.45f, 14, main, 2f, true);

        Color trail = it.C;
        trail.A = 0.24f * alpha;
        DrawArc(center, r + 5f, a0 - 0.18f, a0 + 1.15f, 14, trail, 1.4f, true);
    }
}
