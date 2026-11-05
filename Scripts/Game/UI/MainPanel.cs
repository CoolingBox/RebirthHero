// 文件：MainPanel.cs
// 《轮回大侠》**主面板** —— 应用外壳：全局状态、存档、子界面开关、导航条、顶部状态栏、主题。
//
// 为什么要有这一层（2026-11-05）：
//   BattlePanel 曾经同时是"主界面"和"战斗场"—— 1800 行里只有一半在讲战斗，
//   另一半是存档、客栈/装备/武学/天赋的开关、主题刷新、顶部状态栏。读代码时
//   要跨过二十个与当前战斗无关的方法，才能找到敌我到底怎么结算。
//   现在职责一刀切开：
//
//   · MainPanel（本文件）= **一局的容器**
//     - 持有全部状态机（进度 / 修为 / 银两 / 材料 / 装备 / 道行 / 客栈 / 武学装配）
//     - 存档（抓取-灌回-落盘）、自动保存开关、退出兜底
//     - 子界面开关与回调（客栈 / 武学 / 装备 / 天赋 / 卡册 / 选关 / 战报 / 存档 / 设置）
//     - 导航条、顶部状态栏（含大劫倒计时）、结算栏按钮文案、主题刷新
//     - 时钟（BattleClock）与规则上下文（BattleRuleContext）
//   · BattlePanel = **一场战斗**
//     - 引擎、卡牌层、飘字、节拍推进、伤害/死亡演出、掉落飘字
//     - 开战 / 结算 / 大劫上门 / 转世触发
//     - 它**不持有**任何状态机，全部通过 `Bind(main)` 拿引用（同一个实例，不是拷贝）
//
// 依赖方向：BattlePanel → MainPanel（单向）。MainPanel 只在必要时回调战斗
// （转发时钟节拍、点倒计时迎战），不反过来读战斗内部细节 —— 只有 `InDajie`、
// `DajieWins`、`NextArmed` 三个读数是为了显示。
//
// 场景：Resources/UI/MainPanel.tscn（原 BattlePanel.tscn 的根，2026-11-05 改名）。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class MainPanel : BasePanel
{
    // ============ 全局状态机（这一局的全部账本） ============
    private StageRun _run;
    private Cultivation _cult;
    private Silver _silver;
    private readonly MaterialBag _materials = new MaterialBag();
    private readonly EquipBag _equips = new EquipBag();
    private readonly SkillLoadout _loadout = new SkillLoadout();
    private readonly Dao _dao = new Dao();
    private Inn _inn;
    private Random _rng;

    // ============ 配置表（Init 时取好，规则与战斗都从这里读） ============
    private List<HeroConfig> _roster;
    private Func<int, SkillConfig> _lookup;
    private List<SkillConfig> _skillPool;
    private List<EnemyConfig> _enemyTemplates;
    private List<SkillConfig> _enemyPool;
    private List<EquipConfig> _equipPool;
    private List<EquipSetConfig> _equipSetPool;
    private List<EquipAffixConfig> _equipAffixPool;
    private int _summonCapBase;

    // ============ 时钟与规则上下文 ============
    private readonly BattleClock _clock = new BattleClock();
    private readonly BattleRuleContext _rule = new BattleRuleContext();

    // ============ 战斗逻辑（普通类；画面与节点全归 BattlePanel） ============
    private BattleManager _battleMgr;

    // 读档时先把大劫进度存这里 —— BattleManager 要在状态机就位之后才能创建
    private int _pendingDajieWins;

    private Vector2 _area = new Vector2(1920f, 1080f);

    private bool _built;
    private bool _needRestart;

    // ============ 给 BattlePanel 的只读视图（同一个实例，不是拷贝） ============
    public StageRun Run { get { return _run; } }
    public Cultivation Cult { get { return _cult; } }
    public Silver Money { get { return _silver; } }
    public MaterialBag Materials { get { return _materials; } }
    public EquipBag Equips { get { return _equips; } }
    public Dao Merit { get { return _dao; } }
    public Random Rng { get { return _rng; } }
    public SkillLoadout Loadout { get { return _loadout; } }
    public Inn Inn { get { return _inn; } }
    public BattleClock Clock { get { return _clock; } }
    public BattleRuleContext Rule { get { return _rule; } }
    public Func<int, SkillConfig> Lookup { get { return _lookup; } }
    public List<SkillConfig> SkillPool { get { return _skillPool; } }
    public List<EnemyConfig> EnemyTemplates { get { return _enemyTemplates; } }
    public List<SkillConfig> EnemyPool { get { return _enemyPool; } }
    public List<EquipConfig> EquipPool { get { return _equipPool; } }
    public List<EquipSetConfig> EquipSetPool { get { return _equipSetPool; } }
    public List<EquipAffixConfig> EquipAffixPool { get { return _equipAffixPool; } }
    public int SummonCapBase { get { return _summonCapBase; } }
    public Vector2 Area { get { return _area; } }

    /// <summary>当前是不是大劫战（归 BattleManager，顶部栏只读）。</summary>
    public bool InDajie
    {
        get
        {
            if (_battleMgr == null)
            {
                return false;
            }
            return _battleMgr.InDajie;
        }
    }

    /// <summary>本世已打赢几场大劫（顶部标题用）。</summary>
    public int DajieWins
    {
        get
        {
            if (_battleMgr == null)
            {
                return 0;
            }
            return _battleMgr.DajieWins;
        }
    }

    /// <summary>结算栏「再点一次」是否已上膛（主面板用它渲染按钮文案）。</summary>
    public bool NextArmed
    {
        get
        {
            if (_battleMgr == null)
            {
                return false;
            }
            return _battleMgr.NextArmed;
        }
    }

    // ============ 生命周期 ============
    public override void OnShow()
    {
        base.OnShow();
        if (!_built)
        {
            Init();
            LoadProgress();   // 必须早于第一场战斗：把盘上的进度灌回状态机
            InitStatic();
            BuildBattleSession();
            _built = true;
        }

        AudioManager.PlayBGM("battle_01");
        if (_battleMgr != null)
        {
            _battleMgr.StartBattle();
        }
    }

    /// <summary>
    /// 建战斗逻辑并接上显示面板。
    /// 依赖顺序：状态机（Init）→ 读档 → 场景节点（InitStatic）→ 本方法。
    /// 显示面板由 GameRoot 先 Show（它是全屏底层），所以这里能直接取到 Current。
    /// </summary>
    private void BuildBattleSession()
    {
        _battleMgr = new BattleManager();

        BattlePanel view = BattlePanel.Current;
        _battleMgr.Bind(this, view);

        if (view != null)
        {
            view.AttachManager(_battleMgr);
        }
        if (_pendingDajieWins > 0)
        {
            _battleMgr.RestoreDajie(_pendingDajieWins);
        }
    }

    public override void OnHide()
    {
        _clock.StopBeat();
        _clock.StopAdvance();
        base.OnHide();
    }

    /// <summary>面板是否已完成一次性初始化（战斗侧某些回调会判它）。</summary>
    public bool Built { get { return _built; } }

    // ============ 一次性初始化 ============
    private void Init()
    {
        GameObject.MouseFilter = Control.MouseFilterEnum.Stop;
        GameObject.ProcessMode = Node.ProcessModeEnum.Always;

        var heroes = ConfigManager.GetAll<HeroConfig>();
        _roster = new List<HeroConfig>();
        foreach (int id in new[] { 2, 1, 4, 3 })
        {
            HeroConfig h = FindHero(heroes, id);
            if (h != null)
            {
                _roster.Add(h);
            }
        }

        // 0 = 空槽，不是合法武学 id —— 在入口拦掉，避免漏判时刷 "找不到ID为 0"
        _lookup = id =>
        {
            if (id <= 0)
            {
                return null;
            }
            return ConfigManager.GetOne<SkillConfig>(id);
        };

        _skillPool = new List<SkillConfig>();
        List<SkillConfig> allSkills = ConfigManager.GetAll<SkillConfig>();
        if (allSkills != null)
        {
            foreach (SkillConfig s in allSkills)
            {
                if (s != null && s.Status == SkillStatus.Implemented)
                {
                    _skillPool.Add(s);
                }
            }
        }

        _enemyTemplates = ConfigManager.GetAll<EnemyConfig>() ?? new List<EnemyConfig>();
        _enemyPool = EnemyFactory.BuildSkillPool(ConfigManager.GetAll<SkillConfig>());

        _equipPool = ConfigManager.GetAll<EquipConfig>();
        if (_equipPool == null)
        {
            _equipPool = new List<EquipConfig>();
        }
        _equipSetPool = ConfigManager.GetAll<EquipSetConfig>();
        if (_equipSetPool == null)
        {
            _equipSetPool = new List<EquipSetConfig>();
        }
        _equipAffixPool = ConfigManager.GetAll<EquipAffixConfig>();
        if (_equipAffixPool == null)
        {
            _equipAffixPool = new List<EquipAffixConfig>();
        }

        _rng = new Random();

        // 下列表的灌入顺序有硬约束：召唤物档位 / 境界乘区 / 大劫选档 / 天赋校验
        // 都必须在建队与读档之前完成，否则第一场战斗会用兜底规格。
        SummonCatalog.Init(ConfigManager.GetAll<SummonConfig>());
        Realm.Init(ConfigManager.GetAll<RealmConfig>());
        DajieFactory.Init(ConfigManager.GetAll<DajieBossConfig>());
        Talent.Init(ConfigManager.GetAll<TalentConfig>());
        BattleArt.Init();

        _summonCapBase = BattleConfig.SummonCap;

        _cult = new Cultivation();
        _silver = new Silver();
        _run = new StageRun(BuildPlayerTeam, BuildEnemyWave, 20261015);

        _inn = new Inn(heroes, ConfigManager.GetAll<QualityConfig>(),
                       id => ConfigManager.GetOne<QualityConfig>(id));
        _inn.NewLife(20261016, RosterIds());

        _loadout.Init(heroes);

        // 退出兜底：其余存档点都在"状态可能变化"的时刻，这里只防直接关窗口
        GameObject.TreeExiting += SaveProgress;
    }

    private void InitStatic()
    {
        _area = GetArea();
        GameObject.Size = _area;
        GameObject.Position = Vector2.Zero;

        // 导航条按钮（结算栏的两个按钮归 BattlePanel，不在这里接）
        if (_TInnBtn != null)
        {
            _TInnBtn.Pressed += OnOpenInn;
        }
        if (_TTalentBtn != null)
        {
            _TTalentBtn.Pressed += OnOpenTalent;
        }
        if (_TGalleryBtn != null)
        {
            _TGalleryBtn.Pressed += OnOpenGallery;
        }
        if (_TStatsBtn != null)
        {
            _TStatsBtn.Pressed += OnToggleStats;
        }
        if (_TStageBtn != null)
        {
            _TStageBtn.Pressed += OnOpenStage;
        }
        if (_TSkillBtn != null)
        {
            _TSkillBtn.Pressed += OnOpenSkills;
        }
        if (_TEquipBtn != null)
        {
            _TEquipBtn.Pressed += OnOpenEquips;
        }
        if (_TSaveBtn != null)
        {
            _TSaveBtn.Pressed += OnOpenSave;
        }
        if (_TSettingBtn != null)
        {
            _TSettingBtn.Pressed += OnOpenSettings;
        }

        // 点顶部"大劫 mm:ss"提前迎战（不加控件、不改 .tscn）
        if (_TExp != null)
        {
            _TExp.MouseFilter = Control.MouseFilterEnum.Stop;
            _TExp.GuiInput += OnDajieClockInput;
        }

        // —— 时钟：主面板订阅，再按职责转发给战斗会话 ——
        GameObject.AddChild(_clock);
        _clock.Build(SaveBackup.AutoSaveMinutes * 60);
        _clock.Ticked += OnBeat;
        _clock.AdvanceDue += OnAdvanceDue;
        _clock.AutoSaveDue += SaveProgress;
        _clock.ConfirmExpired += OnConfirmExpired;
        _clock.DajieDue += OnDajieDue;
        // 大劫间隔 = 配置 × 天赋乘区；时钟每次自己问，天赋点了下一跳就生效
        _clock.DajieIntervalProvider = () => DajieRule.Interval();

        SyncAutoSave();

        UIThemeManager.ApplyTo(GameObject);
        ApplyThemeColors();
        UIThemeManager.Changed += OnThemeChanged;

        // 导出钩子（环境变量为空时完全不生效）
        _galleryShotDir = OS.GetEnvironment("GALLERY_SHOT");
        _battleShotDir = OS.GetEnvironment("BATTLE_SHOT");
    }

    // ============ 时钟转发 ============
    private void OnBeat()
    {
        if (_galleryShotDir.Length > 0)
        {
            TickGalleryShot();
        }
        if (_battleMgr != null)
        {
            _battleMgr.OnBeat();
        }
    }

    private void OnAdvanceDue()
    {
        if (_battleMgr != null)
        {
            _battleMgr.OnAdvance();
        }
    }

    private void OnConfirmExpired()
    {
        if (_battleMgr != null)
        {
            _battleMgr.DisarmNext();
        }
    }

    private void OnDajieDue()
    {
        if (!_built)
        {
            return;
        }
        if (_run == null || _run.Layer <= 0)
        {
            return;
        }
        if (_battleMgr != null)
        {
            _battleMgr.StartDajie();
        }
    }

    /// <summary>点顶部倒计时提前迎战（用户口径："可提前打"）。</summary>
    private void OnDajieClockInput(InputEvent e)
    {
        InputEventMouseButton mb = e as InputEventMouseButton;
        if (mb == null)
        {
            return;
        }
        if (!mb.Pressed || mb.ButtonIndex != MouseButton.Left)
        {
            return;
        }
        if (InDajie || _battleMgr == null || !_battleMgr.HasEngine)
        {
            return;
        }

        AudioManager.PlayUISFX("ui_confirm");
        _battleMgr.StartDajie();
    }

    // ============ 存档 ============
    /// <summary>
    /// 存档粒度 = 一场战斗的结算点（不是每次掉落）。崩溃最多损失一场的掉落，放置游戏可接受。
    /// 触发点：每场结算 / 关子界面 / 选层 / 重开本世 / 自动保存 / 退出兜底 / 手动保存。
    /// </summary>
    public void SaveProgress()
    {
        GameSave data = CaptureProgress();
        if (data == null)
        {
            return;
        }
        SaveManager.Save(data);
    }

    private GameSave CaptureProgress()
    {
        if (_run == null || _cult == null || _silver == null)
        {
            return null;
        }

        GameSave data = GameProgress.Capture(_run, _cult, _silver, _materials, _equips, _loadout, _inn, RosterIds(), _dao);
        if (data != null)
        {
            // 大劫状态不在 GameProgress 的搬运清单里，单独补存
            data.DajieWins = DajieWins;
            data.DajieClock = _clock.DajieElapsed;
        }
        return data;
    }

    private void LoadProgress()
    {
        GameSave data = SaveManager.Load<GameSave>();
        GameProgress.Apply(data, _run, _cult, _silver, _materials, _equips, _loadout, _inn, _dao);

        if (data != null && data.Written)
        {
            _pendingDajieWins = data.DajieWins;
            _clock.DajieElapsed = data.DajieClock;   // 负值由时钟内部夹回 0
            if (_pendingDajieWins < 0)
            {
                _pendingDajieWins = 0;
            }
        }

        SyncTalentDerived();

        // 规则层的状态视图（必须等状态机都造好）
        _rule.Run = _run;
        _rule.Cult = _cult;
        _rule.Money = _silver;
        _rule.Materials = _materials;
        _rule.Equips = _equips;
        _rule.Dao = _dao;
        _rule.Rng = _rng;
        _rule.EquipPool = _equipPool;
        _rule.AffixPool = _equipAffixPool;
        _rule.RosterIds = RosterIds;
        _rule.EquipById = EquipById;
        _rule.SetById = SetById;
    }

    /// <summary>天赋带来的**结构性**变化（格子数）在这里统一落地 —— 目前只有武学槽。</summary>
    private void SyncTalentDerived()
    {
        TalentBonus tb = Talent.Bonus();

        int slots = SkillLoadout.BaseSlots + tb.SkillSlotAdd;
        if (slots < SkillLoadout.BaseSlots)
        {
            slots = SkillLoadout.BaseSlots;
        }
        if (slots > SkillLoadout.MaxSlots)
        {
            slots = SkillLoadout.MaxSlots;
        }
        SkillLoadout.Slots = slots;
    }

    private void OnOpenSave()
    {
        AudioManager.PlayUISFX("ui_open");
        _clock.StopBeat();
        _clock.StopAdvance();

        SavePanel.BindData(CaptureProgress, ApplyProgress, SyncAutoSave, OnCloseSave);
        UIManager.ShowPanel<SavePanel>();
    }

    private bool ApplyProgress(GameSave data)
    {
        bool ok = GameProgress.Apply(data, _run, _cult, _silver, _materials, _equips, _loadout, _inn, _dao);
        if (!ok)
        {
            return false;
        }

        StatsPanel.InvalidateCurrent();
        SaveProgress();
        _needRestart = true;   // 面板关闭后按新进度重开（不在面板还开着时起战斗）
        return true;
    }

    private void OnCloseSave()
    {
        if (_needRestart)
        {
            _needRestart = false;
            if (_battleMgr != null)
            {
                _battleMgr.StartBattle();
            }
            return;
        }

        ResumeBattle();
    }

    /// <summary>自动保存开关变了 → 交给时钟启停（设置本身由面板写盘）。</summary>
    private void SyncAutoSave()
    {
        SettingData settings = SettingManager.Data;
        bool on = settings != null && settings.AutoSave;
        _clock.SyncAutoSave(on);
    }

    // ============ 阵容查询 ============
    /// <summary>当前出战阵容 = 客栈编队（带当世资质）。编队为空即真正空编队。</summary>
    public List<HeroConfig> ActiveRoster()
    {
        if (_inn == null)
        {
            return _roster;
        }

        var heroes = ConfigManager.GetAll<HeroConfig>();
        var list = new List<HeroConfig>();
        foreach (int id in _inn.Roster)
        {
            HeroConfig h = FindHero(heroes, id);
            if (h != null)
            {
                list.Add(h);
            }
        }
        return list;
    }

    public List<int> RosterIds()
    {
        var ids = new List<int>();
        foreach (HeroConfig h in _roster)
        {
            ids.Add(h.Id);
        }
        return ids;
    }

    private static HeroConfig FindHero(IEnumerable<HeroConfig> heroes, int id)
    {
        if (heroes == null)
        {
            return null;
        }
        foreach (HeroConfig h in heroes)
        {
            if (h.Id == id)
            {
                return h;
            }
        }
        return null;
    }

    /// <summary>侠客 Id → 名字（奇遇/升级提示用）。</summary>
    public string HeroName(int id)
    {
        foreach (HeroConfig h in _roster)
        {
            if (h.Id == id)
            {
                return h.Name;
            }
        }
        return "#" + id;
    }

    // ============ 装备查表（战斗建队时用，规则层也收这两个委托） ============
    public EquipConfig EquipById(int id)
    {
        if (_equipPool == null)
        {
            return null;
        }
        for (int i = 0; i < _equipPool.Count; i++)
        {
            if (_equipPool[i].Id == id)
            {
                return _equipPool[i];
            }
        }
        return null;
    }

    public EquipSetConfig SetById(int id)
    {
        if (_equipSetPool == null)
        {
            return null;
        }
        for (int i = 0; i < _equipSetPool.Count; i++)
        {
            if (_equipSetPool[i].Id == id)
            {
                return _equipSetPool[i];
            }
        }
        return null;
    }

    public EquipAffixConfig AffixById(int id)
    {
        if (_equipAffixPool == null)
        {
            return null;
        }
        for (int i = 0; i < _equipAffixPool.Count; i++)
        {
            if (_equipAffixPool[i].Id == id)
            {
                return _equipAffixPool[i];
            }
        }
        return null;
    }

    /// <summary>该侠客的套装 + 词条修正（建队时解析一次）。</summary>
    public EquipTuning TuningOf(int heroId)
    {
        return EquipSets.Resolve(_equips, heroId, EquipById, AffixById, SetById);
    }

    // ============ 建队 / 波次（战斗与推图共用的工厂入口） ============
    public List<BattleUnit> BuildPlayerTeam()
    {
        TalentBonus talent = Talent.Bonus();

        // 召唤上限是引擎全局静态，每次建队前按"基础 + 天赋"**重算**而不是累加 ——
        // 否则点一次「御兽」会永久多 1 只，转世也回不来。
        int cap = _summonCapBase + talent.SummonCapAdd;
        BattleConfig.SummonCap = cap > 0 ? cap : _summonCapBase;

        return HeroBuilder.BuildTeam(ActiveRoster(), _lookup, id => _cult.LevelOf(id), includePending: false,
                                     qualityOf: id => _inn?.QualityOf(id),
                                     affixLookup: id => ConfigManager.GetOne<QualityConfig>(id),
                                     skillIdsOf: _loadout.Get,
                                     equipOf: id => _equips.BonusOf(id, EquipById),
                                     tuningOf: TuningOf,
                                     realmMult: Realm.Mult,
                                     talent: talent);
    }

    public List<BattleUnit> BuildEnemyWave(int layer, int wave, bool isFinisher)
    {
        return EnemyFactory.BuildWave(layer, wave, isFinisher, _enemyTemplates, _enemyPool);
    }

    // ============ 战斗回调（BattleManager 调这里做"主面板级"的动作） ============
    /// <summary>转世 / 重开本世时的收尾：关掉所有子界面、清统计、落盘。</summary>
    public void OnLifeReset()
    {
        CloseAllSubPanels();
        StatsPanel.InvalidateCurrent();
        _clock.ResetDajie();
        RefreshHeader();
        SaveProgress();
    }

    /// <summary>把视口存成 PNG（导出钩子用；战斗侧不该直接碰 GameObject）。</summary>
    public void SaveViewportShot(string path)
    {
        Image img = GameObject.GetViewport().GetTexture().GetImage();
        img.SavePng(path);
    }

    // ============ 子界面开关 ============
    // 约定：打开音在开关这里播（面板不知道是谁调它起来的）；
    //       关闭音在子界面内部播（它自己知道自己被关了）；
    //       **打开子界面不停战斗** —— 挂机收益不该因为看菜单而中断，
    //       唯一例外是存档面板（读档会整份替换状态机，必须停）。
    private void OnOpenInn()
    {
        AudioManager.PlayUISFX("ui_open");
        if (_TInnBtn != null)
        {
            _TInnBtn.Disabled = true;
        }
        InnPanel.BindData(_inn, _silver,
                          heroOf: id => ConfigManager.GetOne<HeroConfig>(id),
                          affixOf: id => ConfigManager.GetOne<QualityConfig>(id),
                          onClose: OnCloseInn,
                          onRosterChanged: RefreshHeader);
        UIManager.ShowPanel<InnPanel>();
    }

    private void OnCloseInn()
    {
        AudioManager.PlayUISFX("ui_close");
        if (_TInnBtn != null)
        {
            _TInnBtn.Disabled = false;
        }
        SaveProgress();
    }

    private void OnOpenSkills()
    {
        AudioManager.PlayUISFX("ui_open");
        SkillsPanel.BindData(_loadout, ActiveRoster(), _lookup, _skillPool, OnCloseSkills);
        UIManager.ShowPanel<SkillsPanel>();
    }

    private void OnCloseSkills()
    {
        SaveProgress();   // 武学装配是跨世资产，改完立刻落盘
    }

    private void OnOpenEquips()
    {
        AudioManager.PlayUISFX("ui_open");
        EquipPanel.BindData(_equips, _equipPool, ActiveRoster(), _silver, _equipSetPool, _equipAffixPool, OnCloseEquips);
        UIManager.ShowPanel<EquipPanel>();
    }

    private void OnCloseEquips()
    {
        SaveProgress();
    }

    private void OnOpenTalent()
    {
        AudioManager.PlayUISFX("ui_open");
        TalentPanel.BindData(_dao, OnCloseTalent);
        UIManager.ShowPanel<TalentPanel>();
    }

    private void OnCloseTalent()
    {
        SyncTalentDerived();   // 可能点了「藏经」→ 槽位变了
        SaveProgress();
        RefreshHeader();
    }

    private void OnOpenGallery()
    {
        AudioManager.PlayUISFX("ui_open");
        UIManager.ShowPanel<GalleryPanel>();
    }

    private void OnOpenStage()
    {
        AudioManager.PlayUISFX("ui_open");
        StagePanel.BindData(_run, _materials, OnGoLayer, OnCloseStage, InDajie);
        UIManager.ShowPanel<StagePanel>();
    }

    /// <summary>选关：换层后直接开一场新战斗（不续跑旧的那场）。</summary>
    private void OnGoLayer(int layer)
    {
        if (InDajie)
        {
            return;   // 大劫中锁死换层（选关按钮也已被禁用，这里再拦一道）
        }
        _run.JumpTo(layer);
        SaveProgress();
        RefreshHeader();
        if (_battleMgr != null)
        {
            _battleMgr.StartBattle();
        }
    }

    private void OnCloseStage()
    {
        SaveProgress();
    }

    private void OnToggleStats()
    {
        AudioManager.PlayUISFX("ui_open");
        StatsPanel.ToggleCurrent();
        if (_battleMgr != null && _battleMgr.HasEngine)
        {
            StatsPanel.BindBattle(_battleMgr.Engine, _battleMgr.Engine.A, _run.Layer);
        }
    }

    private void OnOpenSettings()
    {
        AudioManager.PlayUISFX("ui_open");
        SettingPanel.BindData(OnCloseSettings);
        UIManager.ShowPanel<SettingPanel>();
    }

    private void OnCloseSettings()
    {
        SyncAutoSave();   // 自动保存开关可能改了
        SaveProgress();
        RefreshHeader();
    }

    /// <summary>
    /// 关闭全部子界面。转世 / 读档 / 大劫失败时调用 —— 那些面板还指着要被清空的状态机。
    /// </summary>
    public void CloseAllSubPanels()
    {
        UIManager.HidePanel<InnPanel>();
        UIManager.HidePanel<SkillsPanel>();
        UIManager.HidePanel<EquipPanel>();
        UIManager.HidePanel<TalentPanel>();
        UIManager.HidePanel<GalleryPanel>();
        UIManager.HidePanel<StagePanel>();
        UIManager.HidePanel<SavePanel>();
        UIManager.HidePanel<SettingPanel>();
        UIManager.HidePanel<StatsPanel>();

        if (_TInnBtn != null)
        {
            _TInnBtn.Disabled = false;
        }
    }

    /// <summary>恢复战斗节奏 —— 只有存档面板还在用它（读档会整份替换状态机）。</summary>
    private void ResumeBattle()
    {
        if (_battleMgr == null)
        {
            return;
        }
        _battleMgr.ResumeAfterPause();
    }

    // ============ 结算栏按钮文案 ============
    // 按钮本身在 BattlePanel 的场景里（点了转发给 BattleManager），这里只负责算文案。

    /// <summary>
    /// 刷按钮文字：等确认时显示警示语（红色），平时显示"转世 · +N 道行"/"重开本世"。
    /// 抽成方法是因为顶部栏每次刷新都会走到这里 —— 直接写死会在确认中途被覆盖。
    /// </summary>
    public void RefreshNextButtonText()
    {
        if (_run == null)
        {
            return;
        }

        BattlePanel view = BattlePanel.Current;
        if (view == null)
        {
            return;
        }

        if (NextArmed)
        {
            view.SetNextButtonText("⚠ 再点一次 · 本世清零（道行保留）", true);
            return;
        }

        if (RebirthRule.CanRebirth(_run.BestLayer))
        {
            double gain = RebirthRule.DaoGain(_run.BestLayer);
            view.SetNextButtonText("转世 · +" + gain.ToString("0.#") + " 道行", false);
        }
        else
        {
            view.SetNextButtonText("重开本世", false);
        }
    }

    // ============ 顶部状态栏 ============
    public void RefreshHeader()
    {
        if (_run == null)
        {
            return;
        }

        string waveTag = _run.IsFinisherWave
            ? (_run.IsBossLayer ? "  ★层主波" : "  ◆精英波")
            : "";
        string mode = _run.AutoPush ? "" : "  ⏸挂机中";

        if (_TTitle != null)
        {
            if (InDajie)
            {
                _TTitle.Text = "《轮回大侠》· 大劫上门（第 " + (DajieWins + 1) + " 场）";
            }
            else
            {
                _TTitle.Text = "《轮回大侠》· 第 " + _run.Layer + " 层 " + _run.Wave + "/" + _run.WavesInLayer + " 波" + waveTag + mode;
            }
        }

        // 修为池是"每侠客各一份"，绝对值不如进度直观；战斗中途未结算升级 → 进度可超 100%，clamp
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

        string realmTag = "";
        RealmConfig realmCur = Realm.Current;
        if (realmCur != null)
        {
            realmTag = "境界 " + realmCur.Name + "×" + realmCur.Mult.ToString("0") + "　｜　";
        }
        if (_dao.TotalGained > 0)
        {
            realmTag += "道行 " + _dao.Amount.ToString("0.#") + "　｜　";
        }

        if (InDajie)
        {
            realmTag = "⚔ 大劫中　｜　" + realmTag;
        }
        else
        {
            double remain = DajieRule.Remain(_clock.DajieElapsed);
            realmTag = "大劫 " + ((int)(remain / 60)).ToString("00") + ":"
                     + ((int)(remain % 60)).ToString("00") + "（点此迎战）　｜　" + realmTag;
        }

        RefreshNextButtonText();

        if (_TExp != null)
        {
            _TExp.Text = realmTag
                       + "最高 " + _run.BestLayer + " 层 · 已战 " + _run.Battles + " 场　｜　"
                       + "银两 " + BigNum.From(_silver.Amount).ToString(2) + "（本世累计 " + BigNum.From(_silver.TotalGained).ToString(2) + "）"
                       + "　修为累计 " + BigNum.From(_cult.TotalGained).ToString(2) + "　｜　" + lv;
        }
    }

    // ============ 主题 ============
    /// <summary>
    /// 主题变更：刷子界面样式，再把"重建卡牌与背景"交给 BattlePanel
    /// （本面板没有战斗力相关的节点，只有导航条与顶栏）。
    /// </summary>
    private void ApplyThemeColors()
    {
        StagePanel.ApplyThemeCurrent();
        SkillsPanel.ApplyThemeCurrent();
        StatsPanel.ApplyThemeCurrent();
        SettingPanel.ApplyThemeCurrent();
        EquipPanel.ApplyThemeCurrent();
        SavePanel.ApplyThemeCurrent();

        // 卡牌与堆叠卡的底色写死在 ColorRect 上，换主题必须重建 ——
        // 但那些节点归 BattlePanel，主面板只把"重建"这个动作交过去。
        BattlePanel view = BattlePanel.Current;
        if (view == null || _battleMgr == null)
        {
            return;
        }
        view.ApplyTheme(_battleMgr.Engine);
    }

    private void OnThemeChanged(int themeId)
    {
        UIThemeManager.ApplyTo(GameObject);
        ApplyThemeColors();
    }

    private Vector2 GetArea()
    {
        var r = GameObject.GetViewportRect().Size;
        if (r.X < 2 || r.Y < 2)
        {
            r = new Vector2(1920, 1080);   // 设计分辨率兜底
        }
        return r;
    }

    // ============ 导出钩子（GALLERY_SHOT / BATTLE_SHOT） ============
    private string _galleryShotDir = "";
    private string _battleShotDir = "";
    private int _galleryShotPhase;
    private double _galleryShotMark;

    /// <summary>验证视差用的两个固定倾斜角（左右各满倾）。</summary>
    static readonly Vector2[] GalleryShotTilts =
    {
        new Vector2(-0.9f, 0f),
        new Vector2(0.9f, 0f),
    };

    /// <summary>战斗画面的截图由战斗会话判定时机，主面板只负责存图。</summary>
    public bool BattleShotWanted
    {
        get { return _battleShotDir.Length > 0; }
    }

    /// <summary>已过的秒数（节拍数 × 0.1），导出钩子用它做延时。</summary>
    public double Elapsed
    {
        get { return _clock.Elapsed; }
    }

    public void SaveBattleShot()
    {
        string path = _battleShotDir.PathJoin("battle.png");
        SaveViewportShot(path);

        double vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
        GD.Print("[Shot] " + path + " · 显存 " + (vram / 1048576.0).ToString("0") + " MB");
        GameObject.GetTree().Quit();
    }

    /// <summary>
    /// 卡册截图：打开 → 定格左右满倾各截一张。
    /// 为什么要定格：卡在自动摆动时两张相隔 1 秒只差 1~2 像素，测不出各层深度差。
    /// </summary>
    private void TickGalleryShot()
    {
        if (_galleryShotPhase == 0)
        {
            if (_clock.Elapsed > 0.8)
            {
                OnOpenGallery();
                GalleryPanel.SetForcedTilt(true, GalleryShotTilts[0]);
                _galleryShotPhase = 1;
                _galleryShotMark = _clock.Elapsed;
            }
            return;
        }

        if (_galleryShotPhase == 1 && _clock.Elapsed - _galleryShotMark > 1.0)
        {
            string path = _galleryShotDir.PathJoin("gallery_a.png");
            SaveViewportShot(path);
            GD.Print("[Shot] " + path);

            GalleryPanel.SetForcedTilt(true, GalleryShotTilts[1]);
            _galleryShotPhase = 2;
            _galleryShotMark = _clock.Elapsed;
            return;
        }

        if (_galleryShotPhase == 2 && _clock.Elapsed - _galleryShotMark > 1.0)
        {
            string path = _galleryShotDir.PathJoin("gallery_b.png");
            SaveViewportShot(path);
            GD.Print("[Shot] " + path);
            GameObject.GetTree().Quit();
        }
    }
}
