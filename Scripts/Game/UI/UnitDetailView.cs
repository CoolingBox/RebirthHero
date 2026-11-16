// 文件：UnitDetailView.cs
// 《轮回大侠》战斗界面 —— **卡牌详情浮层**（点战斗卡打开）
//
// 为什么单开一个浮层，而不是把字塞进卡里：
//   战斗卡只有 203×261，信息带占 84px，已经装了名字 / 血条 / 行动条 / 攻速 / 武学名。
//   再想加"等级、释放进度、释放次数、本层伤害"就只能靠缩字号 —— 那是把可读性换给密度。
//   详情是**按需展开**的：平时卡面保持一眼可读，想看细节时点一下，信息才铺开。
//
// 为什么是浮层（Control）而不是 BasePanel：
//   它不切换场景、不存档、不接导航，只覆盖在战斗画面上读实时数据。
//   做成 BasePanel 要多一份 .tscn + 绑定文件 + 一套 Show/Hide 生命周期，换不到任何东西。
//
// 为什么**不暂停**战斗：
//   "释放进度"「行动条」这类数字只有动起来才有意义。挂机游戏里玩家点开是"看看这队打得怎么样"，
//   不是"暂停研究"。数字跟着 10Hz 刷新（由 MainPanel 的节拍转发），关掉即停。
//
// 数据由 MainPanel 装配（它才持有修为等级与战果统计）；本类只认 UnitDetailData，
// 不碰 Cultivation / StatsTracker，也不认识 BattleEngine。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;   // BigNum（伤害/气血用同一套大数格式，别另写一份缩字逻辑）

/// <summary>详情里的一行武学。字段都是"已经算好、只等显示"的读数。</summary>
public sealed class UnitSkillRow
{
    public string Name = "";
    /// <summary>触发条件文案，如"每 4 次行动" / "受击 25%" / "开局光环"。</summary>
    public string Cond = "";
    /// <summary>条件进度，如"3/4"；概率型留空。</summary>
    public string Progress = "";
    public int Fires;
    /// <summary>0 = 不限次。</summary>
    public int MaxFires;
    public int Triggers;
    public double Damage;
}

/// <summary>详情浮层要显示的全部数字（一次装配，逐帧刷新时整份替换）。</summary>
public sealed class UnitDetailData
{
    public string Name = "";
    /// <summary>副标题：阵营 + 位次 + 状态。</summary>
    public string Subtitle = "";
    public string ArtKey = "";
    public bool Enemy;
    /// <summary>品阶：与战斗卡共用 CardBorderFactory 的纹样映射。</summary>
    public int Rarity = 1;

    /// <summary>等级行（我方 = 等级 + 修为进度；敌方 = 层段/档位）。</summary>
    public string LevelText = "";

    public double Hp;
    public double MaxHp;
    /// <summary>被召唤武学锁死的生命（画成血条右端灰段）。</summary>
    public double LockedHp;
    public double Shield;

    public double Atk;
    public double Spd;
    public double Def;
    public double Aggro;

    public double Gauge;
    public double GaugeMax = 1;
    public double SecondsToAct;

    /// <summary>战果统计只有我方有（StatsTracker 只归账玩家），敌方置 false。</summary>
    public bool ShowStats;
    public double Damage;
    public double Taken;
    public double Heal;
    public int Actions;
    public int Triggers;

    /// <summary>当前状态一行：毒/火/续治/破绽/沉默/增益。"当前无异常状态"表示干净。</summary>
    public string StatusText = "";

    public List<UnitSkillRow> Skills = new List<UnitSkillRow>();
}

public partial class UnitDetailView : Control
{
    /// <summary>关闭回调（点空白、点关闭按钮、开新一场时都会走）。</summary>
    public Action OnClosed;

    // —— 尺寸：1380×800 居中，左边一张 7:9 大卡，右边读数区 818 宽 ——
    const int PanelW = 1380;
    const int PanelH = 800;
    const int CardW = 470;
    const int CardH = 604;
    const int CardPad = 12;
    const int RightX = 534;
    const int RightW = 818;

    /// <summary>最多列几行武学；多出来的折成一行"还有 N 个"。</summary>
    const int SkillRowMax = 6;
    const float SkillRowH = 38f;
    const float SkillRowY = 452f;

    /// <summary>
    /// 人物头顶探出卡框的**绝对像素**。
    /// 原用 `CharScale = 1.11`（按"素材透明留白均值约 9%"反推，认为只探出 ~13px），
    /// 但 CropPortraits.py 把 L2 裁到 alpha 边界后留白没了，1.11 变成实打实探出
    /// 66px，头发顶到标题区。改用绝对像素，与卡册大卡、战斗卡同量级。
    /// </summary>
    const float CharOverhang = 16f;

    static readonly Color ScrimColor = new Color(0.02f, 0.02f, 0.03f, 0.80f);

    Panel _panel;
    StyleBoxFlat _panelStyle;
    ColorRect _scrim;
    Button _closeBtn;

    Label _title;
    Label _subtitle;
    Label _levelText;
    Label _hpText;
    Label _statText;
    Label _nextText;
    Label _gainTitle;
    Label _gainA;
    Label _gainB;
    Label _statusText;
    Label _skillTitle;
    Label _moreSkills;
    Label _hint;

    ColorRect _hpBack;
    ColorRect _hpFill;
    ColorRect _hpLocked;
    ColorRect _hpShield;
    ColorRect _gaugeBack;
    ColorRect _gaugeFill;
    ColorRect _sepA;
    ColorRect _sepB;

    Control _artHolder;
    ColorRect _artBack;
    readonly TextureRect[] _art = new TextureRect[4];
    /// <summary>不透明环形衬边，与战斗卡 / 卡册共用工厂装配。</summary>
    Panel _frameBacking;

    /// <summary>人物溢出层：不在被裁剪的 _artHolder 里，所以头/发/兵器能越出卡框。</summary>
    TextureRect _charOver;

    readonly Label[] _skillRows = new Label[SkillRowMax];

    string _artKey = "";
    bool _built;

    public bool IsOpen
    {
        get { return Visible; }
    }

    // ============ 建节点（幂等） ============

    /// <summary>
    /// 建节点只做一次。**不放在 _Ready**：本浮层由 MainPanel 在打开时 lazy new 出来，
    /// 那时父节点可能刚入树，_Ready 会被推迟（同 BattleParallaxCard 的踩坑记录）。
    /// </summary>
    void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }
        _built = true;

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        UIThemeManager.ApplyTo(this);

        BuildScrim();
        BuildPanel();
        BuildArt();

        ApplyColors();

        // 尺寸在 FullRect 生效之后才知道 —— 居中放到事件里做，别在构造时读 Size（那时还是 0）
        Resized += CenterPanel;
        CenterPanel();
    }

    void BuildScrim()
    {
        _scrim = new ColorRect();
        _scrim.Name = "Scrim";
        _scrim.Color = ScrimColor;
        _scrim.MouseFilter = MouseFilterEnum.Stop;   // 吃掉点击：点空白处关掉浮层
        _scrim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _scrim.GuiInput += OnScrimInput;
        AddChild(_scrim);
    }

    void BuildPanel()
    {
        _panelStyle = new StyleBoxFlat();
        _panelStyle.SetCornerRadiusAll(10);

        _panel = new Panel();
        _panel.Name = "Panel";
        _panel.MouseFilter = MouseFilterEnum.Stop;   // 面板内的点击不再传给背后的战斗
        _panel.Position = new Vector2((1920 - PanelW) * 0.5f, (1080 - PanelH) * 0.5f);
        _panel.Size = new Vector2(PanelW, PanelH);
        _panel.AddThemeStyleboxOverride("panel", _panelStyle);
        AddChild(_panel);

        _title = MakeLabel(_panel, 32, new Vector2(28, 16), new Vector2(900, 44));
        _subtitle = MakeLabel(_panel, 20, new Vector2(30, 62), new Vector2(900, 26));

        // 人物破框后头发可能伸到标题区：把标题提到人物层之上，保证名字永远读得到
        _title.ZIndex = 2;
        _subtitle.ZIndex = 2;

        _closeBtn = new Button();
        _closeBtn.Text = "关闭 ✕";
        _closeBtn.Position = new Vector2(PanelW - 150f, 22f);
        _closeBtn.Size = new Vector2(120f, 42f);
        _closeBtn.FocusMode = Control.FocusModeEnum.None;   // 焦点环会跟鼠标抢视觉
        _closeBtn.Pressed += Close;
        _panel.AddChild(_closeBtn);

        _levelText = MakeLabel(_panel, 24, new Vector2(RightX, 104), new Vector2(RightW, 30));

        // —— 气血条 ——
        _hpBack = MakeRect(_panel, new Vector2(RightX, 142), new Vector2(RightW, 26));
        _hpFill = MakeRect(_panel, new Vector2(RightX, 142), new Vector2(RightW, 26));
        _hpLocked = MakeRect(_panel, new Vector2(RightX, 142), new Vector2(0, 26));
        _hpShield = MakeRect(_panel, new Vector2(RightX, 142), new Vector2(0, 26));
        _hpText = MakeLabel(_panel, 18, new Vector2(RightX, 142), new Vector2(RightW, 26));
        _hpText.HorizontalAlignment = HorizontalAlignment.Center;
        _hpText.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.96f));
        _hpText.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _hpText.AddThemeConstantOverride("outline_size", 4);

        _statText = MakeLabel(_panel, 21, new Vector2(RightX, 180), new Vector2(RightW, 28));

        // —— 行动条 ——
        _gaugeBack = MakeRect(_panel, new Vector2(RightX, 222), new Vector2(RightW, 16));
        _gaugeFill = MakeRect(_panel, new Vector2(RightX, 222), new Vector2(RightW, 16));
        _nextText = MakeLabel(_panel, 20, new Vector2(RightX, 244), new Vector2(RightW, 28));

        _sepA = MakeRect(_panel, new Vector2(RightX, 284), new Vector2(RightW, 1));

        // —— 本层战果 ——
        _gainTitle = MakeLabel(_panel, 22, new Vector2(RightX, 296), new Vector2(RightW, 30));
        _gainA = MakeLabel(_panel, 21, new Vector2(RightX, 330), new Vector2(RightW, 28));
        _gainB = MakeLabel(_panel, 21, new Vector2(RightX, 356), new Vector2(RightW, 28));

        _statusText = MakeLabel(_panel, 20, new Vector2(RightX, 388), new Vector2(RightW, 28));
        _statusText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _statusText.ClipText = true;

        _sepB = MakeRect(_panel, new Vector2(RightX, 418), new Vector2(RightW, 1));

        // —— 武学明细 ——
        _skillTitle = MakeLabel(_panel, 22, new Vector2(RightX, 428), new Vector2(RightW, 30));
        for (int i = 0; i < SkillRowMax; i++)
        {
            float y = SkillRowY + i * SkillRowH;
            _skillRows[i] = MakeLabel(_panel, 19, new Vector2(RightX, y), new Vector2(RightW, SkillRowH - 4));
        }
        _moreSkills = MakeLabel(_panel, 19, new Vector2(RightX, SkillRowY + SkillRowMax * SkillRowH),
                                new Vector2(RightW, 26));

        _hint = MakeLabel(_panel, 19, new Vector2(28, PanelH - 44f), new Vector2(700, 26));
        _hint.Text = "点面板外或「关闭」返回 · 数字随战斗实时刷新";
    }

    void BuildArt()
    {
        _artHolder = new Control();
        _artHolder.Name = "Art";
        _artHolder.Position = new Vector2(28, 104);
        _artHolder.Size = new Vector2(CardW, CardH);
        _artHolder.ClipContents = true;
        _artHolder.MouseFilter = MouseFilterEnum.Ignore;
        _panel.AddChild(_artHolder);

        _artBack = new ColorRect();
        _artBack.Color = UIThemeManager.CardAlly;
        _artBack.Position = Vector2.Zero;
        _artBack.Size = new Vector2(CardW, CardH);
        _artBack.MouseFilter = MouseFilterEnum.Ignore;
        _artHolder.AddChild(_artBack);

        for (int i = 0; i < 4; i++)
        {
            if (i == 3)
            {
                // 线描卡框是镂空的，先垫一圈实体衬边挡住背景外沿。
                // 详情大卡 470 宽，取 5%（≈24px），与战斗卡 12/203 同比例。
                _frameBacking = CardBorderFactory.CreateBacking(24f);
                _frameBacking.Position = Vector2.Zero;
                _frameBacking.Size = new Vector2(CardW, CardH);
                _artHolder.AddChild(_frameBacking);
            }

            TextureRect r = new TextureRect();
            r.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            r.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            r.MouseFilter = MouseFilterEnum.Ignore;
            // 素材 896×1152 显示成 470×604（缩 1.9 倍），必须带 mipmap，否则发丝花纹会抖
            r.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

            // 只有最底层的远背景留余量；其余层用卡面尺寸（边缘透明，露出的是下层）
            float pad = i == 0 ? CardPad : 0f;
            r.Position = new Vector2(-pad, -pad);
            r.Size = new Vector2(CardW + pad * 2f, CardH + pad * 2f);

            _artHolder.AddChild(r);
            _art[i] = r;
        }

        // 人物破框：L2 单独挂在 _panel 上（不在被裁剪的 _artHolder 里），放大后压在卡框之上
        _charOver = CardArtFx.MakeCharOverlay("CharOver");
        _panel.AddChild(_charOver);
        LayoutCharOver();
    }

    /// <summary>
    /// 按 L2 纹理**真实比例**排版人物溢出层。
    /// CropPortraits.py 之后 L2 已裁到各自 alpha 边界，宽高比 0.46~0.58 各异；
    /// 若把宽高都设成卡面的旧 CharScale 倍（521×670，aspect 0.778），
    /// KeepAspectCovered 会按宽度放大并上下各裁约 20%，正好把头切掉。
    /// 战斗卡与卡册都已按此修正，这里是第三处调用点。
    /// </summary>
    void LayoutCharOver()
    {
        if (_charOver == null)
        {
            return;
        }

        float h = CardH + CharOverhang;
        float aspect = 7f / 9f;
        if (_charOver.Texture != null)
        {
            Texture2D tex = _charOver.Texture;
            aspect = (float)tex.GetWidth() / (float)tex.GetHeight();
        }

        float w = h * aspect;
        _charOver.Position = new Vector2(28f + (CardW - w) * 0.5f, 104f + CardH - h);
        _charOver.Size = new Vector2(w, h);
    }

    void BuildRightColumn()
    {
        // 右栏节点在 BuildPanel 里一次建完（顺序就是绘制顺序），这里不重复。
    }

    /// <summary>面板居中：屏幕尺寸变了也跟着走（1920×1080 是设计分辨率，实机可能不同）。</summary>
    void CenterPanel()
    {
        if (_panel == null)
        {
            return;
        }

        Vector2 area = Size;
        if (area.X < 2f || area.Y < 2f)
        {
            area = new Vector2(1920, 1080);
        }

        _panel.Position = new Vector2((area.X - PanelW) * 0.5f, (area.Y - PanelH) * 0.5f);
    }

    Label MakeLabel(Node parent, int fontSize, Vector2 pos, Vector2 size)
    {
        Label l = new Label();
        l.MouseFilter = MouseFilterEnum.Ignore;
        l.Position = pos;
        l.Size = size;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.AddThemeFontSizeOverride("font_size", fontSize);
        parent.AddChild(l);
        return l;
    }

    ColorRect MakeRect(Node parent, Vector2 pos, Vector2 size)
    {
        ColorRect r = new ColorRect();
        r.MouseFilter = MouseFilterEnum.Ignore;
        r.Position = pos;
        r.Size = size;
        parent.AddChild(r);
        return r;
    }

    // ============ 配色（随主题） ============

    public void ApplyColors()
    {
        if (!_built)
        {
            return;
        }

        _panelStyle.BgColor = UIThemeManager.BgBase;
        _panelStyle.BorderColor = UIThemeManager.PanelBorder;
        _panelStyle.SetBorderWidthAll(2);

        _title.AddThemeColorOverride("font_color", UIThemeManager.TextPrimary);
        _subtitle.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        _levelText.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        _statText.AddThemeColorOverride("font_color", UIThemeManager.TextSecondary);
        _nextText.AddThemeColorOverride("font_color", UIThemeManager.TextSecondary);
        _gainTitle.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        _gainA.AddThemeColorOverride("font_color", UIThemeManager.TextPrimary);
        _gainB.AddThemeColorOverride("font_color", UIThemeManager.TextPrimary);
        _statusText.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        _skillTitle.AddThemeColorOverride("font_color", UIThemeManager.Accent);
        _moreSkills.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);
        _hint.AddThemeColorOverride("font_color", UIThemeManager.TextMuted);

        _sepA.Color = UIThemeManager.Separator;
        _sepB.Color = UIThemeManager.Separator;
        _hpBack.Color = UISignalColors.HpBack;
        _gaugeBack.Color = UISignalColors.GaugeBack;

        for (int i = 0; i < SkillRowMax; i++)
        {
            _skillRows[i].AddThemeColorOverride("font_color", UIThemeManager.TextSecondary);
        }
    }

    // ============ 开 / 关 / 刷新 ============

    public void Open(UnitDetailData data)
    {
        EnsureBuilt();
        ApplyColors();
        Visible = true;
        Apply(data);
    }

    public void Close()
    {
        if (!Visible)
        {
            return;
        }

        Visible = false;
        _artKey = "";   // 下次打开重新贴图（换了一张卡）

        if (OnClosed != null)
        {
            OnClosed();
        }
    }

    void OnScrimInput(InputEvent @event)
    {
        InputEventMouseButton mb = @event as InputEventMouseButton;
        if (mb == null || !mb.Pressed || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        Close();
    }

    /// <summary>把一份读数刷到界面上（10Hz 由 MainPanel 转发）。</summary>
    public void Apply(UnitDetailData d)
    {
        if (!_built || d == null)
        {
            return;
        }

        _title.Text = d.Name;
        _subtitle.Text = d.Subtitle;
        _levelText.Text = d.LevelText;

        ApplyBorder(d.Rarity);

        // 血条：满格 = BaseHp + LockedHp（"原本该有的最大生命"），锁死那截单独画灰段
        double total = d.MaxHp + d.LockedHp;
        double frac = 1.0;
        if (total > 0)
        {
            frac = d.Hp / total;
        }
        frac = Math.Clamp(frac, 0.0, 1.0);

        double fillW = RightW * frac;
        _hpFill.Position = new Vector2(RightX, 142);
        _hpFill.Size = new Vector2((float)fillW, 26);
        _hpFill.Color = HpColor(frac);

        double lockW = RightW * (total > 0 ? d.LockedHp / total : 0.0);
        _hpLocked.Position = new Vector2(RightX + RightW - (float)lockW, 142);
        _hpLocked.Size = new Vector2((float)lockW, 26);
        _hpLocked.Color = UISignalColors.HpLocked;

        // 护盾金色蒙层：只说"占血条多少"，具体数值写在下面的文字里
        double shieldW = RightW * (total > 0 ? d.Shield / total : 0.0);
        if (shieldW > RightW)
        {
            shieldW = RightW;
        }
        _hpShield.Position = new Vector2(RightX, 142);
        _hpShield.Size = new Vector2((float)shieldW, 26);
        _hpShield.Color = UISignalColors.ShieldGoldFill;

        string hpLine = "气血 " + BigNum.From(d.Hp).ToString(2) + " / " + BigNum.From(d.MaxHp).ToString(2);
        if (d.LockedHp > 0)
        {
            hpLine += "（锁 " + BigNum.From(d.LockedHp).ToString(2) + "）";
        }
        if (d.Shield > 0)
        {
            hpLine += "　🛡 " + BigNum.From(d.Shield).ToString(2);
        }
        _hpText.Text = hpLine;

        _statText.Text = "攻击 " + BigNum.From(d.Atk).ToString(2)
                       + "　｜　速度 " + d.Spd.ToString("0")
                       + "　｜　防御 " + BigNum.From(d.Def).ToString(2)
                       + "　｜　仇恨 " + d.Aggro.ToString("0");

        double gfrac = d.GaugeMax > 0 ? Math.Clamp(d.Gauge / d.GaugeMax, 0.0, 1.0) : 0.0;
        _gaugeFill.Position = new Vector2(RightX, 222);
        _gaugeFill.Size = new Vector2((float)(RightW * gfrac), 16);
        _gaugeFill.Color = UISignalColors.GaugeFill;

        _nextText.Text = "行动条 " + (gfrac * 100).ToString("0") + "%"
                       + "　｜　" + d.SecondsToAct.ToString("0.0") + " 秒后出手";

        if (d.ShowStats)
        {
            _gainTitle.Text = "本层战果";
            _gainA.Text = "伤害 " + BigNum.From(d.Damage).ToString(2)
                        + "　｜　承伤 " + BigNum.From(d.Taken).ToString(2)
                        + "　｜　治疗 " + BigNum.From(d.Heal).ToString(2);
            _gainB.Text = "行动 " + d.Actions + " 次"
                        + "　｜　武学触发 " + d.Triggers + " 次";
        }
        else
        {
            _gainTitle.Text = "本层战果";
            _gainA.Text = "战果只统计我方侠客（敌方单位不归账）。";
            _gainB.Text = "行动 " + d.Actions + " 次（含普攻与武学）";
        }

        _skillTitle.Text = "武学 · " + d.Skills.Count + " 门";
        _statusText.Text = d.StatusText;

        int shown = d.Skills.Count;
        if (shown > SkillRowMax)
        {
            shown = SkillRowMax;
        }

        for (int i = 0; i < SkillRowMax; i++)
        {
            Label row = _skillRows[i];
            if (i < shown)
            {
                row.Visible = true;
                row.Text = SkillLine(d.Skills[i], d.ShowStats);
            }
            else row.Visible = false;
        }

        int rest = d.Skills.Count - shown;
        _moreSkills.Visible = rest > 0;
        if (rest > 0)
        {
            _moreSkills.Text = "… 还有 " + rest + " 门（详情仅列前 " + SkillRowMax + " 门）";
        }

        if (_artBack != null)
        {
            _artBack.Color = d.Enemy ? UIThemeManager.CardEnemy : UIThemeManager.CardAlly;
        }
        LoadArt(d.ArtKey);
    }

    /// <summary>
    /// 一行武学的文案：名字 + 条件 + 条件进度 + 已放/上限 + 本层触发与伤害。
    /// 用全角竖线分隔而不是多列对齐：中文字宽不等，列对齐反而会读出"参差"。
    /// </summary>
    static string SkillLine(UnitSkillRow s, bool showStats)
    {
        string line = "「" + s.Name + "」";
        if (!string.IsNullOrEmpty(s.Cond))
        {
            line += "　" + s.Cond;
        }
        if (!string.IsNullOrEmpty(s.Progress))
        {
            line += " " + s.Progress;
        }

        line += "　｜　已放 " + s.Fires;
        if (s.MaxFires > 0)
        {
            line += "/" + s.MaxFires;
        }
        else line += "/∞";

        if (showStats)
        {
            line += "　｜　触发 " + s.Triggers
                  + "　｜　伤 " + BigNum.From(s.Damage).ToString(2);
        }

        return line;
    }

    static Color HpColor(double frac)
    {
        if (frac <= 0.25)
        {
            return UISignalColors.HpBad;
        }
        if (frac <= 0.50)
        {
            return UISignalColors.HpWarn;
        }
        return UISignalColors.HpGood;
    }

    /// <summary>贴卡面（同一个素材不重复读）。缺层就留空 —— 不硬套别的卡的框。</summary>
    void LoadArt(string key)
    {
        if (string.IsNullOrEmpty(key) || key == _artKey)
        {
            return;
        }
        _artKey = key;

        for (int i = 0; i < 3; i++)
        {
            string path = BattleArt.Root + key + "/L" + (i + 1) + ".png";
            if (!ResourceLoader.Exists(path))
            {
                _art[i].Texture = null;
                if (i == 1 && _charOver != null)
                {
                    _charOver.Texture = null;
                }
                continue;
            }

            Texture2D tex = GD.Load<Texture2D>(path);

            // 人物层走溢出层，不放进裁剪区
            if (i == 1 && _charOver != null)
            {
                _art[i].Texture = null;
                _charOver.Texture = tex;
                continue;
            }

            _art[i].Texture = tex;
        }

        // 纹理换了，宽高比可能不同，必须重排人物层（BuildArt 时 Texture 还是 null）
        LayoutCharOver();
    }

    /// <summary>第 4 层卡框与战斗卡共用 CardBorderFactory 的 6 套品阶纹样。</summary>
    void ApplyBorder(int rarity)
    {
        CardBorderKind kind = CardBorderFactory.ForRarity(rarity);
        _art[3].Texture = CardBorderFactory.Get(kind);
    }
}
