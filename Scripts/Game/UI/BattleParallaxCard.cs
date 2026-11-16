// 文件：BattleParallaxCard.cs
// 《轮回大侠》战斗界面 —— **4 层视差侠客卡**（Control 子类，由 HeroCardLayer 建并逐帧刷新）
//
// 与卡册（Gallery/GalleryCard.cs）同源，但两者关注点不同：
//   · 卡册卡：大、可点，只负责好看；
//   · 战斗卡：小，**同时是一块信息面板**（名字 / 血条 / 行动条 / 攻速 / 武学 / 死亡态），
//     点一下打开详情（2026-11-12）。
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
using System.Collections.Generic;
using Godot;

public partial class BattleParallaxCard : Control
{
    /// <summary>本卡对应的战斗单位（HeroCardLayer 用它做刷新与锚点）。</summary>
    public BattleUnit Unit;

    /// <summary>
    /// 点卡面（打开详情）。由 HeroCardLayer 逐卡挂上 —— 卡自己不知道该找谁要详情。
    /// </summary>
    public event Action<BattleUnit> Clicked;

    // —— 视差参数（战斗卡比卡册小，位移等比收小）——
    public float ShiftPx = 8f;
    public float FollowSpeed = 6f;
    public float SwayAmount = 0.16f;
    public float GlowStrength = 0.09f;

    /// <summary>各层深度：远背景几乎不动、人物中等、前景最多、卡框完全不动。</summary>
    static readonly float[] Depth = { 0.18f, 0.55f, 1.00f, 0.00f };

    /// <summary>只给最底层（不透明背景）留的余量。</summary>
    const float PadSize = 10f;

    // —— 人物溢出（2026-11-14，参考 Marvel Snap"角色破框"）——
    /// <summary>
    /// 人物层相对**可见画区**（卡高 − 信息带 84）的放大倍数。
    /// <para>
    /// 2026-11-14 定稿：1.10 时头顶刚好高出卡框几个像素，脚压在信息带上沿，整具人都露着。
    /// 比这个再大，腿就会重新被信息带吃掉（玩家会读成"缺腿"）。
    /// </para>
    /// <para>
    /// 卡册 / 详情大卡没有信息带，仍按整卡排版，它们的倍数在 `GalleryCard` / `UnitDetailView` 里。
    /// </para>
    /// </summary>
    const float CharScale = 1.05f;

    /// <summary>素材宽高比（896:1152 = 7:9），用来按高度反推宽度。</summary>
    const float CharAspect = 896f / 1152f;

    /// <summary>脚底越过"信息带上沿"的像素数 —— 4px 足够让脚不悬空，又不会伸进文字行。</summary>
    const float CharFootOver = 4f;

    // —— 信息带尺寸 ——
    // 左右对战把卡压到 203×261，信息带只剩 84（占卡高 32%）。
    // 再高就把整幅画压到 60% 以下，"这是张卡"的感觉会没掉；再矮则血条数字读不出。
    const float PanelH = 84f;
    const float PanelInset = 5f;
    const float PanelPad = 7f;
    const float BarH = 14f;
    const float GaugeH = 5f;

    // —— 出手横幅：显示 1.2s，最后 0.35s 淡出 ——
    // 短于最短的行动周期（速度 60 时也要 1.6s 才轮一次），所以两条不会叠着读；
    // 同一行动里连放两门武学的情况由 ShowCast 合并成一行。
    const float CastShowSec = 1.20f;
    const float CastFadeSec = 0.35f;
    const float CastBarH = 26f;
    const float CastTop = 8f;
    const float CastInset = 10f;

    // —— 血条拖尾节奏（秒）：先变色"停住"让人看见掉了多少，再滑过去 ——
    const float TrailFlashHold = 0.12f;
    const float TrailSlide = 0.32f;

    readonly TextureRect[] _layers = new TextureRect[4];
    readonly Vector2[] _baseOf = new Vector2[4];
    // AI 线描边框是镂空的，若背景铺满整卡，插画会从金线外沿一直露到卡边，
    // 看起来"背景比边框大一圈"。把画区内缩，最外一圈完全交给实体边框。
    const float EdgeW = 12f;
    const float ArtInset = 12f;

    // 边框是**一张共享纹理**（CardBorderFactory），不是四条 ColorRect。
    // AI 生图的整幅卡框，纹样在 Setup 里按品阶换纹理，颜色只给轻微阵营倾向。
    TextureRect _borderFrame;
    Panel _frameBacking;

    ColorRect _board;
    Control _art;
    TextureRect _glow;
    Control _panel;

    /// <summary>人物溢出层：**不在**被裁剪的 _art 里，所以头/发/兵器能越过卡框。</summary>
    TextureRect _charOver;

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

    // —— 出手横幅（2026-11-12）：这一手放了什么武学，直接写在卡牌头部 ——
    Panel _castBar;
    StyleBoxFlat _castStyle;
    Label _castText;
    float _castLeft;

    /// <summary>同一手放出的武学（一般 1 门，主 + 副效果时 2 门），各自累计段数用于"×N"。</summary>
    sealed class CastEntry
    {
        public string Name;
        public int Hits;
    }

    readonly List<CastEntry> _castEntries = new List<CastEntry>();

    Vector2 _tilt = Vector2.Zero;
    Vector2 _target = Vector2.Zero;
    float _t;
    float _phase;
    Vector2 _charBase;

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
        // Stop：卡要能接点击（点在卡上 = 看这张卡的详情）。
        // 背景层与信息带里的子节点全是 Ignore，不会把点击抢走。
        MouseFilter = MouseFilterEnum.Stop;
        // 不裁剪：人物溢出层要画到卡框外面（见 BuildCharOver）
        ClipContents = false;

        EnsureBuilt();
        SetProcess(true);
    }

    /// <summary>
    /// 左键按下 = 看详情。只认 Pressed 不认 Released：鼠标在卡上划过时视差会跟着动，
    /// 若按 Released 判定，按住拖到别处松手也会弹一次详情。
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        InputEventMouseButton mb = @event as InputEventMouseButton;
        if (mb == null || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        AcceptEvent();
        if (Clicked != null && Unit != null)
        {
            Clicked(Unit);
        }
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

        MouseFilter = MouseFilterEnum.Stop;
        // 卡根**不裁剪**：人物溢出层要画到卡框外面去。
        // 背景/前景/卡框仍由内部的 _art 自己 ClipContents 管住，不会漏到战场上。
        ClipContents = false;

        BuildBoard();
        BuildArt();
        BuildBorder();
        BuildCharOver();   // 必须晚于 BuildBorder：人物要压在卡框之上
        BuildPanel();
        BuildCast();

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
        // 必须裁剪：背景层为视差被放大了 20px（PadSize），不裁就会溢出到边框外，
        // 看起来像"背景比边框大一圈"。人物溢出层挂在卡根、不在本层内，不受影响。
        _art.ClipContents = true;
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
    /// <summary>
    /// 人物溢出层（2026-11-14，参考 Marvel Snap 的"角色破框"）：
    /// 把 L2 人物**从被裁剪的画框里拿出来**，放大 <see cref="CharScale"/> 倍、底边对齐卡底，
    /// 于是头/发/兵器越过卡框，压在卡框与相邻留白上 —— 卡还是那张卡，人先"出画"。
    /// <para>
    /// 为什么不是简单地把卡根改成不裁剪：那样背景与前景也会漏到卡外。
    /// 所以内层 `_art` 继续裁剪（L1/L3/L4 都在里面），只有这一层单独挂在卡根上。
    /// </para>
    /// <para>
    /// 为什么贴了渐隐 shader：素材是按卡面裁的，**没有出血**；放大后贴图四边就是
    /// "被切断的飘带/发梢"。硬切看着像 bug，按 5% 的宽度渐隐，就变成"衣袖散进雾里"。
    /// </para>
    /// </summary>
    void BuildCharOver()
    {
        _charOver = CardArtFx.MakeCharOverlay("CharOver");
        AddChild(_charOver);
    }

    void BuildBorder()
    {
        // 衬边与边框纹样都由工厂装配，卡册 / 详情大卡用同一套，三处不再各写一份。
        _frameBacking = CardBorderFactory.CreateBacking(EdgeW);
        AddChild(_frameBacking);

        // 默认纹样先给回阑，Setup 时按品阶换成真正的纹理
        _borderFrame = CardBorderFactory.CreateNode(CardBorderKind.HuiLan, Colors.White);
        AddChild(_borderFrame);
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

    /// <summary>
    /// 出手横幅：卡牌头部一条暗底金字，写"这一手放了什么武学"。
    /// <para>
    /// 为什么要它：信息带里的武学名是 12px 的常驻列表，**看得到名字但看不到"正在放哪个"** ——
    /// 玩家只能靠飘字伤害去猜。挂机时最想确认的恰恰是"我的毒爆到底放出来了没有"。
    /// </para>
    /// <para>
    /// 为什么只有 26px 高、贴在顶部：卡面顶部是留白（人物头肩之上），不压信息带；
    /// 再高就会盖住立绘的脸 —— 那是玩家认出"这是谁"的关键区域。
    /// </para>
    /// </summary>
    void BuildCast()
    {
        // 圆角标签而不是通栏色块：通栏会把卡框的顶边整条切掉，读起来像"卡被贴了封条"。
        // 左右各留 10px，卡框的转角就能露出来，标签是"落在卡上"，不是"盖住卡"。
        _castStyle = new StyleBoxFlat();
        _castStyle.BgColor = new Color(0.04f, 0.03f, 0.02f, 0.86f);
        _castStyle.BorderColor = new Color(1f, 0.85f, 0.45f, 0.55f);
        _castStyle.SetBorderWidthAll(1);
        _castStyle.SetCornerRadiusAll(7);

        _castBar = new Panel();
        _castBar.Name = "Cast";
        _castBar.MouseFilter = MouseFilterEnum.Ignore;
        _castBar.Visible = false;
        _castBar.AddThemeStyleboxOverride("panel", _castStyle);
        AddChild(_castBar);

        _castText = MakeLabel(14, HorizontalAlignment.Center);
        _castText.AddThemeColorOverride("font_color", new Color(1f, 0.88f, 0.45f));
        _castText.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _castText.AddThemeConstantOverride("outline_size", 4);
        _castText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _castText.ClipText = true;
        _castBar.AddChild(_castText);
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

        // 悬停提示：没有它没人会猜"卡能点"
        TooltipText = u.Name + " · 点击查看详情";

        if (_board != null)
        {
            _board.Color = enemy ? UIThemeManager.CardEnemy : UIThemeManager.CardAlly;
        }

        if (_borderFrame != null)
        {
            // 纹样表达"什么货色"（品阶）
            CardBorderKind kind = CardBorderFactory.ForRarity(u != null ? u.Rarity : 0);
            _borderFrame.Texture = CardBorderFactory.Get(kind);
            // 平面线描边框自带金/墨/朱砂配色，**不做任何染色**：
            // 染成红或青都会毁掉国风线描的配色，敌我改由站位与卡牌底色区分。
            _borderFrame.Modulate = Colors.White;
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
            // L4 是素材自带的亮金回纹框：与主题无关、且比人物更抢眼。
            // 2026-11-16：素材自带的亮金回纹框已完全弃用（62 份逐像素重复、且与国风
            // 扁平线描不搭）。战斗卡 / 卡册 / 详情大卡三处统一走 CardBorderFactory
            // 的 6 套品阶线描纹样 + 环形实体衬边，这一层永久停用。
            if (i == 3)
            {
                _layers[i].Texture = null;
                _layers[i].Visible = false;
                continue;
            }

            string path = root + names[i] + ".png";
            if (!ResourceLoader.Exists(path))
            {
                _layers[i].Texture = null;
                if (i == 1 && _charOver != null)
                {
                    _charOver.Texture = null;
                }
                continue;
            }

            Texture2D tex = GD.Load<Texture2D>(path);

            if (i == 0)
            {
                // 不再压暗背景。原先的 (0.70,0.70,0.73) 是基于**线性帧缓冲误存的偏暗截图**
                // 做的过度补偿（见 FixShotSrgb.py）；在正确的 sRGB 画面下它会把源图
                // 的暖色山水压成发灰的一片，细节全丢，人物反而失去衬托。
                // 源图本身就是"人物为主、背景稍虚"的成品构图，交给它自己表达层次。
                _layers[i].Modulate = Colors.White;
            }

            // 人物层走溢出层：**不再**放进被裁剪的 _art 里，否则放大后被卡面裁掉就白做了
            if (i == 1 && _charOver != null)
            {
                _layers[i].Texture = null;
                _charOver.Texture = tex;
                continue;
            }

            _layers[i].Texture = tex;
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
            // 画区内缩：插画只从金线内圈开始，卡边那一圈由实体衬边 + 线描边框占据。
            // L1 相对 _art 还有 10px 视差余量，被 ClipContents 裁到内圈，不会露边。
            _art.Position = new Vector2(ArtInset, ArtInset);
            _art.Size = new Vector2(Size.X - ArtInset * 2f, Size.Y - ArtInset * 2f);
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
        LayoutCast();
        LayoutCharOver();
    }

    /// <summary>
    /// 人物溢出层：**按"可见画区"排版**（卡高减去底部信息带），整具人都露出来。
    /// <para>
    /// 2026-11-14 第二次改（玩家反馈"缺胳膊少腿"）：原来按整卡高 261 排版、底边对齐卡底，
    /// 但底部 84px 被信息带盖住 —— 于是人物永远在大腿处被截断，看起来就是"没腿"。
    /// 现在以 (卡高 − 信息带) 为基准定尺寸，脚落在信息带上沿再压 4px（不悬空），
    /// 头顶因此自然高出卡框几个像素，正好保留"略微破框"的效果。
    /// </para>
    /// </summary>
    void LayoutCharOver()
    {
        if (_charOver == null)
        {
            return;
        }

        // 按**整卡高**排版、底边对齐卡底：人物上半身因此足够大，
        // 小腿被底部信息带自然裁掉 —— 卡牌人物要"大"，不要求"全"。
        // （此前按可见画区排版并整体缩小来保住全身，结果人物读不出脸。）
        float h = Mathf.Round(Size.Y * CharScale);

        // 素材已裁到 alpha 边界：**纹理尺寸即人物尺寸**，宽高比随人而定。
        // 不能再用固定的 896/1152 —— 那是含留白时的比例，沿用会把人又缩回去。
        float aspect = CharAspect;
        if (_charOver.Texture != null)
        {
            aspect = (float)_charOver.Texture.GetWidth() / (float)_charOver.Texture.GetHeight();
        }

        // 尺寸与基准位置都吸附整数像素。破框的发梢/飘带是半透明细线，
        // 落在半像素上会被双线性重采样洗成"发虚"；取整后采样点对齐像素网格。
        // 视差本身仍是连续的（见 Animate），只是每帧绘制位置取整。
        float w = Mathf.Round(h * aspect);
        _charBase = new Vector2(
            Mathf.Round((Size.X - w) * 0.5f),
            Mathf.Round(Size.Y - h + CharFootOver));
        _charOver.Position = _charBase;
        _charOver.Size = new Vector2(w, h);
    }

    void LayoutBorder()
    {
        if (_frameBacking != null)
        {
            _frameBacking.Position = Vector2.Zero;
            _frameBacking.Size = Size;
        }

        if (_borderFrame == null)
        {
            return;
        }

        // 整幅卡框铺满卡面：AI 边框是固定比例的整幅图，不能用九宫格（会把浮雕纹样拉花）
        _borderFrame.Position = Vector2.Zero;
        _borderFrame.Size = Size;
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

    void LayoutCast()
    {
        if (_castBar == null)
        {
            return;
        }

        float w = Size.X - CastInset * 2f;
        _castBar.Position = new Vector2(CastInset, CastTop);
        _castBar.Size = new Vector2(w, CastBarH);
        SetRect(_castText, 4f, 0f, w - 8f, CastBarH);
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
        UpdateCast((float)delta);
    }

    // ============ 出手横幅 ============

    /// <summary>
    /// 报出这一手放的武学名与段数。<paramref name="hits"/> 是引擎结算出的实际段数（含重放），
    /// 大于 1 时写成「斩妖诀 ×3」—— 玩家不用去数飘字，一眼知道这一手打了几下。
    /// 同一行动里连放两门（主效果 + 副效果）时并排显示。
    /// </summary>
    public void ShowCast(string name, int hits)
    {
        if (string.IsNullOrEmpty(name) || _castBar == null)
        {
            return;
        }
        if (hits < 1)
        {
            hits = 1;
        }

        // 上一手已经过去（超过 0.3s）→ 重新开一条；0.3s 内的都算同一次行动
        if (_castLeft <= CastShowSec - 0.30f)
        {
            _castEntries.Clear();
        }
        AddCastEntry(name, hits);

        _castText.Text = BuildCastLine();
        _castLeft = CastShowSec;
        _castBar.Visible = true;
        ApplyCastAlpha(1f);
    }

    void AddCastEntry(string name, int hits)
    {
        for (int i = 0; i < _castEntries.Count; i++)
        {
            if (_castEntries[i].Name == name)
            {
                _castEntries[i].Hits += hits;
                return;
            }
        }

        // 卡面只放得下两门；第三门起不显示 —— 写了也读不出来，不如不加
        if (_castEntries.Count >= 2)
        {
            return;
        }

        CastEntry e = new CastEntry();
        e.Name = name;
        e.Hits = hits;
        _castEntries.Add(e);
    }

    string BuildCastLine()
    {
        string line = "";
        for (int i = 0; i < _castEntries.Count; i++)
        {
            if (i > 0)
            {
                line += " · ";
            }

            CastEntry e = _castEntries[i];
            line += e.Name;
            if (e.Hits > 1)
            {
                line += " ×" + e.Hits;
            }
        }
        return line;
    }

    void UpdateCast(float dt)
    {
        if (_castLeft <= 0f || _castBar == null)
        {
            return;
        }

        _castLeft -= dt;
        if (_castLeft <= 0f)
        {
            _castLeft = 0f;
            _castEntries.Clear();
            _castBar.Visible = false;
            return;
        }

        float alpha = 1f;
        if (_castLeft < CastFadeSec)
        {
            alpha = _castLeft / CastFadeSec;
        }
        ApplyCastAlpha(alpha);
    }

    void ApplyCastAlpha(float alpha)
    {
        Color c = _castBar.Modulate;
        c.A = alpha;
        _castBar.Modulate = c;
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

        // 人物溢出层跟着"人物"这一层的深度走 —— 它现在就是 L2，错开的话会看到两张人
        if (_charOver != null)
        {
            Vector2 charOff = new Vector2(-_tilt.X, -_tilt.Y) * (ShiftPx * Depth[1]);
            // 视差保持连续浮点：实测把每帧位置取整后静态帧锐度无改善，
            // 却会让 ±4.4px 的慢速摆动变成 1px 台阶跳动，得不偿失。
            // 只对布局时的尺寸/基准位置取整（见 LayoutCharOver）。
            _charOver.Position = _charBase + charOff;
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
