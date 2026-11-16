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

    /// <summary>不透明环形衬边：挡住背景露到金线外圈的那一圈，三处大卡共用工厂装配。</summary>
    Panel _frameBacking;

    /// <summary>被裁剪的内层：L1/L3/衬边/卡框与高光都在里面，只有人物可以出画。</summary>
    Control _inner;

    /// <summary>人物溢出层（2026-11-14，Marvel Snap 式破框；见 CardArtFx）。</summary>
    TextureRect _charOver;

    Vector2 _charBase;

    /// <summary>
    /// 人物头顶探出卡框的**绝对像素**。
    /// 原用 `CharScale = 1.10`（按"素材透明留白均值约 9%"反推，认为只探出 ~14px），
    /// 但 CropPortraits.py 把 L2 裁到 alpha 边界后留白没了，1.10 变成实打实探出
    /// 74px，头整个顶出卡框。改用绝对像素，不受卡高与素材留白比例影响。
    /// 战斗卡探出约 13px，这里取 16px 与它同量级。
    /// </summary>
    const float CharOverhang = 16f;

    public override void _Ready()
    {
        // 卡本身不拦鼠标：面板里还有别的可点控件，且倾斜靠全局鼠标位置算
        MouseFilter = MouseFilterEnum.Ignore;
        // 根不裁剪：人物要画到卡框外面；背景/前景/卡框由内层 _inner 自己裁
        ClipContents = false;

        BuildLayers();
        Resized += Layout;
        Layout();
        SetProcess(true);
    }

    void BuildLayers()
    {
        _inner = new Control();
        _inner.Name = "Inner";
        _inner.MouseFilter = MouseFilterEnum.Ignore;
        _inner.ClipContents = true;
        AddChild(_inner);

        for (int i = 0; i < 4; i++)
        {
            if (i == 3)
            {
                // 卡框层之前先垫实体衬边：线描是镂空的，背景会露在金线外圈。
                // 卡册大卡约 575 宽，取 5%（≈28px），与战斗卡 12/203 同比例。
                _frameBacking = CardBorderFactory.CreateBacking(28f);
                _inner.AddChild(_frameBacking);
            }

            TextureRect r = new TextureRect();
            r.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            r.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            r.MouseFilter = MouseFilterEnum.Ignore;
            // 必须显式指定带 mipmap 的过滤：素材是 896×1152，卡面只显示 575×739（缩 1.5 倍），
            // 用默认的 Linear 会直接采样稀疏纹素 —— 细节闪烁、摩尔纹，而且开了 mipmap 也白占 33% 内存。
            r.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
            _inner.AddChild(r);
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
        _inner.AddChild(_glow);

        // 人物在最后、且挂在根上：越过卡框、压在卡框之上
        _charOver = CardArtFx.MakeCharOverlay("CharOver");
        AddChild(_charOver);
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
        if (_inner != null)
        {
            _inner.Position = Vector2.Zero;
            _inner.Size = Size;
        }

        if (_frameBacking != null)
        {
            _frameBacking.Position = Vector2.Zero;
            _frameBacking.Size = Size;
        }

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

        if (_charOver != null)
        {
            // 按**纹理真实比例**算宽，不能用卡面尺寸的 1.10 倍同时当宽高。
            // CropPortraits.py 之后 L2 已裁到各自 alpha 边界，宽高比 0.46~0.58 各不相同；
            // 若把节点设成 632×813(aspect 0.778) 再配 KeepAspectCovered，
            // 纹理会按宽度放大 1.42x、绘制高 1339，上下各裁 263px —— 正好把头裁掉。
            // 战斗卡 BattleParallaxCard.LayoutCharOver 早已按此修正，卡册这里漏了。
            float h = Size.Y + CharOverhang;
            float aspect = 7f / 9f;
            if (_charOver.Texture != null)
            {
                Texture2D tex = _charOver.Texture;
                aspect = (float)tex.GetWidth() / (float)tex.GetHeight();
            }

            float w = h * aspect;
            _charBase = new Vector2((Size.X - w) * 0.5f, Size.Y - h);
            _charOver.Position = _charBase;
            _charOver.Size = new Vector2(w, h);
        }
    }

    /// <summary>该条目实际有几层（卡册条目全部有共享卡框，正常为 4 层）。</summary>
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

        // 人物层已搬到溢出层，仍算一层（否则卡册会报"3 层"）
        if (_charOver != null && _charOver.Texture != null)
        {
            n++;
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

        Texture2D tex;
        if (layer == 1 && _charOver != null)
        {
            tex = _charOver.Texture;
        }
        else tex = _layers[layer].Texture;

        if (tex == null)
        {
            return Vector2.Zero;
        }
        return tex.GetSize();
    }

    /// <summary>兼容旧调用：没给品阶就按最低档取框。</summary>
    public void SetEntry(string key)
    {
        SetEntry(key, 1);
    }

    /// <summary>
    /// 切到某个条目。key = 素材目录名。
    /// 第 4 层不再是素材自带的亮金回纹框，而是与战斗卡共用
    /// `CardBorderFactory` 的 6 套平面线描纹样（按品阶），三处边框体系统一。
    /// </summary>
    public void SetEntry(string key, int rarity)
    {
        string[] paths =
        {
            ArtRoot + key + "/L1.png",
            ArtRoot + key + "/L2.png",
            ArtRoot + key + "/L3.png",
        };

        for (int i = 0; i < 3; i++)
        {
            if (!ResourceLoader.Exists(paths[i]))
            {
                _layers[i].Texture = null;
                if (i == 1 && _charOver != null)
                {
                    _charOver.Texture = null;
                }
                continue;
            }

            Texture2D tex = GD.Load<Texture2D>(paths[i]);

            // 人物层交给溢出层（不在裁剪区里），卡册才看得到"角色出画"
            if (i == 1 && _charOver != null)
            {
                _layers[i].Texture = null;
                _charOver.Texture = tex;
                continue;
            }

            _layers[i].Texture = tex;
        }

        CardBorderKind kind = CardBorderFactory.ForRarity(rarity);
        _layers[3].Texture = CardBorderFactory.Get(kind);

        // Layout 会按新纹理的宽高比重排人物层（见 Layout 内的人物分支）
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

        // 人物溢出层跟着"人物"的深度走，否则倾斜时会看到两张人错位
        if (_charOver != null)
        {
            Vector2 charOff = new Vector2(-_tilt.X, -_tilt.Y) * (ShiftPx * Depth[1]);
            _charOver.Position = _charBase + charOff;
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
