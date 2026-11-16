// 文件：CardArtFx.cs
// 卡面美术的共用小件：**人物破框**（角色头/发/兵器越出卡框）
//
// 为什么需要它（2026-11-14，用户给的参考是 Marvel Snap）：
//   现有素材是按 7:9 卡面**裁切**好的 4 层图，没有出血（bleed）——
//   人物层贴图的四边就是"被切断的飘带/发梢"。想做出"人从卡里探出来"，只能：
//     ① 把人物层单独拿出来、**不参与卡面裁剪**，放大一点、底边对齐卡底；
//     ② 只给底边留一小段 alpha 渐隐，柔化"被信息带硬切"的那条边。
//   两个卡片界面（战斗卡 / 卡册 / 详情大卡）用的是同一套做法，所以 shader 与常数放这里，
//   免得两边各写一份、调参时只改到一边。
//
// 2026-11-16 修正（用户反馈"人物突出边框部分有虚化"）：
//   原实现给**四边**各做 5% 的 alpha 渐隐，理由是藏"切线"。但 `CropPortraits.py`
//   之后 L2 已裁到 alpha 边界，四边本来就是自然透明轮廓，不需要再藏；
//   而顶部 5% 换算到卡面是 13.7px，破出卡框的头顶/发梢/飘带正好落在里面，
//   被 smoothstep 洗到只剩三成不透明度 —— 这就是"虚化"。
//   现在**只淡底边**：顶部与左右完全不动，破框部分保持和卡内一样锐利。
//
// 边界：只有"怎么画"没有"画什么"，不引用任何卡牌类。

using Godot;

public static class CardArtFx
{
    /// <summary>
    /// 底边渐隐带宽（占贴图高度的比例）。
    /// 0.02 约合卡面 5.5px：够把"信息带硬切"的断面化开，又不会啃掉衣摆。
    /// 底边本来就被信息带盖住，这段过渡只在边缘露出的缝隙里起作用。
    /// </summary>
    public const float BottomFade = 0.02f;

    static Shader _fadeShader;

    /// <summary>只在贴图底边做 alpha 渐隐，顶部与左右保持原样。</summary>
    public static Shader FadeShader()
    {
        if (_fadeShader != null)
        {
            return _fadeShader;
        }

        Shader s = new Shader();
        s.Code =
            "shader_type canvas_item;\n" +
            "uniform float bottom_fade : hint_range(0.0, 0.2) = 0.02;\n" +
            "void fragment() {\n" +
            "    vec4 c = texture(TEXTURE, UV);\n" +
            "    float d = 1.0 - UV.y;\n" +
            "    float a = smoothstep(0.0, bottom_fade, d);\n" +
            "    COLOR = vec4(c.rgb, c.a * a);\n" +
            "}\n";

        _fadeShader = s;
        return s;
    }

    /// <summary>给"人物溢出层"配一份带底边渐隐的材质（每层独立，便于将来单独调）。</summary>
    public static ShaderMaterial MakeFadeMaterial()
    {
        ShaderMaterial mat = new ShaderMaterial();
        mat.Shader = FadeShader();
        mat.SetShaderParameter("bottom_fade", BottomFade);
        return mat;
    }

    /// <summary>建一个标准的"人物溢出层"节点（不裁剪、跟随卡面尺寸由调用方摆位）。</summary>
    public static TextureRect MakeCharOverlay(string name)
    {
        TextureRect r = new TextureRect();
        r.Name = name;
        r.MouseFilter = Control.MouseFilterEnum.Ignore;
        r.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        r.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        // L2 约 446x944 缩到卡面 129x274（约 3.4x），需要 mipmap 防发丝混叠成噪点
        r.TextureFilter = Control.TextureFilterEnum.LinearWithMipmaps;
        r.Material = MakeFadeMaterial();
        return r;
    }
}
