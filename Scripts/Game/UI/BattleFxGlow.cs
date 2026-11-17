// 文件：BattleFxGlow.cs
// 战斗特效的 HDR 发光源层 —— 只把特效像素推到 > 1.0，
// 真正的多尺度辉光由 WorldEnvironment 的引擎原生 Glow 完成（见 GameRoot）。
//
// 2026-11-16 v4 重构（跨引擎调研后，取代 v1~v3 的自研描边辉光）：
//   旧版是单 pass 8 邻域 dilation + blend_add，本质是沿轮廓贴一圈亮边，
//   不是 bloom。Unity URP / Unreal / Godot 的标准管线是
//   亮度阈值提取 → 多尺度降采样金字塔 → 上采样归一化累加 →
//   tonemap 之前用 screen 合成回 HDR 原图。
//   Godot 的 WorldEnvironment Glow 就是这套实现（glow levels 1~7 即多尺度层级），
//   直接用引擎的比自研 SubViewport 链稳。
//
// 选择性不靠 layer mask，靠 HDR：卡面 / UI / 背景的颜色都 <= 1，
//   glow_hdr_threshold = 1.0，只有本层注入过 emissive 的特效像素会超过阈值发光。
//   先验探针实测：HDR 块发光、普通橙块与卡面人物完全不发光。
//
// 为什么仍是独立节点：blend_add 是 CanvasItem 级 render_mode。
//   核心层要 Nearest + Mix（原样、锐利、颜色不变），
//   本层要 Additive 才能只往屏幕里注入 HDR 能量而不覆盖核心。
//
// 边界：主体只画**同尺寸、同帧**的形状，不放大、不膨胀、不描边、不做色散。
//   唯一的几何扩展是投射物后面的拖尾条带（2026-11-17，取代原来的 3 段残影）。
//   13 相性的几何、轨迹、时序仍全在 BattleFxProjectile / BattleFxImpact。
//   关闭整套辉光：注释 GameRoot 的 SetupGlowEnvironment() 即可
//   （没有 > 1 像素时 glow 是空操作）。

using System.Collections.Generic;
using Godot;

public partial class BattleFxGlow : Node2D
{
    /// <summary>
    /// 注入的 HDR 增量。核心层已画 1.0×贴图色，本层再叠 boost×fade×贴图色，
    /// 特效亮部总和约 1 + boost×fade（> 1 才发光），暗部仍 <= 1 不发光，
    /// 等于天然 bright-pass，稀疏半透明像素不会被无差别点亮。
    ///
    /// 2026-11-17 从 0.85 降到 0.40：13 相性换成的投射物素材本身**更亮、面积更大**
    /// （64×32 的能量弹、整颗火球、成片星点），0.85 时亮部总和到 1.6，
    /// 引擎 Glow（threshold 1.0 + Screen）把绿/粉/金全洗成青白 ——
    /// 实测 poison 的亮部从 (204,237,96) 变成 (178,241,254)。
    /// 降到 0.40 后总和约 1.25，只留一层薄光，颜色得以保留。
    /// </summary>
    const float EmissiveBoost = 0.40f;

    BattleFxLayer _owner;

    public void Bind(BattleFxLayer owner)
    {
        _owner = owner;
    }

    public override void _Ready()
    {
        // 与核心层一致：像素放大后保持硬边，不把发光源糊成一团。
        TextureFilter = TextureFilterEnum.Nearest;
        Material = MakeAdditiveMaterial();
        SetProcess(false);
    }

    static ShaderMaterial MakeAdditiveMaterial()
    {
        Shader s = new Shader();
        s.Code = @"
shader_type canvas_item;
render_mode blend_add;

void fragment() {
    // texture() 返回非预乘 RGBA，blend_add 下 COLOR.a 固定 1 不会自动衰减，
    // 所以必须自己乘 t.a，否则半透明边缘会被当成全强度注入。
    vec4 t = texture(TEXTURE, UV);
    COLOR = vec4(t.rgb * t.a * COLOR.rgb, 1.0);
}
";
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = s;
        return mat;
    }

    /// <summary>
    /// 画与核心**同尺寸、同帧**的形状，只往屏幕注入 HDR 能量。
    /// fade 乘进 tint.rgb（不进 alpha），避免 blend_add 下二次衰减。
    /// </summary>
    void DrawEmissive(Texture2D sheet, float p, Vector2 center, float size, float fade)
    {
        if (sheet == null)
        {
            return;
        }

        int frames = sheet.GetWidth() / FxPixel.FrameSize;
        if (frames < 1)
        {
            frames = 1;
        }

        int idx = (int)(p * frames);
        if (idx < 0)
        {
            idx = 0;
        }
        if (idx >= frames)
        {
            idx = frames - 1;
        }

        Rect2 src = new Rect2(idx * FxPixel.FrameSize, 0f,
            FxPixel.FrameSize, FxPixel.FrameSize);
        Rect2 dst = new Rect2(center.X - size * 0.5f, center.Y - size * 0.5f, size, size);
        float gain = EmissiveBoost * fade;
        Color tint = new Color(gain, gain, gain, 1f);
        DrawTextureRectRegion(sheet, dst, src, tint);
    }

    public override void _Draw()
    {
        if (_owner == null)
        {
            return;
        }

        // 浅色主题下 additive 叠到米白背景更容易洗白，注入量收一档。
        bool light = UIThemeManager.IsLightOf(UIThemeManager.Id);
        float themeScale = light ? 0.70f : 1f;

        List<BattleFxLayer.FxItem> items = _owner.Items;
        for (int i = 0; i < items.Count; i++)
        {
            BattleFxLayer.FxItem it = items[i];
            float p = it.T / it.Dur;
            if (p < 0f)
            {
                continue;
            }

            if (it.Kind == BattleFxLayer.FxKind.Bolt)
            {
                DrawBoltEmissive(it, p, themeScale);
                continue;
            }

            if (it.Kind == BattleFxLayer.FxKind.Impact ||
                it.Kind == BattleFxLayer.FxKind.Spark ||
                it.Kind == BattleFxLayer.FxKind.Ring ||
                it.Kind == BattleFxLayer.FxKind.Crack ||
                it.Kind == BattleFxLayer.FxKind.Death)
            {
                Texture2D sheet = FxPixel.Impact(it.Cat);
                float ss = (48f + it.Power * 16f) * (0.80f + p * 0.40f) *
                    BattleFxLayer.FxPixelScale;
                float fade = 0.60f * (1f - p * 0.50f) * themeScale;
                DrawEmissive(sheet, p, it.A, ss, fade);
            }
        }
    }

    // —— 拖尾（2026-11-17，三版迭代，最终选 v2）——
    // v1：3 个历史点各画一份精灵 —— 实机是「3 根分离的竖条」（间距 40px > 可见宽度）。
    // v2（当前）：加密到 18 份重叠，间距约 10px，尺寸递减 —— 连成一条锥形光尾。
    // v3：几何条带（UE Ribbon Renderer / Unity Trail Renderer 的思路）。
    //     试过 DrawPrimitive 与 DrawLine 两版：位置、宽度、亮度都调通了，但**形状语言不搭** ——
    //     主体是「竖刺 + 横底」的像素图，条带是一根横杠，拼在一起像两件不相干的东西。
    //     根本原因是这套素材里没有「横向光带」这一形，要做得像必须新做一张拖尾贴图，
    //     那属于新增素材而不是改绘制逻辑。所以退回 v2：同一张精灵重复，形状天然同源。
    //
    // 若以后要做真 ribbon：先给 FxPixel 加一张 128×32 的横向渐隐光带，
    // 再用 DrawTextureRectRegion 沿轨迹拉长绘制 —— 那时条带的颜色与形状都来自素材本身。
    const int TrailPoints = 18;

    /// <summary>拖尾长度占飞行进度的比例。0.22 ≈ 飞行 0.43s 里的 0.095s 轨迹。</summary>
    const float TrailSpan = 0.22f;

    /// <summary>尺寸递减：头 90% → 尾 30%，收成锥形。</summary>
    const float TrailHeadScale = 0.90f;
    const float TrailTailScale = 0.30f;

    /// <summary>
    /// 单份注入强度。18 份相互重叠（重叠 3~4 层），单份必须比旧版 3 段低得多，
    /// 否则叠加后过曝成白柱（v3 的增益 1.6 就洗白了）。
    /// </summary>
    const float TrailHeadFade = 0.30f;

    /// <summary>
    /// 投射物：先连续拖尾，再叠主体（同帧、同尺寸），都注入 HDR。
    /// 主体尺寸与核心层严格一致，不再放大 1.24 倍 —— 那是旧版主体重影的来源。
    /// </summary>
    void DrawBoltEmissive(BattleFxLayer.FxItem it, float p, float themeScale)
    {
        // 与核心层用同一张（含镜像方向），发光与主体才不会左右对不上。
        bool flip = it.B.X < it.A.X;
        Texture2D sheet = FxPixel.Bolt(it.Cat, flip);
        if (sheet == null)
        {
            return;
        }

        Vector2 pos = BattleFxLayer.BoltPos(it, p);
        // 尺寸必须与核心层同源：暗器在 BoltSize 里缩到 0.55，这里跟着缩放，
        // 否则辉光会比主体大一圈。
        float core = BattleFxLayer.BoltSize(it);

        // 拖尾先画：它拖在主体后面。加法混合与顺序无关，但先画便于之后单独调亮度。
        DrawTrail(it, p, core, themeScale, sheet);
        DrawEmissive(sheet, p, pos, core, 0.70f * themeScale);
    }

    /// <summary>
    /// 沿真实轨迹铺一串重叠精灵。帧也往回退一点，拖尾读起来是「主体甩下的旧帧」，
    /// 而不是同一帧被复制了 18 份。
    /// </summary>
    void DrawTrail(BattleFxLayer.FxItem it, float p, float core, float themeScale,
        Texture2D sheet)
    {
        // 贴图自带拖尾的相性（sword / poison）不再铺残影，
        // 否则会变成「彗星后面再拖一串彗星」。
        if (FxPixel.BoltHasTail(it.Cat))
        {
            return;
        }

        // 刚出膛时没有拖尾：不能凭空画到起点之前。
        float span = Mathf.Min(TrailSpan, p);
        if (span < 0.02f)
        {
            return;
        }

        for (int i = 1; i <= TrailPoints; i++)
        {
            float t = (float)i / TrailPoints;
            Vector2 pt = BattleFxLayer.BoltPos(it, p - span * t);

            float sc = Mathf.Lerp(TrailHeadScale, TrailTailScale, t);
            // 二次衰减：头部保持亮度、尾部快速消失，才像「被甩在后面」。
            float fade = TrailHeadFade * themeScale * (1f - t) * (1f - t);
            float frameP = Mathf.Clamp(p - 0.10f * t, 0f, 1f);

            DrawEmissive(sheet, frameP, pt, core * sc, fade);
        }
    }
}

public partial class BattleFxLayer
{
    /// <summary>由 _Ready 调用：挂上 HDR 发光源子节点，铺满整层。</summary>
    void SetupGlow()
    {
        BattleFxGlow glow = new BattleFxGlow();
        glow.Name = "FxGlow";
        glow.Bind(this);
        // Node2D 用全局坐标绘制，不需要 FullRect 锚点。
        AddChild(glow);
        _glow = glow;
    }
}
