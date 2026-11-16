// 文件：BattleFxProjectile.cs
// 《轮回大侠》战斗特效 —— 投射物（v0.2 "飞"这一拍）
//
// 职责：把"释放者 → 目标"这段轨迹画出来。
//   · 6 个母形覆盖 13 种相性：梭 / 团 / 针 / 波 / 爪 / 环
//   · 轨迹只有三种：直线（默认）、抛物线（毒）、波形（鞭），外加奇门的自旋
//   · 拖尾一律 ≤40px —— 拖尾是从当前位置向后的一小段，
//     不是从起点拉到当前位置的长线（v0.1 已否决那种"通栏光缆"）
//
// 命中形不在这里：投射物飞完由 BattleFxLayer 自动生成 Impact，见 BattleFxImpact.cs。

using Godot;

public partial class BattleFxLayer
{
    /// <summary>
    /// 飞行进度：匀速。旧版用 1-(1-p)^2 的 ease-out，前半段时间就飞完 75% 距离，
    /// 一出手就窜到目标附近、后半段慢慢蹭，实机读起来像瞬移（用户反馈「飞行过快」）。
    /// 改匀速后整段轨迹都看得清；到点前的收尾交给 DrawBolt 的 alpha 淡出。
    /// </summary>
    static float FlightProgress(float p)
    {
        return Mathf.Clamp(p, 0f, 1f);
    }

    /// <summary>求投射物在进度 p 时的位置。毒走抛物线、鞭走波形，其余直线。</summary>
    internal static Vector2 BoltPos(FxItem it, float p)
    {
        float e = FlightProgress(p);
        Vector2 pos = it.A + (it.B - it.A) * e;

        if (it.Cat == Category.Poison)
        {
            float dist = it.A.DistanceTo(it.B);
            float h = Mathf.Min(60f, dist * 0.18f);
            pos += new Vector2(0f, -1f) * Mathf.Sin(p * Mathf.Pi) * h;
        }
        else if (it.Cat == Category.Whip)
        {
            Vector2 dir = (it.B - it.A).Normalized();
            Vector2 n = new Vector2(-dir.Y, dir.X);
            pos += n * Mathf.Sin(p * Mathf.Pi * 2f) * 8f;
        }

        return pos;
    }

    /// <summary>飞行方向。鞭与毒有偏移，取末端切线会比直线方向更准，但直线方向已足够读。</summary>
    internal static Vector2 BoltDir(FxItem it)
    {
        Vector2 d = it.B - it.A;
        if (d.Length() < 0.01f)
        {
            return new Vector2(1f, 0f);
        }
        return d.Normalized();
    }

    void DrawBolt(FxItem it, float p)
    {
        // 像素序列帧优先：飞行过程中特效本身也在动，不是一张静图平移
        Texture2D sheet = FxPixel.Bolt(it.Cat);
        if (sheet != null)
        {
            Vector2 pp = BoltPos(it, p);
            float pa = 1f;
            if (p > 0.85f)
            {
                pa = 1f - (p - 0.85f) / 0.15f;
            }
            float ps = (46f + it.Power * 14f) * FxPixelScale;
            DrawSheetFrame(sheet, p, pp, ps, pa);
            return;
        }

        // 表情贴图已在 2026-11-16 由像素特效包整体替换，此处直接回退到程序化几何，
        // 保证某张精灵表缺失时仍有兜底、不会白屏。
        Vector2 pos = BoltPos(it, p);
        Vector2 dir = BoltDir(it);
        float alpha = 1f;
        if (p > 0.85f)
        {
            alpha = 1f - (p - 0.85f) / 0.15f;   // 到位前略收，避免硬切到命中形
        }

        switch (it.Cat)
        {
            case Category.Hidden:
                DrawNeedle(it, pos, dir, alpha);
                break;
            case Category.Saber:
                DrawSaberBolt(it, pos, dir, alpha);
                break;
            case Category.Fist:
                DrawFistBolt(it, pos, dir, p, alpha);
                break;
            case Category.Palm:
                DrawPalmBolt(it, pos, dir, p, alpha);
                break;
            case Category.Poison:
                DrawPoisonBolt(it, pos, dir, alpha);
                break;
            case Category.Qimen:
                DrawQimenBolt(it, pos, p, alpha);
                break;
            case Category.Zen:
                DrawZenBolt(it, pos, alpha);
                break;
            case Category.Whip:
                DrawWhipBolt(it, pos, dir, alpha);
                break;
            case Category.Beast:
                DrawClawBolt(it, pos, dir, alpha);
                break;
            default:
                // 剑 / 刀以外的梭形家族：剑、腿、轻功、医
                DrawDartBolt(it, pos, dir, alpha);
                break;
        }
    }

    /// <summary>梭形：细长菱形弹体 + 短光尾。剑最利、腿略短、轻功最细。</summary>
    void DrawDartBolt(FxItem it, Vector2 pos, Vector2 dir, float alpha)
    {
        float len = 18f + it.Power * 8f;
        float wide = 4f;
        float tail = 26f;

        if (it.Cat == Category.Leg)
        {
            len = 16f;
            wide = 4f;
        }
        else if (it.Cat == Category.Lightness)
        {
            len = 20f;
            wide = 3f;
        }
        else if (it.Cat == Category.Medicine)
        {
            len = 12f;
            wide = 6f;
            tail = 18f;
        }

        Vector2 n = new Vector2(-dir.Y, dir.X);
        Vector2 head = pos + dir * (len * 0.5f);
        Vector2 back = pos - dir * (len * 0.5f);

        // 弹体：实心菱形，亮核在外层淡色光晕里
        Vector2[] body = new Vector2[]
        {
            head,
            pos + n * (wide * 0.5f),
            back,
            pos - n * (wide * 0.5f),
        };

        Color halo = it.C;
        halo.A = 0.30f * alpha;
        DrawCircle(pos, wide * 1.6f, halo);

        Color core = it.C;
        core.A = 0.95f * alpha;
        DrawPolygon(body, new Color[] { core });

        // 拖尾：向后一小段，外宽内窄
        Vector2 tailEnd = back - dir * tail;
        Color t1 = it.C;
        t1.A = 0.34f * alpha;
        DrawLine(back, tailEnd, t1, wide * 0.9f, true);

        Color t2 = new Color(1f, 1f, 1f, 0.55f * alpha);
        DrawLine(back, back - dir * (tail * 0.55f), t2, 1.6f, true);
    }

    /// <summary>刀芒：新月弧片。刀与剑的区别必须在形状上读得出来 —— 刀是"面"，剑是"线"。</summary>
    void DrawSaberBolt(FxItem it, Vector2 pos, Vector2 dir, float alpha)
    {
        float ang = dir.Angle();
        float r = 11f + it.Power * 4f;

        Color c = it.C;
        c.A = 0.9f * alpha;
        // 弧面迎着飞行方向张开
        DrawArc(pos, r, ang - 0.9f, ang + 0.9f, 14, c, 4.5f, true);

        Color inner = new Color(1f, 0.97f, 0.90f, 0.8f * alpha);
        DrawArc(pos, r - 3f, ang - 0.7f, ang + 0.7f, 12, inner, 2f, true);

        Vector2 back = pos - dir * 12f;
        Color t = it.C;
        t.A = 0.32f * alpha;
        DrawLine(back, back - dir * 20f, t, 5f, true);
    }

    /// <summary>拳风：旋转的短粗方块 + 双尾，钝器感。</summary>
    void DrawFistBolt(FxItem it, Vector2 pos, Vector2 dir, float p, float alpha)
    {
        float size = 8f + it.Power * 4f;
        float rot = p * 2.2f;
        Vector2 n = new Vector2(-dir.Y, dir.X);

        Color c = it.C;
        c.A = 0.92f * alpha;

        // 用旋转后的四个角点构造方块
        Vector2[] quad = new Vector2[4];
        for (int k = 0; k < 4; k++)
        {
            float a = rot + k * Mathf.Pi * 0.5f + Mathf.Pi * 0.25f;
            Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size * 0.72f;
            quad[k] = pos + off;
        }
        DrawPolygon(quad, new Color[] { c });

        Color t = it.C;
        t.A = 0.30f * alpha;
        DrawLine(pos - dir * 6f + n * 3f, pos - dir * 26f + n * 3f, t, 2.5f, true);
        DrawLine(pos - dir * 6f - n * 3f, pos - dir * 26f - n * 3f, t, 2.5f, true);
    }

    /// <summary>掌印：圆团 + 外环，外环随飞行微微撑开（内功的"鼓荡"）。</summary>
    void DrawPalmBolt(FxItem it, Vector2 pos, Vector2 dir, float p, float alpha)
    {
        float r = 7f + it.Power * 3f;

        Color core = it.C;
        core.A = 0.85f * alpha;
        DrawCircle(pos, r, core);

        Color ring = it.C;
        ring.A = 0.55f * alpha;
        DrawArc(pos, r + 4f + p * 3f, 0f, Mathf.Tau, 20, ring, 1.8f, true);

        Color t = it.C;
        t.A = 0.28f * alpha;
        DrawLine(pos - dir * 8f, pos - dir * 30f, t, 6f, true);
    }

    /// <summary>毒砂：小绿团 + 周围三粒散点，抛物线飞行。</summary>
    void DrawPoisonBolt(FxItem it, Vector2 pos, Vector2 dir, float alpha)
    {
        Color c = it.C;
        c.A = 0.80f * alpha;
        DrawCircle(pos, 6f + it.Power * 2f, c);

        for (int k = 0; k < 3; k++)
        {
            float a = (it.Seed % 360) * Mathf.Pi / 180f + k * 2.1f;
            Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 9f;
            Color d = it.C;
            d.A = 0.5f * alpha;
            DrawCircle(pos + off, 2f, d);
        }

        Color t = it.C;
        t.A = 0.26f * alpha;
        DrawLine(pos - dir * 6f, pos - dir * 22f, t, 5f, true);
    }

    /// <summary>奇门：自旋方片。唯一"边飞边转"的投射物。</summary>
    void DrawQimenBolt(FxItem it, Vector2 pos, float p, float alpha)
    {
        float rot = p * Mathf.Pi * 2f;
        float size = 8f;

        Vector2[] quad = new Vector2[4];
        for (int k = 0; k < 4; k++)
        {
            float a = rot + k * Mathf.Pi * 0.5f;
            quad[k] = pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size;
        }

        Color c = it.C;
        c.A = 0.85f * alpha;
        DrawPolyline(new Vector2[] { quad[0], quad[1], quad[2], quad[3], quad[0] }, c, 2f, true);

        Color core = new Color(1f, 1f, 1f, 0.7f * alpha);
        DrawCircle(pos, 2f, core);
    }

    /// <summary>禅：空心圆环，不急不缓。</summary>
    void DrawZenBolt(FxItem it, Vector2 pos, float alpha)
    {
        Color c = it.C;
        c.A = 0.8f * alpha;
        DrawArc(pos, 8f + it.Power * 3f, 0f, Mathf.Tau, 22, c, 2.4f, true);

        Color inner = it.C;
        inner.A = 0.35f * alpha;
        DrawCircle(pos, 3f, inner);
    }

    /// <summary>鞭梢：一小段正弦波，读作"甩过去"。</summary>
    void DrawWhipBolt(FxItem it, Vector2 pos, Vector2 dir, float alpha)
    {
        Vector2 n = new Vector2(-dir.Y, dir.X);
        int seg = 10;
        Vector2[] pts = new Vector2[seg + 1];

        for (int k = 0; k <= seg; k++)
        {
            float f = (float)k / seg;
            float along = -f * 30f;
            float wave = Mathf.Sin(f * Mathf.Pi * 2f) * 4f;
            pts[k] = pos + dir * along + n * wave;
        }

        Color c = it.C;
        c.A = 0.85f * alpha;
        DrawPolyline(pts, c, 2.4f, true);
    }

    /// <summary>爪影：三道平行短斜线，沿飞行方向排开。</summary>
    void DrawClawBolt(FxItem it, Vector2 pos, Vector2 dir, float alpha)
    {
        Vector2 n = new Vector2(-dir.Y, dir.X);

        for (int k = -1; k <= 1; k++)
        {
            Vector2 center = pos + n * (k * 4.5f);
            Color c = it.C;
            c.A = 0.85f * alpha;
            DrawLine(center - dir * 6f - n * 2f, center + dir * 6f + n * 2f, c, 2f, true);
        }
    }

    /// <summary>暗器飞针：极细一点 + 长细尾。最快的投射物，靠"细"和"快"区分。</summary>
    void DrawNeedle(FxItem it, Vector2 pos, Vector2 dir, float alpha)
    {
        Color core = new Color(1f, 1f, 1f, 0.95f * alpha);
        DrawCircle(pos, 2f, core);

        Color t = it.C;
        t.A = 0.6f * alpha;
        DrawLine(pos - dir * 3f, pos - dir * 34f, t, 1.4f, true);
    }

    /// <summary>起手：释放者卡面一小团相性色先涨后收。只指认"谁出手了"，不抢戏。</summary>
    void DrawCast(FxItem it, float p)
    {
        // 起手固定用相性色小闪光，不放表情：一张黄脸在"谁出手了"这一拍太抢戏，
        // 也容易和飞行中的拳头/手掌混淆。
        float swell = Mathf.Sin(p * Mathf.Pi);   // 0→1→0
        float r = 4f + swell * 9f;

        Color c = it.C;
        c.A = 0.55f * (1f - p);
        DrawCircle(it.A, r, c);

        Color ring = it.C;
        ring.A = 0.7f * (1f - p);
        DrawArc(it.A, r + 3f, 0f, Mathf.Tau, 18, ring, 1.6f, true);
    }
}
