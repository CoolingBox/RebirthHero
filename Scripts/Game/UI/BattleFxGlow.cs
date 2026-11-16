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
// 边界：本层只画**同尺寸、同帧**的形状，不放大、不膨胀、不描边、不做色散。
//   13 相性的几何、轨迹、时序仍全在 BattleFxProjectile / BattleFxImpact。
//   关闭整套辉光：注释 GameRoot 的 SetupGlowEnvironment() 即可
//   （没有 > 1 像素时 glow 是空操作）。

using System.Collections.Generic;
using Godot;

public partial class BattleFxGlow : Control
{
    /// <summary>
    /// 注入的 HDR 增量。核心层已画 1.0×贴图色，本层再叠 boost×fade×贴图色，
    /// 特效亮部总和约 1 + boost×fade（> 1 才发光），暗部仍 <= 1 不发光，
    /// 等于天然 bright-pass，稀疏半透明像素不会被无差别点亮。
    /// </summary>
    const float EmissiveBoost = 0.85f;

    BattleFxLayer _owner;

    public void Bind(BattleFxLayer owner)
    {
        _owner = owner;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
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

    /// <summary>
    /// 投射物：主形状 + 沿真实轨迹历史的 3 段残影，都注入 HDR。
    /// 尺寸与核心层严格一致，不再放大 1.24 倍 —— 那是旧版主体重影的来源。
    /// </summary>
    void DrawBoltEmissive(BattleFxLayer.FxItem it, float p, float themeScale)
    {
        Texture2D sheet = FxPixel.Bolt(it.Cat);
        if (sheet == null)
        {
            return;
        }

        Vector2 pos = BattleFxLayer.BoltPos(it, p);
        float core = (46f + it.Power * 14f) * BattleFxLayer.FxPixelScale;
        DrawEmissive(sheet, p, pos, core, 0.70f * themeScale);

        for (int k = 1; k <= 3; k++)
        {
            float lag = Mathf.Min(0.055f * k, p * 0.60f);
            if (lag < 0.01f)
            {
                continue;
            }

            float back = Mathf.Clamp(p - lag, 0f, 1f);
            Vector2 tail = BattleFxLayer.BoltPos(it, back);
            float frameP = Mathf.Clamp(p - 0.10f * k, 0f, 1f);
            float ts = core * (0.92f - 0.16f * (k - 1));
            float fade = (0.45f / (float)k) * themeScale;
            DrawEmissive(sheet, frameP, tail, ts, fade);
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
        glow.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(glow);
        _glow = glow;
    }
}
