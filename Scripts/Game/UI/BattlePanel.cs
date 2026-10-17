// 文件：BattlePanel.cs
// 《轮回大侠》M2.2 推图 + 掉落 —— **编排层**（BasePanel 子类；不持有任何显示子模块的内部状态）
// 发动机：BattleEngine（纯 C# 引擎，与 M0 同款数学）
//
// 架构（2026-10-17 起）：显示与逻辑分离 + 表现层按显示子模块拆成**独立 Godot 节点组件**：
//   · Resources/UI/BattlePanel.tscn   —— 静态外壳；显示子模块节点各挂一个脚本（见下）
//   · Scripts/Game/UI/BattlePanel.cs  —— 本文件：**只做编排**（生命周期、推图状态机、开战/结算、
//                                        掉落、事件接线、逐帧驱动、顶部信息栏、共享工具）；
//                                        不持有卡表/堆叠表/飘字表/战报行 —— 那些归各自组件私有。
//   · Scripts/Game/UI/HeroCardLayer.cs    —— 侠客单位卡（挂 TCards 节点）
//   · Scripts/Game/UI/SummonStackLayer.cs —— 召唤物堆叠卡（挂 TStacks 节点）
//   · Scripts/Game/UI/FloatLayer.cs       —— 战斗飘字（挂 TFxLayer 节点）
//   · Scripts/Game/UI/StatsPanelView.cs   —— 战报统计面板（挂 TStatsPanel 节点；内部 Header/ViewBtn/SortBtn/Body 自管）
//   · Scripts/Game/UI/UiUtil.cs           —— 共享数字格式化
//   · Scripts/Game/UI/Generate/BattlePanelBind.cs —— 由 GenUICode.py 生成：_T* 字段 + OnBind 里的 FindChild 绑定
//
// 规则：显示组件互不直接引用，面板是唯一编排者 —— 事件在面板里解析锚点后转交 FloatLayer；
//       逐帧刷新由面板按序调用各组件（卡 → 堆叠 → 飘字 → 战报）。
//
// 推图循环（2026-10-15 v5.2 口径）：
//   一个【层】= N 波普通敌人 + 1 波收尾首领（普通层用精英、层主层用层主；N 起始 5、每 10 层 +1）
//   · 胜 → 层内进下一波（**血量延续，只回 10% 最大气血，阵亡者不复活**）
//   · 打穿收尾波 → 进下一层（整队重建、满血）
//   · 负/平 → **退回上一层、波次归 1、转入挂机模式**，点「继续推图」再往上冲
//
// 掉落口径（"击杀就可立即获得经验资源" + "升级要等待战斗结束"）：
//   · 每击杀一个敌人 → 立即给 **修为（=经验）**（每人一份）与 **银两**，飘字可见
//   · 修为只累进池子，**升级在本场结束时统一结算** —— 避免战斗中途改等级导致属性错乱

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using TinyFramework;

public partial class BattlePanel : BasePanel
{
    private const float SimDt = 0.15f;    // 每个 tick 推进的模拟秒数（~1.8x 观赏速度）
    private const float TickSec = 0.1f;   // Timer 真实间隔

    private Timer _timer;
    private Timer _advanceTimer;

    private BattleEngine _engine;
    private bool _built, _resultShown;
    private float _areaX = 1920f;         // 视口宽（供各显示组件做布局）

    // —— 显示子模块（场景里各挂脚本的独立节点；面板只持有引用并编排，不含其内部状态）——
    private HeroCardLayer _cards;
    private SummonStackLayer _stacks;
    private FloatLayer _floats;
    private StatsPanelView _stats;

    // —— M2.1 推图状态 ——
    private StageRun _run;                       // 层数推进
    private List<HeroConfig> _roster;            // 玩家阵容（开局 4 名）
    private Func<int, SkillConfig> _lookup;      // SkillConfig 查表
    private List<EnemyConfig> _enemyTemplates;   // 敌方单位模板（EnemyConfig.xlsx）
    private List<SkillConfig> _enemyPool;        // 敌方武学池（已实装全池 + 效果词 ≤14）
    // —— M2.2 掉落 ——
    private Cultivation _cult;                   // 修为（=经验）：击杀即时累加，战斗结束才升级
    private Silver _silver;                      // 银两：击杀即时进账
    // —— M2.3 客栈 ——
    private Inn _inn;

    public override void OnShow()
    {
        base.OnShow();
        if (!_built)
        {
            Init();
            InitStatic();
            _built = true;
        }
        StartBattle();
    }

    public override void OnHide()
    {
        // 面板被隐藏（如将来叠加其它全屏面板）时暂停战斗，避免后台空转
        if (_timer != null) _timer.Stop();
        if (_advanceTimer != null) _advanceTimer.Stop();
        base.OnHide();
    }

    // ============ 一次性初始化（原 OnBind 的职责，OnShow 仅首次执行一次） ============
    private void Init()
    {
        GameObject.MouseFilter = Control.MouseFilterEnum.Stop;   // 拦截输入，避免穿透到下层
        GameObject.ProcessMode = Node.ProcessModeEnum.Always;

        // 玩家阵容 = 开局 4 名（沈孤鸿/铁震山/苏小药/阿蛮），按 1 号位放坦的顺序布阵
        var heroes = ConfigManager.GetAll<HeroConfig>();
        _roster = new List<HeroConfig>();
        foreach (int id in new[] { 2, 1, 4, 3 })
        {
            HeroConfig h = FindHero(heroes, id);
            if (h != null) _roster.Add(h);
        }
        _lookup = id => ConfigManager.GetOne<SkillConfig>(id);

        // 敌方阵容：单位模板（EnemyConfig 表）+ 武学池（已实装全池；按 Id 定序 → 抽取可复现）
        _enemyTemplates = ConfigManager.GetAll<EnemyConfig>() ?? new List<EnemyConfig>();
        _enemyPool = EnemyFactory.BuildSkillPool(ConfigManager.GetAll<SkillConfig>());
        // 召唤物类型档位（SummonConfig 表）——必须在建队前灌入，否则召唤物走引擎兜底规格
        SummonCatalog.Init(ConfigManager.GetAll<SummonConfig>());

        _cult = new Cultivation();
        _silver = new Silver();
        _run = new StageRun(BuildPlayerTeam, BuildEnemyWave, 20261015);

        // M2.3 客栈：开一世（自动刷 4 栏资质）；开局编队 = 开局 4 名（无资质基准版）
        _inn = new Inn(heroes, ConfigManager.GetAll<QualityConfig>(),
                       id => ConfigManager.GetOne<QualityConfig>(id));
        _inn.NewLife(20261016, RosterIds());
    }

    /// <summary>M2.3 客栈：当前出战阵容 = 客栈编队（带当世资质）。编队为空即真正空编队（不再回退开局 4 名）。</summary>
    private List<HeroConfig> ActiveRoster()
    {
        if (_inn == null) return _roster;
        var heroes = ConfigManager.GetAll<HeroConfig>();
        var list = new List<HeroConfig>();
        foreach (int id in _inn.Roster)
        {
            HeroConfig h = FindHero(heroes, id);
            if (h != null) list.Add(h);
        }
        return list;   // 可能为空（全下阵）→ 真正空编队，下一场按空队打
    }

    private List<int> RosterIds()
    {
        var ids = new List<int>();
        foreach (HeroConfig h in _roster) ids.Add(h.Id);
        return ids;
    }

    private static HeroConfig FindHero(IEnumerable<HeroConfig> heroes, int id)
    {
        if (heroes == null) return null;
        foreach (HeroConfig h in heroes)
            if (h.Id == id) return h;
        return null;
    }

    private List<BattleUnit> BuildPlayerTeam()
        => HeroBuilder.BuildTeam(ActiveRoster(), _lookup, id => _cult.LevelOf(id), includePending: false,
                                 qualityOf: id => _inn?.QualityOf(id),
                                 affixLookup: id => ConfigManager.GetOne<QualityConfig>(id));

    private List<BattleUnit> BuildEnemyWave(int layer, int wave, bool isFinisher)
        => EnemyFactory.BuildWave(layer, wave, isFinisher, _enemyTemplates, _enemyPool);

    // ============ 静态框架（仅一次性接线，定位交给 .tscn） ============
    private Vector2 GetArea()
    {
        var r = GameObject.GetViewportRect().Size;
        if (r.X < 2 || r.Y < 2) r = new Vector2(1920, 1080);   // 设计分辨率兜底
        return r;
    }

    private void InitStatic()
    {
        // 根面板撑满画布：UIManager 是普通 Node（非 Control），子节点锚点才能相对它解析。
        var area = GetArea();
        GameObject.Size = area;
        GameObject.Position = Vector2.Zero;
        _areaX = area.X;

        // 取显示子模块引用（各自脚本挂在场景节点上，见 BattlePanel.tscn）
        _cards = _TCards as HeroCardLayer;
        _stacks = _TStacks as SummonStackLayer;
        _floats = _TFxLayer as FloatLayer;
        _stats = _TStatsPanel as StatsPanelView;

        if (_TPushBtn != null) _TPushBtn.Pressed += OnResumePush;
        if (_TNextBtn != null) _TNextBtn.Pressed += OnNext;
        if (_TInnBtn != null) _TInnBtn.Pressed += OnOpenInn;
        if (_TStatsBtn != null && _stats != null) _TStatsBtn.Pressed += _stats.Toggle;

        _timer = new Timer { WaitTime = TickSec, OneShot = false, Autostart = false };
        GameObject.AddChild(_timer);
        _timer.Timeout += OnTick;

        _advanceTimer = new Timer { WaitTime = 1.1, OneShot = true, Autostart = false };
        GameObject.AddChild(_advanceTimer);
        _advanceTimer.Timeout += OnAdvance;

        ApplyCjkFont(GameObject);
    }

    // ============ 开战 ============
    private void StartBattle()
    {
        ClearBattle();
        _advanceTimer.Stop();
        if (_TNextBtn != null) _TNextBtn.Visible = false;
        if (_TPushBtn != null) _TPushBtn.Visible = false;

        _cult.BeginBattle();      // 本场掉落计数清零
        _silver.BeginBattle();

        _engine = _run.Begin();
        HookEvents();

        _areaX = GetArea().X;
        _stats.BeginBattle(_engine, _engine.A, _run.Layer);   // 挂总线 + 进新层清零 + 建报行
        _cards.Build(_engine.A, _engine.B, _areaX);

        _resultShown = false;
        if (_TResult != null) _TResult.Visible = false;
        RefreshHeader();
        _timer.Stop();
        _timer.Start();
        _cards.Refresh(_engine);
        _stacks.Sync(_engine, _areaX);
    }

    /// <summary>止步后重开本世（银两与修为/等级一并归零 —— 都是"此生"资产，GDD §4.2）。</summary>
    private void OnNext()
    {
        _run.Reset();
        _cult.Reset();
        _silver.Reset();
        _stats.InvalidateLayer();   // 重开本世 → 下次开战强制清零统计
        StartBattle();
    }

    /// <summary>结束挂机，从当前层继续往上推。</summary>
    private void OnResumePush()
    {
        _run.ResumePush();
        StartBattle();
    }

    /// <summary>结算后开下一场（推图模式：胜则已进层 / 挂机模式：原地重复刷）。</summary>
    private void OnAdvance() => StartBattle();

    // ============ M2.3 客栈 ============
    /// <summary>打开客栈：用 UIManager 弹出独立窗口（**同时暂停战斗**：停 _timer / _advanceTimer）。</summary>
    private void OnOpenInn()
    {
        _timer.Stop();
        _advanceTimer.Stop();
        if (_TInnBtn != null) _TInnBtn.Disabled = true;
        InnPanel.BindData(_inn, _silver,
                          heroOf: id => ConfigManager.GetOne<HeroConfig>(id),
                          affixOf: id => ConfigManager.GetOne<QualityConfig>(id),
                          onClose: OnCloseInn,
                          onRosterChanged: RefreshHeader);
        UIManager.ShowPanel<InnPanel>();
    }

    /// <summary>关闭客栈的回调（由 InnPanel 关闭按钮触发）：恢复按钮并续上战斗状态。</summary>
    private void OnCloseInn()
    {
        if (_TInnBtn != null) _TInnBtn.Disabled = false;
        if (_engine != null && !_engine.IsOver) _timer.Start();
        else if (_resultShown) _advanceTimer.Start();
    }

    private void ClearBattle()
    {
        _cards.Clear();
        _stacks.Clear();
        _floats.Clear();
    }

    // ============ 顶部状态栏 ============
    private void RefreshHeader()
    {
        string waveTag = _run.IsFinisherWave
            ? (_run.IsBossLayer ? "  ★层主波" : "  ◆精英波")
            : "";
        string mode = _run.AutoPush ? "" : "  ⏸挂机中";
        if (_TTitle != null)
            _TTitle.Text = "《轮回大侠》M2.2 推图 · 第 " + _run.Layer + " 层 " + _run.Wave + "/" + _run.WavesInLayer + " 波" + waveTag + mode
                         + "　（本世最高 " + _run.BestLayer + " 层 · 已战 " + _run.Battles + " 场）";

        // 修为池是"每侠客各一份"，绝对值不如进度直观；战斗中途未结算升级，故进度会冲到 100% 以上 → clamp
        var lvParts = new List<string>();
        foreach (HeroConfig h in ActiveRoster())
        {
            int l = _cult.LevelOf(h.Id);
            string s;
            if (l >= CultivationConfig.MaxLevel)
            {
                s = h.Name + "L" + l + "满";
            }
            else
            {
                double pct = Math.Min(1.0, _cult.ProgressOf(h.Id)) * 100;
                s = h.Name + "L" + l + "·" + pct.ToString("0") + "%";
            }
            lvParts.Add(s);
        }
        string lv = string.Join(" · ", lvParts);
        if (_TExp != null)
            _TExp.Text = "银两 " + UiUtil.Fmt(_silver.Amount) + "（本世累计 " + UiUtil.Fmt(_silver.TotalGained) + "）"
                       + "　修为累计 " + UiUtil.Fmt(_cult.TotalGained) + "　｜　" + lv;
    }

    // ============ 事件订阅 ============
    private void HookEvents()
    {
        _engine.Bus.OnDamage += (a, t, amt, skill) => { if (amt > 0) SpawnFloat(t, "-" + amt.ToString("0"), new Color(1f, 0.4f, 0.3f)); };
        _engine.Bus.OnHeal += (a, t, amt, skill) => { if (amt > 0) SpawnFloat(t, "+" + amt.ToString("0"), new Color(0.4f, 1f, 0.5f)); };
        _engine.Bus.OnShield += (a, t, amt) => { if (amt > 0) SpawnFloat(t, "🛡 " + amt.ToString("0"), new Color(0.4f, 0.9f, 1f)); };
        _engine.Bus.OnSummon += (owner, unit) => OnSummoned(owner, unit);
        _engine.Bus.OnDeath += OnUnitDied;      // **击杀即时掉落**
        _engine.Bus.OnEnd += OnEnded;
    }

    /// <summary>敌方单位被击杀 → 立刻发放修为（每人一份）与银两，并飘字反馈。</summary>
    private void OnUnitDied(BattleUnit u)
    {
        // 召唤物死亡不留"尸体"：引擎已把它移出队伍，这里同步堆叠卡（减 1，减到 0 就撤卡）
        if (u.IsSummon) { _stacks.Sync(_engine, _areaX); return; }

        if (_engine?.B == null || !_engine.B.Contains(u)) return;   // 只结算敌方
        int layer = _run.Layer;
        double perHead = CultivationConfig.GainPerKill(layer);
        _cult.GainForKill(RosterIds(), layer);          // 只累进池子，不升级
        double sil = _silver.GainForKill(layer);

        SpawnFloat(u, "修为+" + perHead.ToString("0.#") + " 银两+" + UiUtil.Fmt(sil), new Color(1f, 0.92f, 0.45f));
        RefreshHeader();
    }

    private void OnSummoned(BattleUnit owner, BattleUnit unit)
    {
        _stacks.Sync(_engine, _areaX);   // 召唤物走堆叠卡：立刻同步一次，让飘字马上有锚点（否则要等下一个 tick）
    }

    private void OnEnded(int r)
    {
        if (_resultShown) return;
        _resultShown = true;

        int layer = _run.Layer;
        int wave = _run.Wave;
        int totalWaves = _run.WavesInLayer;
        bool finisher = _run.IsFinisherWave;
        bool win = r == 1;

        // 修为：击杀时已即时进池，这里**统一结算升级**（避免战斗中途改等级）
        List<Cultivation.LevelUp> ups = _cult.SettleLevelUps(RosterIds());
        string upTag = "";
        if (ups.Count > 0)
        {
            var parts = new List<string>();
            foreach (var u in ups)
            {
                string nm = HeroName(u.HeroId);
                parts.Add(nm + "↑L" + u.To);
            }
            upTag = "　" + string.Join(" ", parts);
        }

        double perHeadTotal = _roster.Count > 0 ? _cult.BattleGained / _roster.Count : 0;
        string dropTag = "修为 +" + perHeadTotal.ToString("0.#") + "/人 · 银两 +" + UiUtil.Fmt(_silver.BattleGained);

        _run.Settle(_engine);   // 胜→层内进下一波 / 打穿收尾波→进层；负→退回上一层并转入挂机

        // 层内续战的血量提示（波间只回 10%，阵亡者不复生）
        var survivors = new List<BattleUnit>();
        foreach (var u in _engine.A)
            if (!u.IsSummon && u.Slot >= 1) survivors.Add(u);
        int alive = 0;
        foreach (var u in survivors) if (u.Alive) alive++;
        string hpTag = alive + "/" + survivors.Count + " 人续战（回 10% 气血）";

        if (win)
        {
            if (_TResult != null)
            {
                _TResult.Text = _run.LastSettleClearedLayer
                    ? "🏆 破第 " + layer + " 层（" + totalWaves + " 波） · " + dropTag + upTag
                    : "✔ 第 " + layer + " 层 " + wave + "/" + totalWaves + " 波（" + (finisher ? "首领" : "普通") + "）清 · " + hpTag;
                _TResult.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.5f));
                _TResult.Visible = true;
            }
            _advanceTimer.Start();
        }
        else
        {
            string why = r == 0 ? "超时未决" : "全员阵亡";
            if (_TResult != null)
            {
                _TResult.Text = "⛔ 第 " + layer + " 层 " + wave + "/" + totalWaves + " 波失利（" + why + "）· " + dropTag + upTag + " · 退回第 " + _run.Layer + " 层挂机刷…";
                _TResult.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.35f));
                _TResult.Visible = true;
            }
            if (_TPushBtn != null) _TPushBtn.Visible = true;    // 继续推图
            if (_TNextBtn != null) _TNextBtn.Visible = true;    // 重开本世
            _advanceTimer.Start();      // 卡关不停机：退回上一层继续刷
        }
        RefreshHeader();
    }

    // ============ 飘字锚点（编排：先问侠客卡，再问召唤堆叠卡） ============
    private bool TryAnchor(BattleUnit u, out Vector2 pos)
    {
        if (_cards.TryGetAnchor(u, out pos)) return true;
        if (_stacks.TryGetAnchor(u, out pos)) return true;
        pos = Vector2.Zero;
        return false;
    }

    private void SpawnFloat(BattleUnit tgt, string text, Color color)
    {
        if (!TryAnchor(tgt, out Vector2 p)) return;
        _floats.Spawn(p, text, color);
    }

    // ============ 逐帧驱动 ============
    private void OnTick()
    {
        if (_engine == null) return;
        if (!_engine.IsOver) _engine.Step(SimDt);
        _cards.Refresh(_engine);
        _stacks.Sync(_engine, _areaX);
        _floats.Update(TickSec);
        _stats.Refresh();
    }

    // ============ CJK 字体 ============
    private static FontFile TryLoadCjkFont()
    {
        string[] paths = { "C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/msyh.ttf", "C:/Windows/Fonts/simhei.ttf", "C:/Windows/Fonts/simsun.ttc" };
        foreach (var p in paths)
        {
            if (!File.Exists(p)) continue;
            var f = new FontFile();
            if (f.LoadDynamicFont(p) == Error.Ok) return f;
        }
        return null;
    }

    /// <summary>把中文字体应用到整棵子树（**内部可见**：客栈界面 InnPanel 增量重建后要复用同一份字体）。</summary>
    internal static void ApplyCjkFont(Control root)
    {
        var font = TryLoadCjkFont();
        if (font == null) return;
        var theme = new Theme();
        theme.DefaultFont = font;
        root.Theme = theme;
    }

    string HeroName(int id)
    {
        foreach (HeroConfig h in _roster) if (h.Id == id) return h.Name;
        return "#" + id;
    }
}
