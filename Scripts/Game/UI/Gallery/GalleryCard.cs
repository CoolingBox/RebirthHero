// 文件：GalleryCard.cs
// 《轮回大侠》卡册的 4 层视差卡（L1 远背景 / L2 人物 / L3 近前景 / L4 卡框 + 金属高光）
//
// 口径与 CardParallaxDemo 的同名实现一致，只是资源根换成了本工程的 Resources/UI/Gallery/：
//   · 素材是**预拆层**的（比例统一 7:9），每层本来就是独立艺术资源 ——
//     视差只需按深度给不同位移，不需要 shader 猜层次
//   · 卡框不动（0 位移）：它代表"卡"本身的边界，一动卡就散架
//   · 只有最底层（不透明背景）留余量，其余层用卡面尺寸（边缘透明，位移后露出的是下层）
//   · 必须 clip_contents，否则近景会飘到卡面外的背景上
//
// 为什么不做成 shader：本工程的战斗卡（HeroCardLayer）是 500×176 横条，
// 而这里要的是 7:9 竖版收藏卡 —— 分层的素材直接叠，比单图 shader 位移更准也更省调参。

using Godot;
using System.Collections.Generic;

public partial class GalleryCard : Control
{
    /// <summary>卡面素材根目录。命名 = 条目 key（如 claude_n / beast_niulai）。</summary>
    const string ArtRoot = "res://Resources/UI/Gallery/";

    /// <summary>只给最底层（不透明背景）留的余量，其余层用卡面尺寸。</summary>
    const float PadSize = 14f;

    /// <summary>各层深度：远背景几乎不动、人物中等、前景最多、卡框完全不动。</summary>
    static readonly float[] Depth = { 0.18f, 0.55f, 1.00f, 0.00f };

    /// <summary>满倾斜时最近层的位移（像素）。</summary>
    public float ShiftPx = 24f;

    /// <summary>跟随速度（越大越跟手）。</summary>
    public float FollowSpeed = 7f;

    /// <summary>无鼠标时的自动摆动幅度（卡册里一直有鼠标，但静止时也该有呼吸感）。</summary>
    public float SwayAmount = 0.30f;

    /// <summary>金属反光强度。</summary>
    public float GlowStrength = 0.14f;

    /// <summary>
    /// 固定倾斜角（调试/导出卡面用）：设了它就不再跟随鼠标。
    /// 用途之一是"定点截两张不同角度做像素差"——那是验证各层是否真的按深度位移的唯一硬证据。
    /// </summary>
    public bool UseForcedTilt;
    public Vector2 ForcedTilt = Vector2.Zero;

    readonly List<TextureRect> _layers = new List<TextureRect>();
    readonly List<Vector2> _baseOf = new List<Vector2>();
    TextureRect _glow;
    Vector2 _tilt = Vector2.Zero;
    Vector2 _target = Vector2.Zero;
    float _t;

    public override void _Ready()
    {
        // 卡本身不拦鼠标：面板里还有别的可点控件，且倾斜靠全局鼠标位置算
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;

        BuildLayers();
        Resized += Layout;
        Layout();
        SetProcess(true);
    }

    void BuildLayers()
    {
        for (int i = 0; i < 4; i++)
        {
            TextureRect r = new TextureRect();
            r.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            r.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            r.MouseFilter = MouseFilterEnum.Ignore;
            // 必须显式指定带 mipmap 的过滤：素材是 896×1152，卡面只显示 575×739（缩 1.5 倍），
            // 用默认的 Linear 会直接采样稀疏纹素 —— 细节闪烁、摩尔纹，而且开了 mipmap 也白占 33% 内存。
            r.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
            AddChild(r);
            _layers.Add(r);
        }

        _glow = new TextureRect();
        _glow.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _glow.StretchMode = TextureRect.StretchModeEnum.Scale;
        _glow.MouseFilter = MouseFilterEnum.Ignore;

        CanvasItemMaterial mat = new CanvasItemMaterial();
        mat.BlendMode = CanvasItemMaterial.BlendModeEnum.Add;
        _glow.Material = mat;
        _glow.Texture = MakeGlowTexture();
        _glow.Modulate = new Color(1f, 1f, 1f, GlowStrength);
        AddChild(_glow);
    }

    /// <summary>
    /// 径向渐变而不是线性：线性渐变的矩形四角会露出**直的硬边**（卡面上会出现一条斜亮带），
    /// 径向从中心向外衰减，边界永远是圆的。
    /// </summary>
    static GradientTexture2D MakeGlowTexture()
    {
        Gradient g = new Gradient();
        g.Offsets = new float[] { 0f, 0.55f, 1f };
        g.Colors = new Color[]
        {
            new Color(1f, 1f, 1f, 0.85f),
            new Color(1f, 1f, 1f, 0.35f),
            new Color(1f, 1f, 1f, 0f),
        };

        GradientTexture2D t = new GradientTexture2D();
        t.Gradient = g;
        t.Fill = GradientTexture2D.FillEnum.Radial;
        t.FillFrom = new Vector2(0.5f, 0.5f);
        t.FillTo = new Vector2(1f, 0.5f);
        t.Width = 256;
        t.Height = 256;
        return t;
    }

    void Layout()
    {
        _baseOf.Clear();
        for (int i = 0; i < _layers.Count; i++)
        {
            float pad = i == 0 ? PadSize : 0f;
            _layers[i].Size = new Vector2(Size.X + pad * 2f, Size.Y + pad * 2f);
            _layers[i].Position = new Vector2(-pad, -pad);
            _baseOf.Add(new Vector2(-pad, -pad));
        }

        _glow.Size = new Vector2(Size.X * 1.35f, Size.Y * 0.95f);
        _glow.PivotOffset = _glow.Size * 0.5f;
    }

    /// <summary>该条目实际有几层（没有 L4 的素材 = 3 层视差）。</summary>
    public int LayerCount()
    {
        int n = 0;
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].Texture != null)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>某层贴图的原始尺寸（面板上显示"素材 896×1152"用；没有则返回零）。</summary>
    public Vector2 TextureSizeOf(int layer)
    {
        if (layer < 0 || layer >= _layers.Count)
        {
            return Vector2.Zero;
        }
        Texture2D tex = _layers[layer].Texture;
        if (tex == null)
        {
            return Vector2.Zero;
        }
        return tex.GetSize();
    }

    /// <summary>
    /// 切到某个条目。key = 素材目录名（如 claude_n）。
    /// 缺层就留空 —— 三兽只有 3 层（没有卡框），这里**不硬套**别的卡的金框。
    /// </summary>
    public void SetEntry(string key)
    {
        string[] paths =
        {
            ArtRoot + key + "/L1.png",
            ArtRoot + key + "/L2.png",
            ArtRoot + key + "/L3.png",
            ArtRoot + key + "/L4.png",
        };

        for (int i = 0; i < 4; i++)
        {
            if (ResourceLoader.Exists(paths[i]))
            {
                _layers[i].Texture = GD.Load<Texture2D>(paths[i]);
            }
            else
            {
                _layers[i].Texture = null;
            }
        }

        Layout();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;

        if (UseForcedTilt)
        {
            _tilt = ForcedTilt;
            Apply();
            return;
        }

        // 鼠标在整个卡面矩形附近就跟随（卡册的卡偏大，卡外一点点也该有反应）
        Vector2 mp = GetGlobalMousePosition();
        Vector2 center = GlobalPosition + Size * 0.5f;
        Vector2 rel = (mp - center) / (Size * 0.55f);

        if (rel.Length() < 1.35f)
        {
            _target = new Vector2(
                Mathf.Clamp(rel.X, -1.1f, 1.1f),
                Mathf.Clamp(rel.Y, -1.1f, 1.1f));
        }
        else
        {
            _target = new Vector2(
                Mathf.Sin(_t * 0.62f) * SwayAmount,
                Mathf.Cos(_t * 0.47f) * SwayAmount * 0.6f);
        }

        _tilt = _tilt.Lerp(_target, Mathf.Min(1f, (float)delta * FollowSpeed));
        Apply();
    }

    void Apply()
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            Vector2 off = new Vector2(-_tilt.X, -_tilt.Y) * (ShiftPx * Depth[i]);
            _layers[i].Position = _baseOf[i] + off;
        }

        float gx = -_tilt.X * Size.X * 0.22f;
        float gy = -_tilt.Y * Size.Y * 0.14f;
        _glow.Position = new Vector2(
            Size.X * 0.5f - _glow.Size.X * 0.5f + gx,
            Size.Y * 0.5f - _glow.Size.Y * 0.5f + gy);

        float mag = Mathf.Min(1f, _tilt.Length() * 0.9f);
        _glow.Modulate = new Color(1f, 1f, 1f, GlowStrength * (0.30f + mag * 0.70f));
    }
}
