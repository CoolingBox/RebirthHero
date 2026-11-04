// 文件：TalentPanel.cs
// 《轮回大侠》M3 天赋树 —— **独立窗口**（BasePanel 子类，由 UIManager 开关）
//
// 拓扑（用户 2026-11-02 三改）：**主干 3 层 → 分叉两条子路 → 相邻两支子路末端汇到交汇节点**。
// 参考 PoE 被动树的公开设计说明（"suburbs and roads"）：
//   · cluster（簇）= 一组相关节点，构筑主体；pathway（路）= 过路节点，只收过路费
//   · notable / keystone = 簇心大节点 / 改机制的关键节点
//   · sector 边界上的节点是混合型 —— "交错"就是这么来的
// 本树落到 4 向布局：每个方向一个 sector，主干 3 层（可重复点的小节点），
// 第 3 层分叉成两条子路（各 2 个一次性 notable），相邻两支的子路末端共享一个交汇节点
// （交汇 = 同时放大两支的百分比效果，要求两边都走到）。
//
// 坐标系：极坐标。分支决定基准角（上 270° / 右 0° / 下 90° / 左 180°），
//   Path 决定偏角（子路 ±40°，交汇 +45°），Depth 决定半径。画布 1100×1100，中心 550。
//
// **可拖动 + 可缩放**（用户："UI 可拖动（这样天赋多了就不怕挤）"）：
//   节点 Button 必须设 MouseFilter = Pass —— 默认 Stop 会把事件吃掉、父级收不到，
//   拖动就失效。拖动/滚轮由 TView 的 GuiInput 统一处理。
//
// 视觉与逻辑分离：本文件只画和点，所有规则（成本/前置/等级上限/洗点退款）都在 Talent.cs。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class TalentPanel : BasePanel
{
    // —— 配色 ——
    static readonly Color ColOn      = new Color(1.00f, 0.82f, 0.34f);   // 已点（金）
    static readonly Color ColReady   = new Color(0.80f, 0.86f, 0.94f);   // 可点（亮）
    static readonly Color ColLock    = new Color(0.36f, 0.40f, 0.47f);   // 前置未到（暗）
    static readonly Color ColMech    = new Color(0.48f, 0.86f, 0.82f);   // 机制节点（青）
    static readonly Color ColRune    = new Color(0.86f, 0.62f, 0.95f);   // 交汇节点（紫）
    static readonly Color LineOn     = new Color(1.00f, 0.80f, 0.38f, 0.90f);
    static readonly Color LineOff    = new Color(0.32f, 0.36f, 0.43f, 0.80f);

    // —— 画布与布局（TTree 本地坐标）——
    // 45 节点 = 主干 3 + 子路 3×2 + 交汇 + 外环，半径最深 660 → 画布 1400×1400。
    // 视口只有 1020×800，所以整个视图默认缩到 0.62 看全貌，滚轮放大看细节（PoE 同款体验）。
    const float CenterX = 700f;
    const float CenterY = 700f;
    const float NodeSize = 60f;
    const float LineW = 4f;
    const float MinScale = 0.4f;
    const float MaxScale = 1.3f;

    // —— 外部注入 ——
    static Dao _ctxDao;
    static Action _ctxOnClose;

    public static void BindData(Dao dao, Action onClose)
    {
        _ctxDao = dao;
        _ctxOnClose = onClose;
    }

    Dao _dao;
    Action _onClose;
    readonly Dictionary<int, Button> _nodeBtn = new Dictionary<int, Button>();
    readonly Dictionary<int, List<ColorRect>> _nodeLines = new Dictionary<int, List<ColorRect>>();
    bool _built;
    bool _wired;
    bool _dragging;
    int _tree = 1;          // 当前查看的是第几棵树（1 武学 / 2 江湖）
    float _scale = 0.62f;
    int _selected;
    int _resetArmed;

    // 共享样式（只读，33 个节点复用同一批实例，避免每次刷新建 100+ 个对象）
    StyleBoxFlat _sbLock;
    StyleBoxFlat _sbReady;
    StyleBoxFlat _sbOn;
    StyleBoxFlat _sbMech;
    StyleBoxFlat _sbRune;

    public override void OnShow()
    {
        base.OnShow();
        _dao = _ctxDao;
        _onClose = _ctxOnClose;
        _resetArmed = 0;

        // 洗点 / 读档之后「江湖」可能重新上锁，视图必须退回「武学」—— 否则会显示一张点不动的空树
        if (!Talent.IsUnlocked(_tree))
        {
            _tree = 1;
        }

        if (!_wired)
        {
            Wire();
            _wired = true;
        }

        if (!_built)
        {
            BuildStyles();
            BuildView();
            BuildTree();
            _built = _TTree != null && _nodeBtn.Count > 0;
            if (!_built)
            {
                SetHint("天赋树没有建起来：TTree 绑定失败或天赋表未加载（看控制台 [ConfigManager] 报错）");
            }
        }

        if (_built)
        {
            CenterView();
            Callable.From(CenterView).CallDeferred();
        }
        Refresh();
    }

    void Wire()
    {
        if (_TCloseBtn != null)
        {
            _TCloseBtn.Pressed += OnClosePressed;
        }
        if (_TCenterBtn != null)
        {
            _TCenterBtn.Pressed += OnCenterPressed;
        }
        if (_TResetBtn != null)
        {
            _TResetBtn.Pressed += OnResetPressed;
        }
        if (_TTree1Btn != null)
        {
            _TTree1Btn.Pressed += OnTree1Pressed;
        }
        if (_TTree2Btn != null)
        {
            _TTree2Btn.Pressed += OnTree2Pressed;
        }
        if (_TView != null)
        {
            _TView.GuiInput += OnViewInput;
        }
    }

    void OnClosePressed()
    {
        AudioManager.PlayUISFX("ui_click");
        if (_onClose != null)
        {
            _onClose();
        }
    }

    void OnCenterPressed()
    {
        AudioManager.PlayUISFX("ui_click");
        CenterView();
    }

    void OnResetPressed()
    {
        if (_dao == null)
        {
            return;
        }

        if (_resetArmed == 0)
        {
            _resetArmed = 1;
            if (_TResetBtn != null)
            {
                _TResetBtn.Text = "确定重塑？";
            }
            SetHint("再点一次「确定重塑」：全部天赋清空，道行**全额**退回");
            return;
        }

        _resetArmed = 0;
        if (_TResetBtn != null)
        {
            _TResetBtn.Text = "重塑 ✎";
        }

        double refund = Talent.Respec(_dao);
        AudioManager.PlayUISFX("ui_confirm");
        SetHint("已重塑：退回 " + refund.ToString("0.#") + " 道行");
        // 洗点会把「江湖」树一起清空 → 它重新上锁，视图退回「武学」，否则玩家会盯着一张空树
        _tree = 1;
        BuildTree();
        CenterView();
        Refresh();
    }

    // ============ 两棵树之间切换 ============

    void OnTree1Pressed()
    {
        SwitchTree(1);
    }

    void OnTree2Pressed()
    {
        if (!Talent.IsUnlocked(2))
        {
            AudioManager.PlayUISFX("ui_cancel");
            SetHint("「江湖」要点满「武学」才会出现 —— 还差 " + NodesLeft(1) + " 个节点的最后一级");
            return;
        }
        SwitchTree(2);
    }

    void SwitchTree(int tree)
    {
        if (tree == _tree)
        {
            AudioManager.PlayUISFX("ui_click");
            return;
        }

        _tree = tree;
        _selected = 0;
        AudioManager.PlayUISFX("ui_click");
        HideTip();
        BuildTree();
        CenterView();
        Callable.From(CenterView).CallDeferred();   // 视口 Size 首帧可能还是 0，延后一帧再对齐
        Refresh();
    }

    /// <summary>该树还有多少个节点没点满（用于"还差几个"的提示）。</summary>
    static int NodesLeft(int tree)
    {
        int n = 0;
        foreach (TalentConfig c in Talent.NodesOf(tree))
        {
            if (Talent.LevelOf(c.Id) < c.MaxLevel)
            {
                n++;
            }
        }
        return n;
    }

    // ============ 拖动 / 缩放 ============

    void OnViewInput(InputEvent e)
    {
        InputEventMouseButton mb = e as InputEventMouseButton;
        if (mb != null)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = mb.Pressed;
                return;
            }
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
            {
                ZoomAt(1.12f);
                return;
            }
            if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
            {
                ZoomAt(1f / 1.12f);
                return;
            }
            return;
        }

        InputEventMouseMotion mm = e as InputEventMouseMotion;
        if (mm != null && _dragging)
        {
            PanBy(mm.Relative);
        }
    }

    void PanBy(Vector2 delta)
    {
        if (_TTree == null)
        {
            return;
        }
        HideTip();   // 拖动时浮窗跟着鼠标乱跑，先收起来
        // 鼠标位移是屏幕像素；树本身被 scale 缩放过，但 Position 属于父坐标系，直接加即可
        _TTree.Position = _TTree.Position + delta;
        ClampView();
    }

    void ZoomAt(float mul)
    {
        if (_TTree == null || _TView == null)
        {
            return;
        }
        float before = _scale;
        _scale = Mathf.Clamp(_scale * mul, MinScale, MaxScale);
        if (Mathf.Abs(_scale - before) < 0.001f)
        {
            return;
        }

        // 以视口中心为锚点缩放：位置按比例补偿，避免"越放大越跑偏"
        Vector2 vp = ViewSize();
        Vector2 center = vp * 0.5f;
        Vector2 local = (center - _TTree.Position) / before;
        _TTree.Scale = new Vector2(_scale, _scale);
        _TTree.Position = center - local * _scale;
        ClampView();
    }

    void CenterView()
    {
        if (_TView == null || _TTree == null)
        {
            return;
        }
        _TTree.Scale = new Vector2(_scale, _scale);
        Vector2 vp = ViewSize();
        _TTree.Position = new Vector2(vp.X * 0.5f - CenterX * _scale, vp.Y * 0.5f - CenterY * _scale);
        ClampView();
    }

    Vector2 ViewSize()
    {
        Vector2 vp = _TView != null ? _TView.Size : Vector2.Zero;
        if (vp.X < 10f || vp.Y < 10f)
        {
            // 视口还没布局完（Size 为 0）时用 tscn 设计尺寸兜底，否则树会被推到左上角
            vp = new Vector2(1020f, 800f);
        }
        return vp;
    }

    void ClampView()
    {
        if (_TView == null || _TTree == null)
        {
            return;
        }
        Vector2 vp = ViewSize();
        Vector2 scaled = _TTree.Size * _scale;

        Vector2 p = _TTree.Position;

        // 缩放后比视口小：**居中**（不能 clamp 到 0 —— 那会把树钉在左上角）
        if (scaled.X <= vp.X)
        {
            p.X = (vp.X - scaled.X) * 0.5f;
        }
        else
        {
            p.X = Mathf.Clamp(p.X, vp.X - scaled.X, 0f);
        }

        if (scaled.Y <= vp.Y)
        {
            p.Y = (vp.Y - scaled.Y) * 0.5f;
        }
        else
        {
            p.Y = Mathf.Clamp(p.Y, vp.Y - scaled.Y, 0f);
        }

        _TTree.Position = p;
    }

    // ============ 样式 ============

    void BuildStyles()
    {
        _sbLock = MakeStyle(new Color(0.14f, 0.16f, 0.20f), ColLock, 2);
        _sbReady = MakeStyle(new Color(0.20f, 0.24f, 0.30f), ColReady, 2);
        _sbOn = MakeStyle(new Color(0.32f, 0.26f, 0.12f), ColOn, 3);
        _sbMech = MakeStyle(new Color(0.12f, 0.26f, 0.26f), ColMech, 3);
        _sbRune = MakeStyle(new Color(0.24f, 0.14f, 0.30f), ColRune, 3);
    }

    /// <summary>圆节点：60×60 + 圆角 30 = 正圆（PoE 那种发光圆点），边框做状态区分。</summary>
    static StyleBoxFlat MakeStyle(Color bg, Color border, int width)
    {
        StyleBoxFlat sb = new StyleBoxFlat();
        sb.BgColor = bg;
        sb.BorderColor = border;
        sb.BorderWidthLeft = width;
        sb.BorderWidthTop = width;
        sb.BorderWidthRight = width;
        sb.BorderWidthBottom = width;
        sb.CornerRadiusTopLeft = 30;
        sb.CornerRadiusTopRight = 30;
        sb.CornerRadiusBottomLeft = 30;
        sb.CornerRadiusBottomRight = 30;
        return sb;
    }

    // ============ 造树 ============

    void BuildView()
    {
        if (_TView == null)
        {
            return;
        }
        _TView.MouseFilter = Control.MouseFilterEnum.Pass;
    }

    void BuildTree()
    {
        if (_TTree == null)
        {
            return;
        }

        // 重入保护：绑定失败后重试时，先把上一轮的子节点清掉，避免重叠两套
        foreach (Node old in _TTree.GetChildren())
        {
            _TTree.RemoveChild(old);
            old.QueueFree();
        }
        _nodeBtn.Clear();
        _nodeLines.Clear();

        // ① 连线：每个节点连到它的 ReqA / ReqB（交汇节点有两根）
        //    只画**当前这棵树**的节点 —— 两棵树的 Id 空间不重叠，混着画会连出跨树的鬼线
        foreach (TalentConfig c in Talent.NodesOf(_tree))
        {
            List<ColorRect> list = new List<ColorRect>();
            AddLink(list, c.ReqA, c);
            AddLink(list, c.ReqB, c);
            if (list.Count > 0)
            {
                _nodeLines[c.Id] = list;
            }
        }

        // ② 节点
        foreach (TalentConfig c in Talent.NodesOf(_tree))
        {
            Vector2 p = PosOf(c);

            Button b = new Button();
            b.CustomMinimumSize = new Vector2(NodeSize, NodeSize);
            b.Size = new Vector2(NodeSize, NodeSize);
            b.Position = new Vector2(p.X - NodeSize * 0.5f, p.Y - NodeSize * 0.5f);
            b.ClipText = false;
            b.AddThemeFontSizeOverride("font_size", 14);

            // Pass：按钮自己响应点击，同时把事件上抛给 TView（拖动靠它）
            b.MouseFilter = Control.MouseFilterEnum.Pass;

            b.Pressed += () => OnNodePressed(c.Id);
            b.MouseEntered += () => ShowNodeInfo(c.Id);
            b.MouseExited += HideTip;

            _TTree.AddChild(b);
            _nodeBtn[c.Id] = b;
        }

        GD.Print("[Talent] 建树完成：" + _nodeBtn.Count + " 节点 · 画布 " + _TTree.Size
                 + " · 视口 " + (_TView != null ? _TView.Size.ToString() : "null"));
    }

    void AddLink(List<ColorRect> list, int fromId, TalentConfig child)
    {
        if (fromId <= 0)
        {
            return;
        }
        TalentConfig parent = Talent.Get(fromId);
        if (parent == null)
        {
            return;
        }
        ColorRect line = MakeLine(PosOf(parent), PosOf(child));
        _TTree.AddChild(line);
        list.Add(line);
    }

    /// <summary>
    /// 节点中心坐标（极坐标 → 直角坐标）。
    /// 分支 = 基准角（上 270° / 右 0° / 下 90° / 左 180°，屏幕 y 向下）；
    /// 第 1 棵（武学）：主干 90/165/240、子路 ±40° 且 320/400/480、交汇 560、外环 660；
    /// 第 2 棵（江湖）：**同心圆** —— 只有 Depth，半径 170/285/400/515，四支角度同第 1 棵。
    /// 两棵树故意长得不一样：一个是从内往四方发力，一个是从自身往外扩到江湖。
    /// </summary>
    static Vector2 PosOf(TalentConfig c)
    {
        if (c.Branch <= 0)
        {
            return new Vector2(CenterX, CenterY);   // 中心「初心」/「入世」
        }

        float baseDeg = BranchDeg(c.Branch);
        float deg = baseDeg;
        float r;

        if (c.Tree == 2)
        {
            r = 170f + 115f * (c.Depth - 1);        // 170 / 285 / 400 / 515
        }
        else if (c.Path == 9)
        {
            // 交汇：落在相邻两支的中间角，比子路末端更外一圈
            deg = baseDeg + 45f;
            r = 560f;
        }
        else if (c.Path == 8)
        {
            // 外环「四象」：正方向最外侧，两条线从左右两个交汇斜上来
            r = 660f;
        }
        else if (c.Path == 0)
        {
            r = 90f + 75f * (c.Depth - 1);          // 90 / 165 / 240
        }
        else
        {
            deg = baseDeg + (c.Path == 1 ? -40f : 40f);
            r = 320f + 80f * (c.Depth - 1);         // 320 / 400 / 480
        }

        float rad = deg * Mathf.Pi / 180f;
        return new Vector2(CenterX + Mathf.Cos(rad) * r, CenterY + Mathf.Sin(rad) * r);
    }

    static float BranchDeg(int branch)
    {
        if (branch == 1)
        {
            return 270f;   // 外功：上
        }
        if (branch == 2)
        {
            return 0f;     // 内功：右
        }
        if (branch == 3)
        {
            return 90f;    // 身法：下
        }
        return 180f;       // 悟性：左
    }

    /// <summary>
    /// 两点连线。四向布局 + 分叉之后会有斜线，所以用**绕左端旋转的 ColorRect**：
    /// pivot 放在左端中点，rotation = 线段角度。比 Line2D 稳（Node2D 挂在 Control 下坐标系容易出意外）。
    /// </summary>
    static ColorRect MakeLine(Vector2 from, Vector2 to)
    {
        ColorRect r = new ColorRect();
        r.MouseFilter = Control.MouseFilterEnum.Ignore;

        Vector2 d = to - from;
        float len = d.Length();
        // 节点是圆的，线段从圆的边缘起止，看起来才像"节点之间连了一条路"
        float pad = NodeSize * 0.5f;
        float drawLen = len - pad * 2f;
        if (drawLen < 1f)
        {
            drawLen = 1f;
        }

        Vector2 dir = len > 0.01f ? d / len : new Vector2(1, 0);
        r.Position = from + dir * pad;
        r.Size = new Vector2(drawLen, LineW);
        r.PivotOffset = new Vector2(0f, LineW * 0.5f);
        r.Rotation = Mathf.Atan2(d.Y, d.X);
        return r;
    }

    // ============ 交互 ============

    void OnNodePressed(int id)
    {
        _selected = id;
        TalentConfig c = Talent.Get(id);
        if (c == null)
        {
            return;
        }

        if (Talent.IsMaxed(id))
        {
            AudioManager.PlayUISFX("ui_click");
            SetHint("「" + c.Name + "」已点满（" + c.MaxLevel + "/" + c.MaxLevel + " 级）");
            Refresh();
            return;
        }

        if (!Talent.ReqMet(id))
        {
            AudioManager.PlayUISFX("ui_click");
            if (c.ReqB > 0)
            {
                SetHint("交汇节点：需要「" + NameOf(c.ReqA) + "」与「" + NameOf(c.ReqB) + "」各至少 1 级");
            }
            else
            {
                SetHint("需要先点亮「" + NameOf(c.ReqA) + "」");
            }
            Refresh();
            return;
        }

        double cost = Talent.CostOf(id);
        if (_dao == null || _dao.Amount < cost)
        {
            AudioManager.PlayUISFX("ui_click");
            SetHint("道行不足：需要 " + cost.ToString("0.#") + "，当前 " + (_dao != null ? _dao.Amount.ToString("0.#") : "0"));
            Refresh();
            return;
        }

        if (Talent.TryLearn(id, _dao))
        {
            AudioManager.PlayUISFX("ui_confirm");
            SetHint("「" + c.Name + "」→ " + Talent.LevelOf(id) + " 级（花费 " + cost.ToString("0.#") + " 道行）");
            Refresh();
        }
    }

    static string NameOf(int id)
    {
        TalentConfig c = Talent.Get(id);
        return c != null ? c.Name : "（未知）";
    }

    // ============ 刷新 ============

    void Refresh()
    {
        if (_dao != null && _TTitle != null)
        {
            string tag = _tree == 1 ? "武学" : "江湖";
            _TTitle.Text = "天赋 · " + tag + " · 道行 " + _dao.Amount.ToString("0.#");
        }

        RefreshTabs();

        foreach (TalentConfig c in Talent.Nodes)
        {
            Button b;
            if (!_nodeBtn.TryGetValue(c.Id, out b) || b == null)
            {
                continue;
            }

            int lv = Talent.LevelOf(c.Id);
            bool ready = Talent.ReqMet(c.Id);
            bool mech = IsMechanic(c);
            bool rune = c.Path == 9;

            b.Text = NodeText(c, lv);

            // ⚠️ **不设 Disabled**：禁用按钮收不到 MouseEntered，而"前置没到的节点"恰恰是
            // 玩家最需要悬停查看的东西（我要知道它值不值得绕过去）。前置校验已经在
            // OnNodePressed 里做了，禁不禁用对点击结果没有影响，只影响悬停。
            b.Disabled = false;

            StyleBoxFlat sb;
            Color textCol;
            if (lv > 0)
            {
                sb = rune ? _sbRune : (mech ? _sbMech : _sbOn);
                textCol = rune ? ColRune : (mech ? ColMech : ColOn);
            }
            else if (ready)
            {
                sb = rune ? _sbRune : _sbReady;
                textCol = rune ? ColRune : (mech ? ColMech : ColReady);
            }
            else
            {
                sb = _sbLock;
                textCol = ColLock;
            }

            b.AddThemeStyleboxOverride("normal", sb);
            b.AddThemeStyleboxOverride("hover", sb);
            b.AddThemeStyleboxOverride("pressed", sb);
            b.AddThemeStyleboxOverride("disabled", _sbLock);
            b.AddThemeColorOverride("font_color", textCol);
            b.AddThemeColorOverride("font_disabled_color", ColLock);

            List<ColorRect> lines;
            if (_nodeLines.TryGetValue(c.Id, out lines) && lines != null)
            {
                // 线跟着**子节点**走：子节点点过 = 这条路走通了
                Color col = lv > 0 ? LineOn : LineOff;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i] != null)
                    {
                        lines[i].Color = col;
                    }
                }
            }
        }

        RefreshInfo();
    }

    /// <summary>
    /// 两个树标签的显示：选中态描金，未选灰色；「江湖」未解锁时带锁并显示"还差几个"。
    /// 为什么按钮不 Disabled：和节点一样的理由 —— 禁用后点不动也**没有任何解释**，
    /// 玩家只会以为界面坏了。这里让它可点，点了给一句"还差几个节点"的提示。
    /// </summary>
    void RefreshTabs()
    {
        bool unlocked = Talent.IsUnlocked(2);

        if (_TTree1Btn != null)
        {
            _TTree1Btn.Text = "武学";
            Color c1 = _tree == 1 ? ColOn : ColLock;
            _TTree1Btn.AddThemeColorOverride("font_color", c1);
        }

        if (_TTree2Btn != null)
        {
            if (unlocked)
            {
                _TTree2Btn.Text = "江湖";
                Color c2 = _tree == 2 ? ColOn : ColReady;
                _TTree2Btn.AddThemeColorOverride("font_color", c2);
            }
            else
            {
                _TTree2Btn.Text = "江湖 🔒";
                _TTree2Btn.AddThemeColorOverride("font_color", ColLock);
            }
        }
    }

    static bool IsMechanic(TalentConfig c)
    {
        TalentEff eff = (TalentEff)c.EffectType;
        if (eff == TalentEff.SkillSlot)
        {
            return true;
        }
        if (eff == TalentEff.InnSlot)
        {
            return true;
        }
        if (eff == TalentEff.SummonCap)
        {
            return true;
        }
        if (eff == TalentEff.DajieReward)
        {
            return true;
        }
        if (eff == TalentEff.InnFreeRefresh)
        {
            return true;
        }
        if (eff == TalentEff.DajieInterval)
        {
            return true;
        }
        return false;
    }

    static string NodeText(TalentConfig c, int lv)
    {
        if (c.Depth <= 0 && c.Branch <= 0)
        {
            return c.Name;
        }
        if (lv >= c.MaxLevel)
        {
            return c.Name + "\n满";
        }
        if (lv <= 0)
        {
            return c.Name + "\n" + CostShort(c.CostBase);
        }
        return c.Name + "\n" + lv + "/" + c.MaxLevel;
    }

    /// <summary>
    /// 节点上的成本缩写：60px 宽的圆点塞不下 "1048576"，会溢出到相邻节点上。
    /// 万 / 亿 两档够用到末端无底洞（1.6^98 那种量级本来也不该在节点上显示）。
    /// </summary>
    static string CostShort(double v)
    {
        if (v < 10000)
        {
            return v.ToString("0");
        }
        if (v < 100000000)
        {
            return (v / 10000).ToString("0.#") + "万";
        }
        return (v / 100000000).ToString("0.#") + "亿";
    }

    void RefreshInfo()
    {
        if (_TInfo == null)
        {
            return;
        }

        TalentBonus b = Talent.Bonus();
        List<string> lines = new List<string>();

        if (_dao != null)
        {
            lines.Add("道行 " + _dao.Amount.ToString("0.#"));
        }
        lines.Add("已投入 " + CostShort(Talent.TotalSpent()) + " 点");
        lines.Add("");

        // 两棵树的效果汇总在同一个 TalentBonus 里，所以这里按**属性维度**列，不按树枝列 ——
        // 按树枝列会在第 2 棵的界面里出现"【外功】"，而那个名字属于第 1 棵。
        lines.Add("【属性】攻 " + Pct(b.AtkMul) + " · 血 " + Pct(b.HpMul)
                  + " · 防 " + Pct(b.DefMul) + " · 速 " + Pct(b.SpdMul));
        lines.Add("　　　 伤害 " + Pct(b.DmgMul));
        if (b.ShieldKeepAdd > 0)
        {
            lines.Add("　　　 护盾保持率 +" + (b.ShieldKeepAdd * 100).ToString("0") + " 点");
        }
        if (b.WeightAdd < 0)
        {
            lines.Add("　　　 受击权重 " + b.WeightAdd.ToString("0") + "（更难被打）");
        }
        lines.Add("　　　 装备属性 " + Pct(b.EquipStatMul));

        lines.Add("【产出】银两 " + Pct(b.SilverMul) + " · 修为 " + Pct(b.CultMul));
        lines.Add("　　　 道行 " + Pct(b.DaoMul));
        if (b.DropAdd > 0)
        {
            lines.Add("　　　 装备掉率 +" + (b.DropAdd * 100).ToString("0.#") + "%");
        }
        if (b.RarityBoost > 0)
        {
            lines.Add("　　　 高档装备权重 +" + (b.RarityBoost * 100).ToString("0") + "%");
        }

        List<string> mech = new List<string>();
        if (b.SkillSlotAdd > 0)
        {
            mech.Add("武学槽 +" + b.SkillSlotAdd);
        }
        if (b.SummonCapAdd > 0)
        {
            mech.Add("召唤上限 +" + b.SummonCapAdd);
        }
        if (b.InnSlotAdd > 0)
        {
            mech.Add("客栈栏位 +" + b.InnSlotAdd);
        }
        if (b.InnDiscount > 0)
        {
            mech.Add("客栈刷新 −" + (b.InnDiscount * 100).ToString("0") + "%");
        }
        if (b.InnFreeAdd > 0)
        {
            mech.Add("免费刷新 +" + b.InnFreeAdd);
        }
        if (b.DajieRewardMul > 1.0001)
        {
            mech.Add("大劫奖励 +" + Pct(b.DajieRewardMul));
        }
        if (Math.Abs(b.DajieIntervalMul - 1) > 0.0001)
        {
            mech.Add("大劫间隔 " + ((b.DajieIntervalMul - 1) * 100).ToString("+0;-0") + "%");
        }
        if (mech.Count > 0)
        {
            lines.Add("");
            lines.Add("【已解锁】");
            lines.Add(string.Join(" · ", mech));
        }

        TalentConfig sel = Talent.Get(_selected);
        if (sel != null)
        {
            lines.Add("");
            lines.Add("—— " + sel.Name + " ——");
            lines.Add(sel.Desc);
            int lv = Talent.LevelOf(sel.Id);
            if (lv >= sel.MaxLevel)
            {
                lines.Add("已点满 " + lv + "/" + sel.MaxLevel);
            }
            else
            {
                lines.Add("下一级 " + CostShort(Talent.CostOf(sel.Id)) + " 道行（" + lv + "/" + sel.MaxLevel + "）");
            }
        }

        lines.Add("");
        lines.Add("左键拖动查看　·　滚轮缩放");

        _TInfo.Text = string.Join("\n", lines);
    }

    static string Pct(double mul)
    {
        double v = (mul - 1) * 100.0;
        if (v > -0.05 && v < 0.05)
        {
            return "0%";
        }
        return v.ToString("0.#") + "%";
    }

    void ShowNodeInfo(int id)
    {
        _selected = id;
        RefreshInfo();
        ShowTip(id);
    }

    // ============ 悬浮提示 ============

    /// <summary>
    /// 悬浮浮窗：跟着鼠标显示"这个节点到底是什么、点它值多少"。
    /// 为什么不用 Godot 原生 TooltipText：原生只支持纯文本、固定延迟、位置由引擎决定，
    /// 而这里要排版（分区间距）并跟随鼠标避开边缘。
    /// </summary>
    void ShowTip(int id)
    {
        TalentConfig c = Talent.Get(id);
        if (c == null || _TTip == null || _TTipLabel == null)
        {
            return;
        }

        string text = BuildTipText(c);
        _TTipLabel.Text = text;

        // Label 的 Size 在设文本后要等一帧才准，所以按字符数估高度（19px 字 + 4px 行距）
        float h = CountLines(text) * 26f + 30f;
        if (h < 90f)
        {
            h = 90f;
        }
        _TTip.Size = new Vector2(400f, h);

        Vector2 m = GameObject.GetLocalMousePosition();
        Vector2 panel = GameObject.Size;
        if (panel.X < 10f)
        {
            panel = new Vector2(1500f, 960f);
        }

        float x = m.X + 18f;
        if (x + 400f > panel.X - 8f)
        {
            x = m.X - 418f;
        }
        if (x < 8f)
        {
            x = 8f;
        }

        float y = m.Y + 18f;
        if (y + h > panel.Y - 8f)
        {
            y = panel.Y - h - 8f;
        }
        if (y < 8f)
        {
            y = 8f;
        }

        _TTip.Position = new Vector2(x, y);
        _TTip.Visible = true;
    }

    void HideTip()
    {
        if (_TTip != null)
        {
            _TTip.Visible = false;
        }
    }

    /// <summary>按字符数估折行（19px 中文在 376px 宽里约 19 字一行）。</summary>
    static int CountLines(string s)
    {
        int n = 0;
        string[] arr = s.Split('\n');
        for (int i = 0; i < arr.Length; i++)
        {
            int w = arr[i].Length;
            n += w <= 19 ? 1 : (w + 18) / 19;
        }
        return n < 1 ? 1 : n;
    }

    string BuildTipText(TalentConfig c)
    {
        List<string> lines = new List<string>();
        int lv = Talent.LevelOf(c.Id);
        bool maxed = lv >= c.MaxLevel;
        TalentEff eff = (TalentEff)c.EffectType;

        lines.Add("【" + BranchName(c.Branch) + " · " + PathName(c) + "】" + c.Name);
        lines.Add(c.Desc);
        lines.Add("");

        if (eff == TalentEff.BranchBoost)
        {
            lines.Add("效果：本支百分比加成 ×" + (1 + c.Value).ToString("0.##"));
        }
        else if (eff == TalentEff.AllAttrPct)
        {
            lines.Add("每级 攻/血/防/速 各 " + ValText(c.Value, eff)
                      + "　当前 " + lv + "/" + c.MaxLevel + " 级");
        }
        else
        {
            lines.Add("每级 " + ValText(c.Value, eff) + "　当前 " + lv + "/" + c.MaxLevel
                      + " 级 = " + ValText(c.Value * lv, eff));
        }

        if (maxed)
        {
            lines.Add("已点满");
        }
        else
        {
            lines.Add("下一级 " + CostShort(Talent.CostOf(c.Id)) + " 道行");
        }

        if (c.ReqA > 0 || c.ReqB > 0)
        {
            List<string> reqs = new List<string>();
            if (c.ReqA > 0)
            {
                reqs.Add(ReqOne(c.ReqA));
            }
            if (c.ReqB > 0)
            {
                reqs.Add(ReqOne(c.ReqB));
            }
            lines.Add("前置：" + string.Join("  +  ", reqs));
        }

        return string.Join("\n", lines);
    }

    static string ReqOne(int id)
    {
        TalentConfig r = Talent.Get(id);
        if (r == null)
        {
            return "?";
        }
        if (Talent.LevelOf(id) >= 1)
        {
            return r.Name + "（已点）";
        }
        return r.Name + "（未点）";
    }

    static string BranchName(int b)
    {
        if (b == 1)
        {
            return "外功";
        }
        if (b == 2)
        {
            return "内功";
        }
        if (b == 3)
        {
            return "身法";
        }
        if (b == 4)
        {
            return "悟性";
        }
        return "中心";
    }

    static string PathName(TalentConfig c)
    {
        if (c.Branch <= 0)
        {
            return "起点";
        }
        if (c.Path == 9)
        {
            return "交汇";
        }
        if (c.Path == 8)
        {
            return "外环";
        }
        string p = "主干";
        if (c.Path == 1)
        {
            p = "左子路";
        }
        else if (c.Path == 2)
        {
            p = "右子路";
        }
        return p + " 第" + c.Depth + "层";
    }

    /// <summary>把效果值翻译成人话：百分比带 +/−，点数类带"点"，机制类带"个"。</summary>
    static string ValText(double v, TalentEff eff)
    {
        if (eff == TalentEff.InnDiscount)
        {
            return "−" + Math.Abs(v).ToString("0.#") + "%";
        }

        string sign = v < 0 ? "−" : "+";
        string num = Math.Abs(v).ToString("0.#");

        if (eff == TalentEff.ShieldKeepPts || eff == TalentEff.WeightPts || eff == TalentEff.DropPts)
        {
            return sign + num + " 点";
        }
        if (eff == TalentEff.SkillSlot || eff == TalentEff.SummonCap || eff == TalentEff.InnSlot)
        {
            return sign + num + " 个";
        }
        return sign + num + "%";
    }

    void SetHint(string text)
    {
        if (_THint != null)
        {
            _THint.Text = text;
        }
    }
}
