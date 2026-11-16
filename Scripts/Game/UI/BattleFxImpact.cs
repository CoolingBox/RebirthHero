// 文件：BattleFxImpact.cs
// 《轮回大侠》战斗特效 —— 命中形（v0.2 "落"这一拍）
//
// 与 v0.1 的区别：v0.1 的命中是"一根线 + 几条射线"，剑和刀、掌和拳看上去是同一件事。
// 这里每种相性都有**形状层次**（外圈 / 中形 / 内核），并遵循各自的物理直觉：
//   剑 = 交叉（双刃）　刀 = 弧面（阔）　拳 = 钝击（碎块）　掌 = 扩散（余波）
//   腿 = 横扫　鞭 = 波痕　暗器 = 针孔　医 = 收拢　毒 = 弥散　奇门 = 阵印　禅 = 钟鸣　兽 = 撕爪
//
// 半径一律 ≤56px：卡底 84px 是血条/名字信息带，特效不许进去。

using Godot;

public partial class BattleFxLayer
{
    void DrawImpact(FxItem it, float p)
    {
        // 像素序列帧优先：命中播完整动画，比单张表情有层次
        Texture2D sheet = FxPixel.Impact(it.Cat);
        if (sheet != null)
        {
            float ss = (48f + it.Power * 16f) * (0.80f + p * 0.40f) * FxPixelScale;
            DrawSheetFrame(sheet, p, it.A, ss, 1f - p * 0.55f);
            return;
        }

        // 表情贴图已由像素特效包整体替换，缺失时回退到程序化几何，不白屏。
        switch (it.Cat)
        {
            case Category.Sword:
                ImpactSword(it, p);
                break;
            case Category.Saber:
                ImpactSaber(it, p);
                break;
            case Category.Fist:
                ImpactFist(it, p);
                break;
            case Category.Palm:
                ImpactPalm(it, p);
                break;
            case Category.Leg:
                ImpactLeg(it, p);
                break;
            case Category.Whip:
                ImpactWhip(it, p);
                break;
            case Category.Hidden:
                ImpactNeedle(it, p);
                break;
            case Category.Medicine:
                ImpactMedicine(it, p);
                break;
            case Category.Poison:
                ImpactPoison(it, p);
                break;
            case Category.Qimen:
                ImpactQimen(it, p);
                break;
            case Category.Zen:
                ImpactZen(it, p);
                break;
            case Category.Beast:
                ImpactBeast(it, p);
                break;
            default:
                ImpactLightness(it, p);
                break;
        }
    }

    /// <summary>剑：交叉剑痕。主痕先落、副痕迟 20% 进度补上，外带细芒 —— 双刃感来自"两道"，不是加粗。</summary>
    void ImpactSword(FxItem it, float p)
    {
        float len = 34f + it.Power * 20f;
        float alpha = 1f - p;

        // 主痕：左上 → 右下
        DrawBlade(it.A, -0.78f, len, it.C, alpha, 4.5f);

        // 副痕：右上 → 左下，晚一拍
        if (p > 0.18f)
        {
            float sub = (p - 0.18f) / 0.82f;
            DrawBlade(it.A, 0.62f, len * 0.8f, it.C, 1f - sub, 3f);
        }

        // 细芒：交叉点向外四条
        for (int k = 0; k < 4; k++)
        {
            float a = k * Mathf.Pi * 0.5f + 0.4f;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Color c = it.C;
            c.A = 0.45f * alpha;
            DrawLine(it.A + d * 6f, it.A + d * (12f + p * 12f), c, 1.4f, true);
        }
    }

    /// <summary>一道剑痕：外宽淡色当"剑风"，内窄亮色当"刃口"。</summary>
    void DrawBlade(Vector2 center, float ang, float len, Color c, float alpha, float wide)
    {
        Vector2 d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
        Vector2 a = center - d * (len * 0.5f);
        Vector2 b = center + d * (len * 0.5f);

        Color outer = c;
        outer.A = 0.32f * alpha;
        DrawLine(a, b, outer, wide * 2f, true);

        Color core = new Color(1f, 1f, 1f, 0.9f * alpha);
        DrawLine(a, b, core, wide * 0.45f, true);
    }

    /// <summary>刀：弧面斩。宽弧带扫过卡面，弧尾甩出碎屑 —— 刀是"面"不是"线"。</summary>
    void ImpactSaber(FxItem it, float p)
    {
        float r = 26f + it.Power * 12f;
        float alpha = 1f - p;
        float start = -1.1f + p * 1.4f;

        Color band = it.C;
        band.A = 0.55f * alpha;
        DrawArc(it.A, r, start, start + 1.5f, 18, band, 8f, true);

        Color edge = new Color(1f, 0.96f, 0.88f, 0.85f * alpha);
        DrawArc(it.A, r + 4f, start, start + 1.2f, 16, edge, 2f, true);

        for (int k = 0; k < 3; k++)
        {
            float a = start + 1.5f + k * 0.22f;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Color c = it.C;
            c.A = 0.6f * alpha;
            Vector2 from = it.A + d * r;
            DrawLine(from, from + d * (6f + p * 10f), c, 2f, true);
        }
    }

    /// <summary>拳：钝击。实心核迅速塌缩，碎块向外迸，配一圈低亮冲击环 —— 重而短。</summary>
    void ImpactFist(FxItem it, float p)
    {
        float alpha = 1f - p;

        Color core = new Color(1f, 0.98f, 0.92f, 0.9f * alpha);
        DrawCircle(it.A, Mathf.Max(0.5f, 9f * (1f - p)), core);

        // 碎块：5 个小方块向外飞，角度固定种子（播片可复现）
        for (int k = 0; k < 5; k++)
        {
            float a = (it.Seed % 360) * Mathf.Pi / 180f + k * 1.256f;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 pos = it.A + d * (6f + p * 26f);
            Color c = it.C;
            c.A = 0.8f * alpha;
            float s = 3f + it.Power * 2f;
            DrawRect(new Rect2(pos - new Vector2(s * 0.5f, s * 0.5f), new Vector2(s, s)), c);
        }

        Color ring = it.C;
        ring.A = 0.4f * alpha;
        DrawArc(it.A, 10f + p * 30f, 0f, Mathf.Tau, 22, ring, 2f, true);
    }

    /// <summary>掌：震掌。中心先"压陷"一小块，再三圈扩散，末圈最淡是余波。</summary>
    void ImpactPalm(FxItem it, float p)
    {
        float alpha = 1f - p;

        Color dent = it.C;
        dent.A = 0.5f * alpha;
        DrawCircle(it.A, 6f * (1f - p * 0.5f), dent);

        for (int k = 0; k < 3; k++)
        {
            float kp = p - k * 0.14f;
            if (kp <= 0f)
            {
                continue;
            }
            float r = 8f + kp * (34f + it.Power * 14f);
            Color c = it.C;
            c.A = 0.6f * (1f - kp) * (1f - k * 0.22f);
            DrawArc(it.A, r, 0f, Mathf.Tau, 26, c, 2.4f - k * 0.4f, true);
        }
    }

    /// <summary>腿：横扫。一条横向宽带 + 两道递减残影。</summary>
    void ImpactLeg(FxItem it, float p)
    {
        float alpha = 1f - p;
        float len = 52f + it.Power * 16f;
        float shift = (p - 0.5f) * 20f;

        for (int k = 0; k < 3; k++)
        {
            Color c = it.C;
            c.A = (0.7f - k * 0.22f) * alpha;
            float y = it.A.Y + k * 4f;
            Vector2 a = new Vector2(it.A.X - len * 0.5f + shift - k * 5f, y);
            Vector2 b = new Vector2(it.A.X + len * 0.5f + shift - k * 5f, y);
            DrawLine(a, b, c, 7f - k * 2f, true);
        }
    }

    /// <summary>鞭：波浪红痕 + 末端回抽的一点。</summary>
    void ImpactWhip(FxItem it, float p)
    {
        float alpha = 1f - p;
        int seg = 14;
        Vector2[] pts = new Vector2[seg + 1];

        for (int k = 0; k <= seg; k++)
        {
            float f = (float)k / seg;
            float x = (f - 0.5f) * 50f;
            float y = Mathf.Sin(f * Mathf.Pi * 2.5f + p * 2f) * 6f;
            pts[k] = it.A + new Vector2(x, y);
        }

        Color c = it.C;
        c.A = 0.8f * alpha;
        DrawPolyline(pts, c, 2.6f, true);

        Color tip = new Color(1f, 0.9f, 0.9f, 0.8f * alpha);
        DrawCircle(it.A + new Vector2(25f, 0f), 2.5f, tip);
    }

    /// <summary>暗器：三个针孔十字 + 极细血线。伤口小，才像暗器。</summary>
    void ImpactNeedle(FxItem it, float p)
    {
        float alpha = 1f - p;

        for (int k = 0; k < 3; k++)
        {
            Vector2 pos = it.A + new Vector2((k - 1) * 7f, (k % 2) * 5f - 2f);
            Color c = new Color(1f, 1f, 1f, 0.85f * alpha);
            DrawLine(pos + new Vector2(-2.5f, 0f), pos + new Vector2(2.5f, 0f), c, 1.4f, true);
            DrawLine(pos + new Vector2(0f, -2.5f), pos + new Vector2(0f, 2.5f), c, 1.4f, true);

            Color blood = it.C;
            blood.A = 0.5f * alpha;
            DrawLine(pos, pos + new Vector2(1f, 6f + p * 6f), blood, 1f, true);
        }
    }

    /// <summary>医：三菱形向心收拢 + 浅绿环。方向与伤害相反（向内），是"补"不是"破"。</summary>
    void ImpactMedicine(FxItem it, float p)
    {
        float alpha = 1f - p;

        for (int k = 0; k < 3; k++)
        {
            float a = k * 2.094f + p * 1.2f;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 pos = it.A + d * (24f * (1f - p));

            Color c = it.C;
            c.A = 0.85f * alpha;
            Vector2[] diamond = new Vector2[]
            {
                pos + new Vector2(0f, -4f),
                pos + new Vector2(3f, 0f),
                pos + new Vector2(0f, 4f),
                pos + new Vector2(-3f, 0f),
            };
            DrawPolygon(diamond, new Color[] { c });
        }

        Color ring = it.C;
        ring.A = 0.5f * alpha;
        DrawArc(it.A, 10f + p * 14f, 0f, Mathf.Tau, 22, ring, 1.8f, true);
    }

    /// <summary>毒：三团绿雾散开 + 两个上浮泡。毒是"渗"，不是"炸"。</summary>
    void ImpactPoison(FxItem it, float p)
    {
        float alpha = 1f - p;

        for (int k = 0; k < 3; k++)
        {
            Vector2 pos = it.A + new Vector2((k - 1) * 10f, 0f);
            Color c = it.C;
            c.A = 0.28f * alpha;
            DrawCircle(pos, 8f + p * 12f, c);
        }

        for (int k = 0; k < 2; k++)
        {
            float kp = Mathf.Clamp(p - k * 0.2f, 0f, 1f);
            Vector2 pos = it.A + new Vector2((k - 0.5f) * 10f, -kp * 22f);
            Color c = it.C;
            c.A = 0.6f * (1f - kp);
            DrawArc(pos, 3f, 0f, Mathf.Tau, 12, c, 1.4f, true);
        }
    }

    /// <summary>奇门：旋转方框 + 四角点。</summary>
    void ImpactQimen(FxItem it, float p)
    {
        float alpha = 1f - p;
        float rot = p * 1.6f;
        float r = 14f + p * 16f;

        Vector2[] quad = new Vector2[4];
        for (int k = 0; k < 4; k++)
        {
            float a = rot + k * Mathf.Pi * 0.5f;
            quad[k] = it.A + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        Color c = it.C;
        c.A = 0.8f * alpha;
        DrawPolyline(new Vector2[] { quad[0], quad[1], quad[2], quad[3], quad[0] }, c, 2f, true);

        for (int k = 0; k < 4; k++)
        {
            Color d = it.C;
            d.A = 0.6f * alpha;
            DrawCircle(quad[k], 2f, d);
        }
    }

    /// <summary>禅：钟鸣。大环缓扩 + 内环金晕，最慢的一档命中。</summary>
    void ImpactZen(FxItem it, float p)
    {
        float alpha = 1f - p;

        Color outer = it.C;
        outer.A = 0.6f * alpha;
        DrawArc(it.A, 10f + p * 42f, 0f, Mathf.Tau, 30, outer, 2.6f, true);

        Color inner = it.C;
        inner.A = 0.4f * alpha;
        DrawArc(it.A, 8f + p * 20f, 0f, Mathf.Tau, 24, inner, 1.8f, true);
    }

    /// <summary>兽：三道平行斜爪痕 + 四个血点。</summary>
    void ImpactBeast(FxItem it, float p)
    {
        float alpha = 1f - p;
        Vector2 d = new Vector2(0.5f, 1f).Normalized();

        for (int k = -1; k <= 1; k++)
        {
            Vector2 center = it.A + new Vector2(k * 8f, 0f);
            Color c = it.C;
            c.A = 0.85f * alpha;
            DrawLine(center - d * 14f, center + d * 14f, c, 2.6f, true);
        }

        for (int k = 0; k < 4; k++)
        {
            float a = (it.Seed % 360) * Mathf.Pi / 180f + k * 1.57f;
            Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (10f + p * 14f);
            Color c = it.C;
            c.A = 0.55f * alpha;
            DrawCircle(it.A + off, 1.8f, c);
        }
    }

    /// <summary>轻功：一条细穿线 + 目标处一片淡残影。</summary>
    void ImpactLightness(FxItem it, float p)
    {
        float alpha = 1f - p;

        Color c = it.C;
        c.A = 0.5f * alpha;
        DrawLine(it.A + new Vector2(-26f, -8f), it.A + new Vector2(26f, 8f), c, 1.6f, true);

        Color ghost = it.C;
        ghost.A = 0.22f * alpha;
        DrawRect(new Rect2(it.A + new Vector2(6f, -14f), new Vector2(10f, 28f)), ghost);
    }
}
