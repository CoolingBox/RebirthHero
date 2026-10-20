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
//   · Scripts/Game/UI/StatsPanel.cs    —— 战报（独立 BasePanel；常驻侧栏，点「战报」切换显隐）
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
    private Timer _autoSave;              // M2.6 自动保存主档（间隔见 SaveBackup.AutoSaveMinutes）
    private bool _needRestart;            // 载入存档后要按新进度重开一场（面板关掉后执行）

    private BattleEngine _engine;
    private bool _built, _resultShown;
    private float _areaX = 1920f;         // 视口宽（供各显示组件做布局）

    // —— 显示子模块（场景里各挂脚本的独立节点；面板只持有引用并编排，不含其内部状态）——
    private HeroCardLayer _cards;
    private SummonStackLayer _stacks;
    private FloatLayer _floats;
    private BackdropLayer _backdrop;      // 全屏山水背景（懒创建，见 EnsureBackdrop）

    // 子界面（战报 / 选关 / 武学 / 设置）已拆成独立 BasePanel，由 UIManager 开关，
    // 本面板不再持有它们的引用 —— 只负责"注入依赖 + 打开 + 关闭后恢复战斗"。

    // —— 材料库存（本世资产；重开本世时清空）——
    private readonly MaterialBag _materials = new();

    // —— 装备（M2.5；同属本世资产，转世归零 —— GDD §3.3 账本）——
    private readonly EquipBag _equips = new EquipBag();
    private List<EquipConfig> _equipPool;   // 全部装备模板
    private Random _rng;                    // 掉落随机源（与战斗随机分开，便于复现）

    // —— 武学装配（M2.4：每侠客 3 槽，玩家的构筑选择，不随重开本世清空）——
    private readonly SkillLoadout _loadout = new();
    private List<SkillConfig> _skillPool;   // 全部已实装武学（装配池）

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
            LoadProgress();   // M2.6：把盘上的进度灌回状态机 —— 必须早于第一场战斗
            InitStatic();
            _built = true;
        }
        AudioManager.PlayBGM("battle_01");   // 目前只有战斗一个场景；换曲只改这里（battle_01/02/03 可试听对比）
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
        // 0 = 空槽（SkillLoadout.Empty），不是合法武学 id —— 在入口拦掉，
        // 避免任何调用方漏判时空查表刷 "找不到ID为 0" 的噪音
        _lookup = id =>
        {
            if (id <= 0) return null;
            return ConfigManager.GetOne<SkillConfig>(id);
        };

        // M2.4 武学池 = 全部「已实装」武学（不按 Tier 限制；TierCap 只约束敌方，见 SkillConfig.Tier 注释）
        _skillPool = new List<SkillConfig>();
        List<SkillConfig> allSkills = ConfigManager.GetAll<SkillConfig>();
        if (allSkills != null)
            foreach (SkillConfig s in allSkills)
                if (s != null && s.Status == SkillStatus.Implemented) _skillPool.Add(s);

        // 敌方阵容：单位模板（EnemyConfig 表）+ 武学池（已实装全池；按 Id 定序 → 抽取可复现）
        _enemyTemplates = ConfigManager.GetAll<EnemyConfig>() ?? new List<EnemyConfig>();
        _enemyPool = EnemyFactory.BuildSkillPool(ConfigManager.GetAll<SkillConfig>());

        // M2.5 装备：模板池 + 掉落随机源
        _equipPool = ConfigManager.GetAll<EquipConfig>();
        if (_equipPool == null) _equipPool = new List<EquipConfig>();
        _rng = new Random();
        // 召唤物类型档位（SummonConfig 表）——必须在建队前灌入，否则召唤物走引擎兜底规格
        SummonCatalog.Init(ConfigManager.GetAll<SummonConfig>());

        _cult = new Cultivation();
        _silver = new Silver();
        _run = new StageRun(BuildPlayerTeam, BuildEnemyWave, 20261015);

        // M2.3 客栈：开一世（自动刷 4 栏资质）；开局编队 = 开局 4 名（无资质基准版）
        _inn = new Inn(heroes, ConfigManager.GetAll<QualityConfig>(),
                       id => ConfigManager.GetOne<QualityConfig>(id));
        _inn.NewLife(20261016, RosterIds());

        // M2.4 武学装配：初始 = 表内默认武学（每人 2 个），第 3 槽留给玩家填
        _loadout.Init(heroes);

        // M2.6 存档：退出游戏时兜底存一次。其余存档点在"每个可能改状态的时刻"，
        // 这里只防"直接关窗口"这一类漏网（结算后没关子界面就退出）。
        GameObject.TreeExiting += SaveProgress;
    }

    // ============ M2.6 存档 ============
    // 落盘/读盘一律走框架的 SaveManager（user://GameSave，JSON）；本面板只负责"什么时候存"。
    // 抓/灌的字段映射在 GameProgress 里，备份/导出/列表在 SaveBackup 里，此处不出现文件路径。
    //
    /// <summary>
    /// 存档粒度 = **一场战斗的结算点**（不是每次掉落）。击杀时每只敌人都写盘既慢又无意义 ——
    /// 崩溃损失最多一场战斗的掉落，对放置游戏可接受。
    /// 触发点：每场结算 / 关子界面 / 选层 / 重开本世 / 自动保存计时器 / 退出兜底 / 面板手动保存。
    /// </summary>
    private void SaveProgress()
    {
        GameSave data = CaptureProgress();
        if (data == null) return;

        SaveManager.Save(data);
    }

    /// <summary>抓当前进度（给存档面板的"保存/备份/导出"共用同一条路径）。</summary>
    private GameSave CaptureProgress()
    {
        // 状态机还没造好（Init 未跑完）时返回 null，避免初始化的中间态被写盘
        if (_run == null || _cult == null || _silver == null) return null;

        return GameProgress.Capture(_run, _cult, _silver, _materials, _equips, _loadout, _inn, RosterIds());
    }

    /// <summary>读主档：没存过盘 / 版本不认识时保持"新一局"状态（各状态机已是初始值）。</summary>
    private void LoadProgress()
    {
        GameSave data = SaveManager.Load<GameSave>();
        GameProgress.Apply(data, _run, _cult, _silver, _materials, _equips, _loadout, _inn);
    }

    // ---------- 存档面板（保存 / 备份 / 载入）----------
    private void OnOpenSave()
    {
        AudioManager.PlayUISFX("ui_open");
        _timer.Stop();
        _advanceTimer.Stop();

        SavePanel.BindData(CaptureProgress, ApplyProgress, SyncAutoSave, OnCloseSave);
        UIManager.ShowPanel<SavePanel>();
    }

    /// <summary>载入某份存档：灌回状态机 → 立刻写成主档（下次启动就是这份）→ 标记重开一场。</summary>
    private bool ApplyProgress(GameSave data)
    {
        bool ok = GameProgress.Apply(data, _run, _cult, _silver, _materials, _equips, _loadout, _inn);
        if (!ok) return false;

        StatsPanel.InvalidateCurrent();   // 换档 → 战报清零
        SaveProgress();
        _needRestart = true;              // 面板关闭后按新进度重开（不在面板还开着时起战斗）
        return true;
    }

    private void OnCloseSave()
    {
        if (_needRestart)
        {
            _needRestart = false;
            StartBattle();
            return;
        }

        ResumeBattle();
    }

    /// <summary>自动保存开关变了 → 启停计时器（设置本身由面板写盘）。</summary>
    private void SyncAutoSave()
    {
        if (_autoSave == null) return;

        SettingData settings = SettingManager.Data;
        bool on = settings != null && settings.AutoSave;
        if (on) _autoSave.Start();
        else _autoSave.Stop();
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
    {
        return HeroBuilder.BuildTeam(ActiveRoster(), _lookup, id => _cult.LevelOf(id), includePending: false,
                                     qualityOf: id => _inn?.QualityOf(id),
                                     affixLookup: id => ConfigManager.GetOne<QualityConfig>(id),
                                     skillIdsOf: _loadout.Get,
                                     equipOf: id => _equips.BonusOf(id, EquipById));
    }

    /// <summary>装备模板查表（M2.5）。建队时才调，不在每帧路径上。</summary>
    private EquipConfig EquipById(int id)
    {
        if (_equipPool == null) return null;

        for (int i = 0; i < _equipPool.Count; i++)
            if (_equipPool[i].Id == id) return _equipPool[i];

        return null;
    }

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

        // 子界面按钮：不判断子面板是否存在 —— 它们由 UIManager 按需创建
        if (_TPushBtn != null) _TPushBtn.Pressed += OnResumePush;
        if (_TNextBtn != null) _TNextBtn.Pressed += OnNext;
        if (_TInnBtn != null) _TInnBtn.Pressed += OnOpenInn;
        if (_TStatsBtn != null) _TStatsBtn.Pressed += OnToggleStats;
        if (_TStageBtn != null) _TStageBtn.Pressed += OnOpenStage;
        if (_TSkillBtn != null) _TSkillBtn.Pressed += OnOpenSkills;
        if (_TEquipBtn != null) _TEquipBtn.Pressed += OnOpenEquips;
        if (_TSaveBtn != null) _TSaveBtn.Pressed += OnOpenSave;
        if (_TSettingBtn != null) _TSettingBtn.Pressed += OnOpenSettings;

        _timer = new Timer { WaitTime = TickSec, OneShot = false, Autostart = false };
        GameObject.AddChild(_timer);
        _timer.Timeout += OnTick;

        _advanceTimer = new Timer { WaitTime = 1.1, OneShot = true, Autostart = false };
        GameObject.AddChild(_advanceTimer);
        _advanceTimer.Timeout += OnAdvance;

        // 自动保存：整 10 分钟一次主档。开关读 SettingData（玩家在存档面板里改）
        _autoSave = new Timer { WaitTime = SaveBackup.AutoSaveMinutes * 60, OneShot = false, Autostart = false };
        GameObject.AddChild(_autoSave);
        _autoSave.Timeout += SaveProgress;
        SyncAutoSave();

        UIThemeManager.ApplyTo(GameObject);
        ApplyThemeColors();
        UIThemeManager.Changed += OnThemeChanged;
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
        StatsPanel.BindBattle(_engine, _engine.A, _run.Layer);   // 战报：注入引擎/阵容/层（面板未创建则先存着）
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
        AudioManager.PlayUISFX("ui_confirm");
        _run.Reset();
        _cult.Reset();
        _silver.Reset();
        _materials.Reset();         // 材料也是"此生"资产
        _equips.Reset();            // 装备同样归零（GDD §3.3：装备不跨世）
        StatsPanel.InvalidateCurrent();   // 重开本世 → 下次开战强制清零统计
        SaveProgress();             // 归零结果立刻落盘，避免"重开了但没退游戏"的错位
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
        AudioManager.PlayUISFX("ui_open");
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
        AudioManager.PlayUISFX("ui_close");
        if (_TInnBtn != null) _TInnBtn.Disabled = false;
        SaveProgress();   // 招募 / 换编队 / 刷新都在客栈里发生
        if (_engine != null && !_engine.IsOver) _timer.Start();
        else if (_resultShown) _advanceTimer.Start();
    }

    // ============ 子界面开关 ============
    // 统一约定：
    //   · 打开音在此处播（面板不知道是谁调它起来的）
    //   · 关闭音在面板内部播（面板自己知道自己被关了）
    //   · OnClose* 回调只负责"恢复战斗节奏"，不重复播音
    //   · 打开前一律暂停战斗，关闭后按引擎状态恢复 —— 避免后台空转

    // ============ 武学装配（M2.4）============
    private void OnOpenSkills()
    {
        AudioManager.PlayUISFX("ui_open");
        _timer.Stop();
        _advanceTimer.Stop();

        SkillsPanel.BindData(_loadout, ActiveRoster(), _lookup, _skillPool, OnCloseSkills);
        UIManager.ShowPanel<SkillsPanel>();
    }

    /// <summary>关武学面板：恢复战斗节奏。</summary>
    private void OnCloseSkills()
    {
        SaveProgress();   // 武学装配是跨世资产，改完立刻落盘
        ResumeBattle();
    }

    // ============ 装备（M2.5）============
    private void OnOpenEquips()
    {
        AudioManager.PlayUISFX("ui_open");
        _timer.Stop();
        _advanceTimer.Stop();

        EquipPanel.BindData(_equips, _equipPool, ActiveRoster(), _silver, OnCloseEquips);
        UIManager.ShowPanel<EquipPanel>();
    }

    private void OnCloseEquips()
    {
        SaveProgress();   // 穿脱 / 强化都在面板里改，关闭时才写盘
        ResumeBattle();
    }

    // ============ 选关 ============
    private void OnOpenStage()
    {
        AudioManager.PlayUISFX("ui_open");
        _timer.Stop();
        _advanceTimer.Stop();

        StagePanel.BindData(_run, _materials, OnGoLayer, OnCloseStage);
        UIManager.ShowPanel<StagePanel>();
    }

    /// <summary>选某层：锁层挂机 + 关面板 + 重开一场。</summary>
    private void OnGoLayer(int layer)
    {
        _run.JumpTo(layer);
        UIManager.ClosePanel<StagePanel>();   // 选完即销毁，下次重开时按新层重建
        SaveProgress();                       // 层数已变，立刻落盘
        ResumeBattle();
        StartBattle();
    }

    private void OnCloseStage()
    {
        SaveProgress();
        ResumeBattle();
    }

    /// <summary>战报只是轻量面板：用轻点击音，不用 open/close（避免每次查看都像开新界面）。</summary>
    private void OnToggleStats()
    {
        AudioManager.PlayUISFX("ui_click");
        StatsPanel.ToggleCurrent();
    }

    // ============ 设置 ============
    private void OnOpenSettings()
    {
        AudioManager.PlayUISFX("ui_open");
        _timer.Stop();
        _advanceTimer.Stop();

        SettingPanel.BindData(OnCloseSettings);
        UIManager.ShowPanel<SettingPanel>();
    }

    private void OnCloseSettings()
    {
        ResumeBattle();
    }

    /// <summary>子界面关闭后恢复战斗：未结束则继续 tick，已结束则走结算倒计时。</summary>
    private void ResumeBattle()
    {
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
        // 标题只留"当前在哪"（短、稳定）；最高层/场次这类统计挪到下面的战况行 ——
        // 导航条里标题宽度有限，长句会被按钮压住（2026-10-20 用户反馈的遮挡问题）
        if (_TTitle != null)
            _TTitle.Text = "《轮回大侠》· 第 " + _run.Layer + " 层 " + _run.Wave + "/" + _run.WavesInLayer + " 波" + waveTag + mode;

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
            _TExp.Text = "最高 " + _run.BestLayer + " 层 · 已战 " + _run.Battles + " 场　｜　"
                       + "银两 " + BigNum.From(_silver.Amount).ToString(2) + "（本世累计 " + BigNum.From(_silver.TotalGained).ToString(2) + "）"
                       + "　修为累计 " + BigNum.From(_cult.TotalGained).ToString(2) + "　｜　" + lv;
    }

    // ============ 事件订阅 ============
    private void HookEvents()
    {
        _engine.Bus.OnDamage += (a, t, amt, skill) =>
        {
            if (amt <= 0) return;
            SpawnFloat(t, "-" + amt.ToString("0"), new Color(1f, 0.4f, 0.3f));
            PlayDamageSound(t, skill);
        };
        _engine.Bus.OnHeal += (a, t, amt, skill) => { if (amt > 0) SpawnFloat(t, "+" + amt.ToString("0"), new Color(0.4f, 1f, 0.5f)); };
        _engine.Bus.OnShield += (a, t, amt) => { if (amt > 0) SpawnFloat(t, "🛡 " + amt.ToString("0"), new Color(0.4f, 0.9f, 1f)); };
        _engine.Bus.OnSummon += (owner, unit) => OnSummoned(owner, unit);
        _engine.Bus.OnDeath += OnUnitDied;      // **击杀即时掉落**
        _engine.Bus.OnEnd += OnEnded;
    }

    // ============ 音效（M2.6 表现层：方位 / 距离 / 优先级） ============
    // 声音定位一律用"世界坐标"：卡牌层给出局部锚点，组件自己换算成世界坐标。
    // 不用 GetGlobalRect —— 那只是 Rect 布局值，不等于 canvas 坐标。

    /// <summary>
    /// 命中音：普攻轻、武学重。
    /// 只做"轻重两档"，不按伤害数值分档 —— 引擎没有暴击概念，
    /// 凭数值猜档位会让同一件武器忽轻忽重，反而破坏空间一致性。
    /// </summary>
    private void PlayDamageSound(BattleUnit tgt, string skillName)
    {
        if (!TryWorldAnchor(tgt, out Vector2 pos)) return;

        bool isSkill = !string.IsNullOrEmpty(skillName);
        string sfx = isSkill ? "hit_heavy" : "hit_light";

        AudioManager.SfxPriority priority = AudioManager.SfxPriority.Hit;
        if (isSkill) priority = AudioManager.SfxPriority.Crit;

        AudioManager.PlaySFXAt(sfx, pos, priority);
    }

    private void PlayDeathSound(BattleUnit u)
    {
        if (!TryWorldAnchor(u, out Vector2 pos)) return;
        AudioManager.PlaySFXAt("death", pos, AudioManager.SfxPriority.Death);
    }

    /// <summary>取单位的世界坐标（音频定位用）：先问侠客卡，再问召唤堆叠卡。</summary>
    private bool TryWorldAnchor(BattleUnit u, out Vector2 world)
    {
        if (_cards.TryGetWorldAnchor(u, out world)) return true;
        if (_stacks.TryGetWorldAnchor(u, out world)) return true;
        world = Vector2.Zero;
        return false;
    }

    /// <summary>敌方单位被击杀 → 立刻发放修为（每人一份）与银两，并飘字反馈。</summary>
    private void OnUnitDied(BattleUnit u)
    {
        PlayDeathSound(u);   // 必须在移除锚点之前取位置

        // 召唤物死亡不留"尸体"：引擎已把它移出队伍，这里同步堆叠卡（减 1，减到 0 就撤卡）
        if (u.IsSummon) { _stacks.Sync(_engine, _areaX); return; }

        if (_engine?.B == null || !_engine.B.Contains(u)) return;   // 只结算敌方
        int layer = _run.Layer;
        double perHead = CultivationConfig.GainPerKill(layer);
        _cult.GainForKill(RosterIds(), layer);          // 只累进池子，不升级
        double sil = _silver.GainForKill(layer);

        // 材料：按当前层段掉对应品阶（确定性，不赌概率）
        int matTier = MaterialCatalog.TierOf(layer);
        _materials.Add(matTier, MaterialConfig.MatMobDrop);

        // 装备：概率掉落。与材料刻意不同 —— 材料给"稳定感"（每杀必得），
        // 装备给"惊喜"（偶发、且随层数变好），两种节奏并存才不会让掉落变成纯打卡。
        if (EquipDropper.ShouldDrop(_rng))
        {
            EquipConfig drop = EquipDropper.Roll(layer, _rng, _equipPool);
            if (drop != null)
            {
                _equips.Add(drop.Id);
                SpawnFloat(u, "得 " + drop.Name + "（" + EquipRules.RarityName(drop.Rarity) + "）",
                           UISignalColors.RarityOf(drop.Rarity));
            }
        }

        SpawnFloat(u, "修为+" + perHead.ToString("0.#") + " 银两+" + BigNum.From(sil).ToString(2)
                      + " " + MaterialCatalog.NameOf(matTier) + "+" + MaterialConfig.MatMobDrop,
                   new Color(1f, 0.92f, 0.45f));
        RefreshHeader();
    }

    private void OnSummoned(BattleUnit owner, BattleUnit unit)
    {
        _stacks.Sync(_engine, _areaX);   // 召唤物走堆叠卡：立刻同步一次，让飘字马上有锚点（否则要等下一个 tick）

        // 同步之后再取锚点：否则召唤物还没有堆叠卡，拿不到位置
        if (TryWorldAnchor(unit, out Vector2 pos))
            AudioManager.PlaySFXAt("spawn", pos, AudioManager.SfxPriority.Summon);
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
        string dropTag = "修为 +" + perHeadTotal.ToString("0.#") + "/人 · 银两 +" + BigNum.From(_silver.BattleGained).ToString(2);

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
            // 打穿整层 → 额外材料奖励（收尾波获胜时结算一次）
            string matTag = "";
            if (_run.LastSettleClearedLayer)
            {
                int tier = MaterialCatalog.TierOf(layer);
                _materials.Add(tier, MaterialConfig.MatClearBonus);
                matTag = " · " + MaterialCatalog.NameOf(tier) + "+" + MaterialConfig.MatClearBonus;

                // 全屏事件音：首领波用更重的音，普通清层用轻的（都是居中、不参与方位）
                if (finisher) AudioManager.PlayEventSFX("boss_down");
                else AudioManager.PlayEventSFX("clear_layer");
            }

            if (_TResult != null)
            {
                _TResult.Text = _run.LastSettleClearedLayer
                    ? "🏆 破第 " + layer + " 层（" + totalWaves + " 波） · " + dropTag + upTag + matTag
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
        SaveProgress();   // 每场结算都落一次：场次 = 放置游戏的最小有意义进度单位
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
        StatsPanel.RefreshCurrent();   // 面板隐藏时内部直接返回
    }

    // ============ UI 主题 ============
    // 原来的 ApplyCjkFont 只挂了一个"仅含字体"的 Theme。现在字体与配色统一由
    // UIThemeManager 提供（含标题/正文字体、按钮与面板样式），避免两套 Theme 互相覆盖。

    /// <summary>
    /// 把当前主题的环境色刷到那些"不归 Theme 管"的节点上：
    /// ColorRect 没有主题样式（它是节点属性不是 StyleBox），所以背景必须由代码喂。
    /// </summary>
    private void ApplyThemeColors()
    {
        if (_TBg != null)
            EnsureBackdrop();

        // 子界面各自刷自己的背景与重建（它们不一定存在，静态方法内部判空）
        StagePanel.ApplyThemeCurrent();
        SkillsPanel.ApplyThemeCurrent();
        StatsPanel.ApplyThemeCurrent();
        SettingPanel.ApplyThemeCurrent();
        EquipPanel.ApplyThemeCurrent();
        SavePanel.ApplyThemeCurrent();

        // 卡牌与堆叠卡的底色是在 Build 时写进 ColorRect 的，换主题必须重建节点。
        // 只重建表现层（读现有 BattleUnit），不碰引擎状态与战斗进度。
        if (_engine == null || _cards == null || _stacks == null)
            return;

        _cards.Clear();
        _cards.Build(_engine.A, _engine.B, _areaX);
        _cards.Refresh(_engine);

        _stacks.Clear();
        _stacks.Sync(_engine, _areaX);
    }

    /// <summary>
    /// 背景层懒创建：TBg 是纯色 ColorRect，作为底图未加载时的兜底；
    /// 山水背景叠在它上面。只建一次，换主题时由 BackdropLayer 自己刷新。
    /// </summary>
    private void EnsureBackdrop()
    {
        if (_TBg == null)
            return;

        if (_backdrop != null && GodotObject.IsInstanceValid(_backdrop))
            return;

        _backdrop = new BackdropLayer();
        _TBg.AddChild(_backdrop);
    }

    private void OnThemeChanged(int themeId)
    {
        UIThemeManager.ApplyTo(GameObject);
        ApplyThemeColors();
    }

    string HeroName(int id)
    {
        foreach (HeroConfig h in _roster) if (h.Id == id) return h.Name;
        return "#" + id;
    }
}
