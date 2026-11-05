// 文件：BattleParallaxCard.cs
// 《轮回大侠》战斗界面 —— **4 层视差侠客卡**（Control 子类，由 HeroCardLayer 建并逐帧刷新）
//
// 与卡册（Gallery/GalleryCard.cs）同源，但两者关注点不同：
//   · 卡册卡：大、可点，只负责好看；
//   · 战斗卡：小、不可点，**同时是一块信息面板**（名字 / 血条 / 行动条 / 攻速 / 武学 / 死亡态）。
//   所以信息带**不参与视差** —— 文字跟着倾斜滑来滑去，血量就读不出来了。
//
// 四条与卡册一致的硬规则（都有过踩坑记录）：
//   ① 卡框不动（Depth 0）：它代表"卡"的边界，一动卡就散架；
//   ② 只有最底层（不透明背景）留余量，其余层用卡面尺寸 —— 边缘透明，位移后露出的是下层；
//      给所有层都留余量会被 KeepAspectCovered 放大后裁掉金框；
//   ③ 必须 ClipContents，否则近景会飘到卡外的战场上；
//   ④ 高光用径向渐变（线性渐变旋转后会露出直硬边）。
//
// 死亡态：整卡压到 0.35 透明度（与旧横条卡一致），不做灰化 —— 玩家要能认出"死的是谁"。

using System;
using Godot;

public partial class BattleParallaxCard : Control
{
    /// <summary>本卡对应的战斗单位（HeroCardLayer 用它做刷新与锚点）。</summary>
    public BattleUnit Unit;

    // —— 视差参数（战斗卡比卡册小，位移等比收小）——
    public float ShiftPx = 8f;
    public float FollowSpeed = 6f;
    public float SwayAmount = 0.16f;
    public float GlowStrength = 0.09f;

    /// <summary>各层深度：远背景几乎不动、人物中等、前景最多、卡框完全不动。</summary>
    static readonly float[] Depth = { 0.18f, 0.55f, 1.00f, 0.00f };

    /// <summary>只给最底层（不透明背景）留的余量。</summary>
    const float PadSize = 10f;

    // —— 信息带尺寸 ——
    // 左右对战把卡压到 203×261，信息带只剩 84（占卡高 32%）。
    // 再高就把整幅画压到 60% 以下，"这是张卡"的感觉会没掉；再矮则血条数字读不出。
    const float PanelH = 84f;
    const float PanelInset = 5f;
    const float PanelPad = 7f;
    const float BarH = 14f;
    const float GaugeH = 5f;

    // —— 血条拖尾节奏（秒）：先变色"停住"让人看见掉了多少，再滑过去 ——
    const float TrailFlashHold = 0.12f;
    const float TrailSlide = 0.32f;

    readonly TextureRect[] _layers = new TextureRect[4];
    readonly Vector2[] _baseOf = new Vector2[4];
    readonly ColorRect[] _border = new ColorRect[4];

    ColorRect _board;
    Control _art;
    TextureRect _glow;
    Control _panel;

    Label _nameText;
    ColorRect _hpBack;
    ColorRect _hpFill;
    ColorRect _hpTrail;
    ColorRect _hpShield;
    ColorRect _hpLock;
    Label _hpText;
    ColorRect _gaugeBack;
    ColorRect _gaugeFill;
    Label _statText;
    Label _stateText;
    Label _skillText;

    Vector2 _tilt = Vector2.Zero;
    Vector2 _target = Vector2.Zero;
    float _t;
    float _phase;

    // 血条拖尾状态
    float _lastFrac = -1f;
    float _curFrac;
    float _trailFrom;
    float _trailTo;
    float _trailEdge;
    int _trailPhase;
    float _trailClock;

    bool _built;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;

        EnsureBuilt();
        SetProcess(true);
    }

    /// <summary>
    /// 懒建（幂等）。**不能只靠 _Ready**：建卡发生在 BattlePanel.OnShow → StartBattle 里，
    /// 那一刻面板自己可能还没完成入树（GameRoot 在 _EnterTree 阶段就 ShowPanel），
    /// 子节点的 _Ready 会被延后 —— 于是 Setup 里拿到的全是 null（踩过：NullReferenceException）。
    /// 所有节点构造集中到这里，Setup 先调它，时序就与 _Ready 无关了。
    /// </summary>
    void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }
        _built = true;

        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;

        BuildBoard();
        BuildArt();
        BuildBorder();
        BuildPanel();

        Resized += LayoutAll;
        LayoutAll();
    }

    // ============ 建节点 ============

    /// <summary>底板：素材缺失时的兜底底色，也保证透明边缘后面不是战场背景。</summary>
    void BuildBoard()
    {
        _board = new ColorRect();
        _board.Name = "Board";
        _board.MouseFilter = MouseFilterEnum.Ignore;
        _board.Color = UIThemeManager.CardAlly;
        AddChild(_board);
    }

    void BuildArt()
    {
        _art = new Control();
        _art.Name = "Art";
        _art.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_art);

        for (int i = 0; i < 4; i++)
        {
            TextureRect r = new TextureRect();
            r.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            r.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            r.MouseFilter = MouseFilterEnum.Ignore;
            // 战斗卡 300×386 显示 896×1152 的源图（缩 3 倍），必须走 mipmap，
            // 否则远处的发丝/花纹会抖成噪点（默认 Linear 不采样 mipmap）。
            r.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
            _art.AddChild(r);
            _layers[i] = r;
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
        _art.AddChild(_glow);
    }

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
        t.Width = 128;
        t.Height = 128;
        return t;
    }

    /// <summary>
    /// 阵营描边：4 条 3px 细条压在画面边缘。
    /// 为什么战斗卡要它而卡册不用：竖版卡是整幅画，盟友/敌人靠站位 + 描边区分，
    /// 这是"扫一眼就知道谁打谁"的唯一线索。
    /// 描边**不参与视差** —— 它是卡的边，跟着倾斜会露出缝。
    /// </summary>
    void BuildBorder()
    {
        for (int i = 0; i < 4; i++)
        {
            ColorRect c = new ColorRect();
            c.MouseFilter = MouseFilterEnum.Ignore;
            c.Color = UIThemeManager.CardBorderAlly;
            AddChild(c);
            _border[i] = c;
        }
    }

    void BuildPanel()
    {
        _panel = new Control();
        _panel.Name = "Info";
        _panel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_panel);

        Panel bg = new Panel();
        bg.Name = "Bg";
        bg.MouseFilter = MouseFilterEnum.Ignore;

        StyleBoxFlat sb = new StyleBoxFlat();
        // 0.86 而不是 0.7：卡面是整幅画，亮背景（红叶、雪地）会把灰字洗掉。
        // 信息带必须"读得清"优先于"透出画" —— 血量读错比好看重要。
        sb.BgColor = new Color(0.03f, 0.03f, 0.05f, 0.86f);
        sb.CornerRadiusTopLeft = 8;
        sb.CornerRadiusTopRight = 8;
        sb.CornerRadiusBottomLeft = 8;
        sb.CornerRadiusBottomRight = 8;
        sb.BorderWidthTop = 1;
        sb.BorderColor = new Color(1f, 1f, 1f, 0.14f);
        bg.AddThemeStyleboxOverride("panel", sb);
        _panel.AddChild(bg);

        _nameText = MakeLabel(14, HorizontalAlignment.Center);
        _nameText.AddThemeColorOverride("font_color", UIThemeManager.TextPrimary);
        _nameText.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _nameText.AddThemeConstantOverride("outline_size", 4);
        _panel.AddChild(_nameText);

        _hpBack = MakeRect(UISignalColors.HpBack);
        _hpFill = MakeRect(UISignalColors.HpGood);
        _hpTrail = MakeRect(UISignalColors.HpTrailDamage);
        _hpShield = MakeRect(UISignalColors.ShieldGoldFill);
        _hpLock = MakeRect(UISignalColors.HpLocked);

        // 拖尾压在填充之上：掉血时露在填充右边，回血时盖在新长出的那段上再收掉
        _panel.AddChild(_hpBack);
        _panel.AddChild(_hpFill);
        _panel.AddChild(_hpTrail);
        _panel.AddChild(_hpShield);
        _panel.AddChild(_hpLock);

        _hpText = MakeLabel(12, HorizontalAlignment.Center);
        _hpText.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.95f));
        _hpText.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _hpText.AddThemeConstantOverride("outline_size", 4);
        _panel.AddChild(_hpText);

        _gaugeBack = MakeRect(UISignalColors.GaugeBack);
        _gaugeFill = MakeRect(UISignalColors.GaugeFill);
        _panel.AddChild(_gaugeBack);
        _panel.AddChild(_gaugeFill);

        _statText = MakeLabel(12, HorizontalAlignment.Left);
        _statText.AddThemeColorOverride("font_color", new Color(0.80f, 0.84f, 0.90f));
        _panel.AddChild(_statText);

        _stateText = MakeLabel(12, HorizontalAlignment.Right);
        _stateText.AddThemeColorOverride("font_color", UISignalColors.Shield);
        _panel.AddChild(_stateText);

        _skillText = MakeLabel(12, HorizontalAlignment.Left);
        _skillText.AddThemeColorOverride("font_color", new Color(0.78f, 0.74f, 0.66f));
        _skillText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _skillText.ClipText = true;
        _panel.AddChild(_skillText);
    }

    static ColorRect MakeRect(Color c)
    {
        ColorRect r = new ColorRect();
        r.Color = c;
        r.MouseFilter = MouseFilterEnum.Ignore;
        r.Visible = true;
        return r;
    }

    Label MakeLabel(int fontSize, HorizontalAlignment align)
    {
        Label l = new Label();
        l.MouseFilter = MouseFilterEnum.Ignore;
        l.HorizontalAlignment = align;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.AddThemeFontSizeOverride("font_size", fontSize);
        return l;
    }

    // ============ 数据装配 ============

    /// <summary>
    /// 绑定单位与卡面。必须在 AddChild 之后调用（节点在 _Ready 里才建好）。
    /// enemy 决定描边与底板色，artKey 是 BattleArt 给的素材目录。
    /// </summary>
    public void Setup(BattleUnit u, bool enemy, string artKey)
    {
        EnsureBuilt();
        Unit = u;

        if (_board != null)
        {
            _board.Color = enemy ? UIThemeManager.CardEnemy : UIThemeManager.CardAlly;
        }

        Color borderColor = enemy ? UIThemeManager.CardBorderEnemy : UIThemeManager.CardBorderAlly;
        for (int i = 0; i < _border.Length; i++)
        {
            _border[i].Color = borderColor;
        }

        LoadArt(artKey);

        // 摆动相位按位次错开：否则 8 张卡会像一块板子整体晃
        _phase = u.Slot * 0.7f;
        if (enemy)
        {
            _phase += 1.3f;
        }

        _nameText.Text = SlotTag(u.Slot) + (u.Name ?? "");
        _statText.Text = "ATK " + u.BaseAtk.ToString("0") + " · SPD " + u.BaseSpd.ToString("0");
        _skillText.Text = SkillsText(u);

        LayoutAll();
    }

    static string SlotTag(int slot)
    {
        switch (slot)
        {
            case 1: return "①";
            case 2: return "②";
            case 3: return "③";
            case 4: return "④";
            default: return "";
        }
    }

    static string SkillsText(BattleUnit u)
    {
        if (u.Skills == null || u.Skills.Count == 0)
        {
            return "功 无";
        }

        string s = "功 ";
        for (int i = 0; i < u.Skills.Count; i++)
        {
            if (i > 0)
            {
                s += "·";
            }
            s += u.Skills[i].Name;
        }
        return s;
    }

    void LoadArt(string key)
    {
        string root = BattleArt.Root + key + "/";
        string[] names = { "L1", "L2", "L3", "L4" };

        for (int i = 0; i < 4; i++)
        {
            string path = root + names[i] + ".png";
            if (ResourceLoader.Exists(path))
            {
                _layers[i].Texture = GD.Load<Texture2D>(path);
            }
            else
            {
                _layers[i].Texture = null;
            }
        }
    }

    // ============ 布局 ============

    void LayoutAll()
    {
        if (_board != null)
        {
            _board.Position = Vector2.Zero;
            _board.Size = Size;
        }

        if (_art != null)
        {
            _art.Position = Vector2.Zero;
            _art.Size = Size;
        }

        for (int i = 0; i < 4; i++)
        {
            float pad = i == 0 ? PadSize : 0f;
            _layers[i].Position = new Vector2(-pad, -pad);
            _layers[i].Size = new Vector2(Size.X + pad * 2f, Size.Y + pad * 2f);
            _baseOf[i] = _layers[i].Position;
        }

        if (_glow != null)
        {
            _glow.Size = new Vector2(Size.X * 1.30f, Size.Y * 0.95f);
        }

        LayoutBorder();
        LayoutPanel();
    }

    void LayoutBorder()
    {
        const float w = 3f;

        _border[0].Position = new Vector2(0f, 0f);
        _border[0].Size = new Vector2(Size.X, w);

        _border[1].Position = new Vector2(0f, Size.Y - w);
        _border[1].Size = new Vector2(Size.X, w);

        _border[2].Position = new Vector2(0f, 0f);
        _border[2].Size = new Vector2(w, Size.Y);

        _border[3].Position = new Vector2(Size.X - w, 0f);
        _border[3].Size = new Vector2(w, Size.Y);
    }

    void LayoutPanel()
    {
        if (_panel == null)
        {
            return;
        }

        float panelW = Size.X - PanelInset * 2f;
        _panel.Position = new Vector2(PanelInset, Size.Y - PanelH - 6f);
        _panel.Size = new Vector2(panelW, PanelH);

        Control bg = _panel.GetNodeOrNull<Control>("Bg");
        if (bg != null)
        {
            bg.Position = Vector2.Zero;
            bg.Size = _panel.Size;
        }

        float px = PanelPad;
        float pw = panelW - PanelPad * 2f;

        SetRect(_nameText, px, 4f, pw, 16f);

        SetRect(_hpBack, px, 21f, pw, BarH);
        SetRect(_hpFill, px, 21f, 0f, BarH);
        SetRect(_hpTrail, px, 21f, 0f, BarH);
        SetRect(_hpShield, px, 21f, 0f, BarH);
        SetRect(_hpLock, px, 21f, 0f, BarH);
        SetRect(_hpText, px, 21f, pw, BarH);

        SetRect(_gaugeBack, px, 37f, pw, GaugeH);
        SetRect(_gaugeFill, px, 37f, 0f, GaugeH);

        SetRect(_statText, px, 44f, pw * 0.58f, 16f);
        SetRect(_stateText, px + pw * 0.42f, 44f, pw * 0.58f, 16f);

        SetRect(_skillText, px, 61f, pw, 16f);
    }

    static void SetRect(Control c, float x, float y, float w, float h)
    {
        if (c == null)
        {
            return;
        }
        c.Position = new Vector2(x, y);
        c.Size = new Vector2(w, h);
    }

    // ============ 逐帧 ============

    public override void _Process(double delta)
    {
        if (!_built)
        {
            return;
        }

        _t += (float)delta;
        UpdateTrail((float)delta);
        UpdateTilt((float)delta);
    }

    void UpdateTilt(float dt)
    {
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
                Mathf.Sin((_t + _phase) * 0.52f) * SwayAmount,
                Mathf.Cos((_t + _phase) * 0.41f) * SwayAmount * 0.6f);
        }

        _tilt = _tilt.Lerp(_target, Mathf.Min(1f, dt * FollowSpeed));

        for (int i = 0; i < 4; i++)
        {
            Vector2 off = new Vector2(-_tilt.X, -_tilt.Y) * (ShiftPx * Depth[i]);
            _layers[i].Position = _baseOf[i] + off;
        }

        float gx = -_tilt.X * Size.X * 0.20f;
        float gy = -_tilt.Y * Size.Y * 0.12f;
        _glow.Position = new Vector2(
            Size.X * 0.5f - _glow.Size.X * 0.5f + gx,
            Size.Y * 0.5f - _glow.Size.Y * 0.5f + gy);

        float mag = Mathf.Min(1f, _tilt.Length() * 0.9f);
        _glow.Modulate = new Color(1f, 1f, 1f, GlowStrength * (0.30f + mag * 0.70f));
    }

    // ============ 刷新（0.1s 一次，由面板的 Tick 驱动） ============

    public void Refresh(BattleEngine engine)
    {
        if (Unit == null)
        {
            return;
        }

        EnsureBuilt();

        BattleUnit u = Unit;

        // 血条满格 = 可用上限 + 锁死上限；彩色段 = 当前气血占满格，银灰段 = 锁死部分固定在最右端
        double total = u.BaseHp + u.LockedHp;
        double availFrac = total > 0 ? u.BaseHp / total : 1;
        double hpFrac = total > 0 ? Math.Clamp(u.Hp / total, 0, 1) : 0;

        float barW = _hpBack.Size.X;

        // ① 主体条立即到新值：主体负责"读得出真实血量"，拖尾负责"看得见变化"
        _hpFill.Size = new Vector2((float)(barW * hpFrac), BarH);
        double ratio = u.BaseHp > 0 ? u.Hp / u.BaseHp : 0;
        if (ratio > 0.5)
        {
            _hpFill.Color = UISignalColors.HpGood;
        }
        else if (ratio > 0.25)
        {
            _hpFill.Color = UISignalColors.HpWarn;
        }
        else
        {
            _hpFill.Color = UISignalColors.HpBad;
        }

        // ② 拖尾：这里只"检测变化并起势"，推进在 _Process
        _curFrac = (float)hpFrac;
        if (_lastFrac < 0)
        {
            _lastFrac = _curFrac;
        }
        else if (Math.Abs(_curFrac - _lastFrac) > 0.002f)
        {
            _trailFrom = _lastFrac;
            _trailTo = _curFrac;
            _trailEdge = _trailFrom;
            _trailPhase = 1;
            _trailClock = 0f;
            if (_curFrac < _lastFrac)
            {
                _hpTrail.Color = UISignalColors.HpTrailDamage;
            }
            else
            {
                _hpTrail.Color = UISignalColors.HpTrailHeal;
            }
            _hpTrail.Visible = true;
            _lastFrac = _curFrac;
            LayoutTrail(barW);
        }

        // ③ 护盾：金色蒙层（占血条多少）在血条上，蓝色数值在状态行
        double shieldPct = u.BaseHp > 0 ? u.Shield / u.BaseHp : 0;
        if (u.Shield > 0 && shieldPct > 0.001)
        {
            double shown = Math.Clamp(shieldPct, 0, 1);
            _hpShield.Visible = true;
            _hpShield.Size = new Vector2((float)(barW * shown), BarH);
        }
        else
        {
            _hpShield.Visible = false;
        }

        string hpText = Math.Max(0, u.Hp).ToString("0") + "/" + u.BaseHp.ToString("0");
        if (u.LockedHp > 0)
        {
            _hpLock.Visible = true;
            _hpLock.Position = new Vector2(_hpBack.Position.X + (float)(barW * availFrac), _hpBack.Position.Y);
            _hpLock.Size = new Vector2((float)(barW * (1 - availFrac)), BarH);
            hpText += "  锁" + u.LockedHp.ToString("0");
        }
        else
        {
            _hpLock.Visible = false;
        }
        _hpText.Text = hpText;

        // ④ 行动条
        float g = (float)Math.Clamp(u.Gauge / BattleEngine.GaugeMax, 0, 1);
        _gaugeFill.Size = new Vector2(barW * g, GaugeH);

        // ⑤ 状态行右侧：沉默 > 护盾（沉默更紧急：被禁技能时盾多少不重要）
        if (u.Silenced(engine.T))
        {
            _stateText.Text = "⛔ 沉默";
            _stateText.AddThemeColorOverride("font_color", UISignalColors.Lose);
        }
        else if (u.Shield > 0)
        {
            _stateText.Text = "🛡 " + u.Shield.ToString("0");
            _stateText.AddThemeColorOverride("font_color", UISignalColors.Shield);
        }
        else
        {
            _stateText.Text = "";
        }

        Modulate = u.Alive ? Colors.White : new Color(1f, 1f, 1f, 0.35f);
    }

    void UpdateTrail(float dt)
    {
        if (_trailPhase == 0)
        {
            return;
        }

        _trailClock += dt;

        if (_trailPhase == 1)
        {
            if (_trailClock >= TrailFlashHold)
            {
                _trailPhase = 2;
                _trailClock = 0f;
            }
        }
        else
        {
            float k = _trailClock / TrailSlide;
            if (k >= 1f)
            {
                _trailPhase = 0;
                _hpTrail.Visible = false;
                return;
            }
            float e = 1f - (1f - k) * (1f - k);
            _trailEdge = Mathf.Lerp(_trailFrom, _trailTo, e);
        }

        LayoutTrail(_hpBack.Size.X);
    }

    void LayoutTrail(float barW)
    {
        float lo = Mathf.Min(_trailEdge, _curFrac);
        float hi = Mathf.Max(_trailEdge, _curFrac);
        _hpTrail.Position = new Vector2(_hpBack.Position.X + barW * lo, _hpBack.Position.Y);
        _hpTrail.Size = new Vector2(barW * (hi - lo), BarH);
    }
}
