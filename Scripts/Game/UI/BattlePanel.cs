// 文件：BattlePanel.cs
// 《轮回大侠》M2.2 推图 + 掉落 —— **编排层**（BasePanel 子类；不持有任何显示子模块的内部状态）
// 发动机：BattleEngine（纯 C# 引擎，与 M0 同款数学）
//
// 架构（2026-10-17 起）：显示与逻辑分离 + 表现层按显示子模块拆成**独立 Godot 节点组件**：
//   · Resources/UI/BattlePanel.tscn   —— 静态外壳；显示子模块节点各挂一个脚本（见下）
//   · Scripts/Game/UI/BattlePanel.cs  —— 本文件：**只做编排**（生命周期、推图状态机、开战/结算、
//                                        掉落、事件接线、逐帧驱动、顶部信息栏、共享工具）；
//                                        不持有卡表/堆叠表/飘字表/战报行 —— 那些归各自组件私有。
//   · Scripts/Game/UI/BattleClock.cs     —— **计时器与时间状态**（战斗节拍 / 结算推进 / 自动保存 /
//                                        二次确认超时 / 大劫倒计时）；面板只订阅它的回调
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

    /// <summary>
    /// 全部计时器与时间状态都在这里（2026-11-05 从本类剥离）。
    /// 面板只订阅回调（节拍 / 结算推进 / 自动保存 / 确认超时 / 大劫到点），
    /// 不 new Timer、不算毫秒差 —— 调节奏改 BattleClock，不用翻这个 1800 行的文件。
    /// </summary>
    private readonly BattleClock _clock = new BattleClock();

    private bool _nextArmed;              // 已经点过第一下，等第二下确认
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
    private List<EquipSetConfig> _equipSetPool;      // 6 套装（P1）
    private List<EquipAffixConfig> _equipAffixPool;  // 随机词条池（P1）
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
    // —— M3 道行（永久货币；转世折算唯一来源，天赋树唯一出口）——
    private readonly Dao _dao = new Dao();

    /// <summary>
    /// 战斗规则层（`Scripts/Game/BattleRule/`）拿到的状态视图。
    /// 面板只把**引用**交出去（Init 末尾绑一次），掉落 / 结算 / 大劫 / 转世怎么算账全在那边；
    /// 本类剩下的只有"什么时候调"和"怎么显示"。
    /// </summary>
    private readonly BattleRuleContext _rule = new BattleRuleContext();

    // —— M3 大劫（定时上门的 Boss 战）——
    // 计时全在 _clock：累计秒数读 DajieElapsed，暂停用 SetDajiePaused，
    // 到点由 DajieDue 回调进来。这里只留玩法状态（是不是大劫战、打到第几场）。
    private bool _inDajie;               // 当前这场是不是大劫战
    private int _dajieWins;              // 本世已打赢几场（决定下一场强度档位）
    private List<BattleUnit> _dajieFoes;  // 大劫战的敌方阵容（StartBattle 里用它代替推图波次）
    private DajieBossConfig _dajieStage;   // 本场大劫的档位（悬赏清单：道行 + 局内资源，开始时就定死）
    private bool _pendingRebirth;        // 大劫输了 → 等结算栏展示完再转世
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
        _clock.StopBeat();
        _clock.StopAdvance();
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
            if (h != null)
            {
                _roster.Add(h);
            }
        }
        // 0 = 空槽（SkillLoadout.Empty），不是合法武学 id —— 在入口拦掉，
        // 避免任何调用方漏判时空查表刷 "找不到ID为 0" 的噪音
        _lookup = id =>
        {
            if (id <= 0)
            {
                return null;
            }
            return ConfigManager.GetOne<SkillConfig>(id);
        };

        // M2.4 武学池 = 全部「已实装」武学（不按 Tier 限制；TierCap 只约束敌方，见 SkillConfig.Tier 注释）
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

        // 敌方阵容：单位模板（EnemyConfig 表）+ 武学池（已实装全池；按 Id 定序 → 抽取可复现）
        _enemyTemplates = ConfigManager.GetAll<EnemyConfig>() ?? new List<EnemyConfig>();
        _enemyPool = EnemyFactory.BuildSkillPool(ConfigManager.GetAll<SkillConfig>());

        // M2.5 装备：模板池 + 掉落随机源
        _equipPool = ConfigManager.GetAll<EquipConfig>();
        if (_equipPool == null)
        {
            _equipPool = new List<EquipConfig>();
        }
        // P1 套装与随机词条
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
        // 召唤物类型档位（SummonConfig 表）——必须在建队前灌入，否则召唤物走引擎兜底规格
        SummonCatalog.Init(ConfigManager.GetAll<SummonConfig>());
        // 境界表（RealmConfig）——同样必须在建队前灌入：HeroBuilder 建队时要乘境界乘区
        Realm.Init(ConfigManager.GetAll<RealmConfig>());
        // 大劫表（DajieBossConfig）——大劫上门时按表选档；没灌入会回退到当前层收尾波
        DajieFactory.Init(ConfigManager.GetAll<DajieBossConfig>());
        // 天赋表（TalentConfig）——必须早于 LoadProgress：读档时要按它校验节点 Id 并夹等级
        Talent.Init(ConfigManager.GetAll<TalentConfig>());

        // 战斗卡面素材映射（侠客名 → Resources/UI/Gallery 下的 4 层目录）——必须早于第一次建卡
        BattleArt.Init();

        // 召唤上限的基础值（Tuning 已在此之前注入）—— 天赋「御兽」在这个基础值上做加法。
        // 记在这里而不是每次读表，是为了让"表里改了但代码没改"时表现仍由 Tuning 决定。
        _summonCapBase = BattleConfig.SummonCap;

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

        // 大劫：点顶部倒计时可以提前迎战（不加控件、不改 .tscn）
        if (_TExp != null)
        {
            _TExp.MouseFilter = Control.MouseFilterEnum.Stop;
            _TExp.GuiInput += OnDajieClockInput;
        }

        // —— 战斗规则层的状态视图（必须等上面几个状态机都造好）——
        // 只交引用，不交所有权：这些对象仍归面板持有，规则调用它们的方法改账。
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

    /// <summary>
    /// 点顶部"大劫 mm:ss"提前迎战（用户口径："可提前打"）。
    /// 为什么挂在 Label 上而不是新加按钮：导航条已经 7 个按钮、结算栏还有 2 个，
    /// 再塞一个就挤了；而"倒计时"本身就是玩家盯的那块地方，点它最符合直觉。
    /// </summary>
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
        if (_inDajie || _resultShown || _engine == null)
        {
            return;
        }
        AudioManager.PlayUISFX("ui_confirm");
        StartDajie();
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
        if (data == null)
        {
            return;
        }

        SaveManager.Save(data);
    }

    /// <summary>抓当前进度（给存档面板的"保存/备份/导出"共用同一条路径）。</summary>
    private GameSave CaptureProgress()
    {
        // 状态机还没造好（Init 未跑完）时返回 null，避免初始化的中间态被写盘
        if (_run == null || _cult == null || _silver == null)
        {
            return null;
        }

        GameSave data = GameProgress.Capture(_run, _cult, _silver, _materials, _equips, _loadout, _inn, RosterIds(), _dao);
        if (data != null)
        {
            // 大劫状态是表现层自己的字段（不在 GameProgress 的搬运清单里），单独补存
            data.DajieWins = _dajieWins;
            data.DajieClock = _clock.DajieElapsed;
        }
        return data;
    }

    /// <summary>读主档：没存过盘 / 版本不认识时保持"新一局"状态（各状态机已是初始值）。</summary>
    private void LoadProgress()
    {
        GameSave data = SaveManager.Load<GameSave>();
        GameProgress.Apply(data, _run, _cult, _silver, _materials, _equips, _loadout, _inn, _dao);

        // 大劫进度（GameProgress 不管这两个字段，读档侧自己补）
        if (data != null && data.Written)
        {
            _dajieWins = data.DajieWins;
            _clock.DajieElapsed = data.DajieClock;   // 负值由时钟内部夹回 0
            if (_dajieWins < 0)
            {
                _dajieWins = 0;
            }
        }

        // 天赋恢复之后才能定"武学槽几格"—— 顺序在这里而不是 Init，是因为读档会覆盖天赋等级
        SyncTalentDerived();
    }

    /// <summary>
    /// 天赋带来的**结构性**变化（不是乘区，而是"格子数"）在这里统一落地。
    /// 目前只有武学槽：天赋「藏经」+1 → 3 格变 4 格。
    /// 为什么要夹 MaxSlots：内部数组按 <see cref="SkillLoadout.MaxSlots"/> 开，
    /// 超出会让 Equip/Clear 越界；表里多配一层不该让游戏崩。
    /// </summary>
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

    // ---------- 存档面板（保存 / 备份 / 载入）----------
    private void OnOpenSave()
    {
        AudioManager.PlayUISFX("ui_open");
        _clock.StopBeat();
        _clock.StopAdvance();

        SavePanel.BindData(CaptureProgress, ApplyProgress, SyncAutoSave, OnCloseSave);
        UIManager.ShowPanel<SavePanel>();
    }

    /// <summary>载入某份存档：灌回状态机 → 立刻写成主档（下次启动就是这份）→ 标记重开一场。</summary>
    private bool ApplyProgress(GameSave data)
    {
        bool ok = GameProgress.Apply(data, _run, _cult, _silver, _materials, _equips, _loadout, _inn, _dao);
        if (!ok)
        {
            return false;
        }

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

    /// <summary>自动保存开关变了 → 交给时钟启停（设置本身由面板写盘）。</summary>
    private void SyncAutoSave()
    {
        SettingData settings = SettingManager.Data;
        bool on = settings != null && settings.AutoSave;
        _clock.SyncAutoSave(on);
    }

    /// <summary>M2.3 客栈：当前出战阵容 = 客栈编队（带当世资质）。编队为空即真正空编队（不再回退开局 4 名）。</summary>
    private List<HeroConfig> ActiveRoster()
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
        return list;   // 可能为空（全下阵）→ 真正空编队，下一场按空队打
    }

    private List<int> RosterIds()
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

    /// <summary>天赋「御兽」用：Tuning 注入后的召唤上限基础值（天赋在此之上加）。</summary>
    private int _summonCapBase;

    private List<BattleUnit> BuildPlayerTeam()
    {
        // 天赋是"所有世"的横向乘区，与境界（这一世的纵向乘区）**乘算**叠加。
        // 一个节点都没点时 Bonus() 返回 Identity（全 1），等于天赋树不存在。
        TalentBonus talent = Talent.Bonus();

        // 召唤上限是引擎的全局静态（值由 Tuning 注入），所以每次建队前按"基础 + 天赋"**重算**，
        // 不是累加 —— 否则点一次「御兽」会永久多 1 只，转世也回不来。
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

    /// <summary>装备模板查表（M2.5）。建队时才调，不在每帧路径上。</summary>
    private EquipConfig EquipById(int id)
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

    /// <summary>套装表查表（P1）。</summary>
    private EquipSetConfig SetById(int id)
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

    /// <summary>装备词条表查表（P1）。</summary>
    private EquipAffixConfig AffixById(int id)
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
    private EquipTuning TuningOf(int heroId)
    {
        return EquipSets.Resolve(_equips, heroId, EquipById, AffixById, SetById);
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
        if (_TPushBtn != null)
        {
            _TPushBtn.Pressed += OnResumePush;
        }
        if (_TNextBtn != null)
        {
            _TNextBtn.Pressed += OnNextPressed;   // 两下确认，见 OnNextPressed
        }
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

        // —— 计时器总装（2026-11-05 剥离到 BattleClock）——
        // 时钟只负责"到点喊一声"，喊回来的是本面板的业务方法；节奏参数全在 BattleClock 里。
        GameObject.AddChild(_clock);
        _clock.Build(SaveBackup.AutoSaveMinutes * 60);
        _clock.Ticked += OnBeat;
        _clock.AdvanceDue += OnAdvance;
        _clock.AutoSaveDue += SaveProgress;
        _clock.ConfirmExpired += DisarmNext;
        _clock.DajieDue += OnDajieDue;

        // 大劫间隔 = 配置 × 天赋乘区。交给时钟**每次自己问**（而不是缓存一个值），
        // 天赋点了下一跳就生效，面板里不用到处同步缓存。
        _clock.DajieIntervalProvider = () => DajieRule.Interval();

        SyncAutoSave();

        UIThemeManager.ApplyTo(GameObject);
        ApplyThemeColors();
        UIThemeManager.Changed += OnThemeChanged;

        // —— 临时：卡册截图验证（GALLERY_SHOT=<目录> 时自动打开卡册、截一张、退出）——
        // —— 导出钩子（环境变量为空时完全不生效，只在生成宣传图/验收截图时用）——
        //   GALLERY_SHOT=<目录>：打开卡册，定格左右两个满倾角各截一张
        //   BATTLE_SHOT=<目录> ：开局 1.4s 后截一张战斗画面（此时双方已互相打了几下，血条不全是满的）
        _galleryShotDir = OS.GetEnvironment("GALLERY_SHOT");
        _battleShotDir = OS.GetEnvironment("BATTLE_SHOT");
    }

    private string _galleryShotDir = "";
    private int _galleryShotPhase;
    private double _galleryShotMark;   // 上一次阶段切换时的时钟刻度（不是自己累加的秒数）

    /// <summary>验证视差用的两个固定倾斜角（左右各满倾）。</summary>
    static readonly Vector2[] GalleryShotTilts =
    {
        new Vector2(-0.9f, 0f),
        new Vector2(0.9f, 0f),
    };

    /// <summary>
    /// 临时验证钩子：打开卡册 → 定格倾斜角 → 各截一张。截完删。
    /// 为什么要定格：卡在自动摆动时两张相隔 1 秒只差 1~2 像素，测不出各层的深度差；
    /// 定格到左右满倾，两层之间的位移差才是"按深度分层"的硬证据。
    /// </summary>
    private void TickGalleryShot()
    {
        // 时间刻度直接读时钟的累计节拍秒（本类不再自己累加 —— 计时归 BattleClock）
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
            Image img = GameObject.GetViewport().GetTexture().GetImage();
            string path = _galleryShotDir.PathJoin("gallery_a.png");
            img.SavePng(path);
            GD.Print("[Shot] " + path);

            GalleryPanel.SetForcedTilt(true, GalleryShotTilts[1]);
            _galleryShotPhase = 2;
            _galleryShotMark = _clock.Elapsed;
            return;
        }

        if (_galleryShotPhase == 2 && _clock.Elapsed - _galleryShotMark > 1.0)
        {
            Image img = GameObject.GetViewport().GetTexture().GetImage();
            string path = _galleryShotDir.PathJoin("gallery_b.png");
            img.SavePng(path);
            GD.Print("[Shot] " + path);

            GameObject.GetTree().Quit();
        }
    }

    // ============ 开战 ============
    /// <summary>战斗画面导出钩子（BATTLE_SHOT 为空时不生效）。等 1.4s 是为了让双方都打出过伤害。</summary>
    private string _battleShotDir = "";

    private void TickBattleShot()
    {
        if (_clock.Elapsed < 1.4)
        {
            return;
        }

        Image img = GameObject.GetViewport().GetTexture().GetImage();
        string path = _battleShotDir.PathJoin("battle.png");
        img.SavePng(path);

        // 顺带报一下显存：卡面贴图是这套 UI 里唯一的重资源，出图时看一眼能提前发现回归
        double vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
        GD.Print("[Shot] " + path + " · 显存 " + (vram / 1048576.0).ToString("0") + " MB");
        GameObject.GetTree().Quit();
    }

    private void StartBattle()
    {
        ClearBattle();
        _clock.StopAdvance();
        if (_TNextBtn != null)
        {
            _TNextBtn.Visible = false;
        }
        if (_TPushBtn != null)
        {
            _TPushBtn.Visible = false;
        }

        _cult.BeginBattle();      // 本场掉落计数清零
        _silver.BeginBattle();

        // 大劫战：走独立引擎（满血新队 vs 强化收尾波），**不动推图的层/波状态**
        if (_inDajie && _dajieFoes != null)
        {
            _engine = new BattleEngine();
            _engine.SetTeams(BuildPlayerTeam(), _dajieFoes, _rng.Next());
        }
        else
        {
            _engine = _run.Begin();
        }
        HookEvents();

        _areaX = GetArea().X;
        StatsPanel.BindBattle(_engine, _engine.A, _run.Layer);   // 战报：注入引擎/阵容/层（面板未创建则先存着）
        _cards.Build(_engine.A, _engine.B, _areaX);

        _resultShown = false;
        SyncClockPause();     // 结算展示结束 → 大劫计时恢复（若这场就是大劫，则继续停着）
        if (_TResult != null)
        {
            _TResult.Visible = false;
        }
        RefreshHeader();
        _clock.StartBeat();
        _cards.Refresh(_engine);
        _stacks.Sync(_engine, _areaX);
    }

    /// <summary>
    /// 境界突破（**自动**）：清层后检查双门槛（银两 + 已达层数），满足就进一境。
    /// 为什么不做成按钮：挂机定位下"钱攒够了就自己破"不需要玩家守着点，
    /// 仪式感交给结算栏的提示与音效，而不是交给一次点击。
    /// 判定本身在 `BattleSettle.TryBreakRealm`，这里只播音 + 拼提示。
    /// 返回结算栏提示（空串 = 没突破）。
    /// </summary>
    private string TryAutoBreakRealm()
    {
        RealmConfig nx = BattleSettle.TryBreakRealm(_silver, _run.BestLayer);
        if (nx == null)
        {
            return "";
        }

        AudioManager.PlayEventSFX("clear_layer");
        return " · 境界→" + nx.Name + "（属性 ×" + nx.Mult.ToString("0") + "）";
    }

    /// <summary>
    /// 「转世 / 重开本世」按钮 —— **两下确认**。
    /// <para>
    /// 这一下会把本世资产全部清零（层数 / 等级 / 银两 / 材料 / 装备 / 境界），
    /// 而按钮就贴在结算栏上、跟「继续推图」挨着，误触一次就是一世的进度。
    /// 挂机游戏里玩家多半是边刷边点，值一次二次确认。
    /// </para>
    /// 大劫打输触发的转世**不走这里**（那是系统判负，不需要玩家点头，见 OnAdvance）。
    /// </summary>
    private void OnNextPressed()
    {
        if (!_nextArmed)
        {
            _nextArmed = true;
            AudioManager.PlayUISFX("ui_click");
            RefreshNextButtonText();
            _clock.StartConfirm();
            return;
        }

        DisarmNext();
        OnNext();
    }

    /// <summary>解除"等确认"状态（超时 / 已确认 / 结算刷新时都走这里）。</summary>
    private void DisarmNext()
    {
        _clock.StopConfirm();
        if (!_nextArmed)
        {
            return;
        }

        _nextArmed = false;
        RefreshNextButtonText();
    }

    /// <summary>
    /// 刷按钮文字：等确认时显示警示语（红色），平时显示"转世 · +N 道行"/"重开本世"。
    /// 抽成方法是因为 <see cref="RefreshHeader"/> 每帧都在刷它 —— 直接写死会在确认中途被覆盖。
    /// </summary>
    private void RefreshNextButtonText()
    {
        if (_TNextBtn == null || _run == null)
        {
            return;
        }

        if (_nextArmed)
        {
            _TNextBtn.Text = "⚠ 再点一次 · 本世清零（道行保留）";
            _TNextBtn.AddThemeColorOverride("font_color", UISignalColors.Lose);
            return;
        }

        _TNextBtn.RemoveThemeColorOverride("font_color");
        if (RebirthRule.CanRebirth(_run.BestLayer))
        {
            double gain = RebirthRule.DaoGain(_run.BestLayer);
            _TNextBtn.Text = "转世 · +" + gain.ToString("0.#") + " 道行";
        }
        else
        {
            _TNextBtn.Text = "重开本世";
        }
    }

    /// <summary>
    /// 转世 / 重开本世。
    /// 折算与归零的**规则**在 `RebirthRule`（够不够门槛、发多少道行、清哪六本账），
    /// 这里只做面板该做的事：关子界面、清大劫档位、落盘、重开一场。
    /// </summary>
    private void OnNext()
    {
        // 大劫打输也会走到这里（OnAdvance → OnNext）：先把"等确认"状态清掉，
        // 否则新一世的按钮会停在警示文案上。
        DisarmNext();
        AudioManager.PlayUISFX("ui_confirm");

        // 转世会把"此生"资产全部清零。如果这时玩家正开着子界面（战斗现在不停，
        // 大劫随时可能输掉触发转世），那些面板还指着已被清空的状态机 ——
        // 轻则显示过期数字，重则操作作用在归零后的账户上（花钱买不到人）。
        // 所以先无条件关掉所有子界面，让玩家回到主界面看到"新的一世"。
        CloseAllSubPanels();

        // ① 发道行（够门槛才有；天赋「天机」的乘区在规则里乘）
        RebirthRule.GrantDao(_rule, _run.BestLayer);

        // ② 清零本世资产（层 / 修为 / 银两 / 材料 / 装备 / 境界）
        RebirthRule.ResetLife(_rule);

        // ③ 只有面板知道的两件事：大劫档位与倒计时
        _dajieWins = 0;
        _clock.ResetDajie();

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

    /// <summary>
    /// 大劫结算。赢 → 记一次胜利、重新计时、回推图；输 → 转世（发道行 + 归零 + 重开）。
    /// 输的时候先让结算栏亮 2 秒再重开 —— 玩家得知道自己是"被大劫打死的"，
    /// 而不是莫名其妙回到第 1 层。
    /// </summary>
    private void OnDajieEnded(bool win)
    {
        _inDajie = false;
        _dajieFoes = null;
        // 大劫结束，但结算栏还在展示（_resultShown 仍为 true）→ 计时继续停着，
        // 等下一场 StartBattle 把结算标记清掉才恢复
        SyncClockPause();

        // 大劫打完了 → 选关面板如果还开着，把换层解锁（它可能是大劫前打开的）
        StagePanel.RefreshLockIfOpen(false);

        if (win)
        {
            // 奖励先发：道行（永久）+ 修为 / 银两 / 材料 / 装备（局内资源）
            string rewardTag = GrantDajieReward();
            _dajieWins++;
            _clock.ResetDajie();

            // 打赢也要说一声：玩家可能正开着面板，看不到主界面的结算栏
            DajieBanner.Flash("⚔ 大劫已过（第 " + _dajieWins + " 场）· 奖励已入账");

            if (_TResult != null)
            {
                _TResult.Text = "⚔ 大劫已过（第 " + _dajieWins + " 场）" + rewardTag
                              + " · " + (DajieRule.Interval() / 60).ToString("0.#") + " 分钟后再来";
                _TResult.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.35f));
                _TResult.Visible = true;
            }
            RefreshHeader();
            SaveProgress();
            _clock.StartAdvance(BattleClock.AdvanceSec);   // 回推图
            return;
        }

        if (_TResult != null)
        {
            _TResult.Text = "☠ 大劫不敌（第 " + (_dajieWins + 1) + " 场）· 转世重来";
            _TResult.AddThemeColorOverride("font_color", new Color(1f, 0.4f, 0.35f));
            _TResult.Visible = true;
        }
        _pendingRebirth = true;
        _clock.StartAdvance(BattleClock.AdvanceSecLonger);
    }

    /// <summary>
    /// 大劫悬赏的**文案**（发奖本身在 `DajieRule.Grant`）。
    /// 规则算完只回一份读数，这里负责把它排成人话 —— 面板不该关心"道行乘了哪个乘区"。
    /// 返回结算栏用的片段（空串 = 这一档什么奖励都没配）。
    /// </summary>
    private string GrantDajieReward()
    {
        int layer = _run != null ? _run.Layer : 1;
        DajieReward reward = DajieRule.Grant(_rule, _dajieStage, layer);
        if (!reward.Any)
        {
            return "";
        }

        var parts = new List<string>();

        if (reward.Dao > 0)
        {
            parts.Add("道行 +" + reward.Dao.ToString("0.#"));
        }

        if (reward.ExpPerHead > 0)
        {
            parts.Add("修为 +" + reward.ExpPerHead.ToString("0.#") + "/人");
            parts.Add("银两 +" + BigNum.From(reward.Silver).ToString(2));
            parts.Add(MaterialCatalog.NameOf(reward.MatTier) + " +" + reward.MatCount);
        }

        if (reward.EquipCount == 1)
        {
            parts.Add("得" + reward.EquipName);
        }
        else if (reward.EquipCount > 1)
        {
            parts.Add("得装备 ×" + reward.EquipCount);
        }

        foreach (Cultivation.LevelUp up in reward.LevelUps)
        {
            parts.Add(HeroName(up.HeroId) + "↑L" + up.To);
        }

        if (parts.Count == 0)
        {
            return "";
        }
        return " · " + string.Join(" · ", parts);
    }

    /// <summary>结算后开下一场（推图模式：胜则已进层 / 挂机模式：原地重复刷）。</summary>
    private void OnAdvance()
    {
        // 大劫输了：这一拍不推图，直接转世（发道行 + 归零 + 重开）
        if (_pendingRebirth)
        {
            _pendingRebirth = false;
            _dajieWins = 0;                 // 新一世，大劫从第 1 档重新爬
            OnNext();                       // 计时清零在 OnNext 里一并做
            return;
        }
        StartBattle();
    }

    // ============ M2.3 客栈 ============
    /// <summary>
    /// 打开客栈。**战斗不停**（用户 2026-11-02："打开子界面时，战斗不要停止"）——
    /// 挂机游戏看菜单不该断收益，而且客栈改的编队/资质本来就是**下一场**才生效，
    /// 对正在跑的这一场没有任何影响，没有停的必要。
    /// </summary>
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

    /// <summary>关闭客栈的回调（由 InnPanel 关闭按钮触发）：恢复按钮 + 落盘。</summary>
    private void OnCloseInn()
    {
        AudioManager.PlayUISFX("ui_close");
        if (_TInnBtn != null)
        {
            _TInnBtn.Disabled = false;
        }
        SaveProgress();   // 招募 / 换编队 / 刷新都在客栈里发生
    }

    // ============ 子界面开关 ============
    // 统一约定（2026-11-02 修订）：
    //   · 打开音在此处播（面板不知道是谁调它起来的）
    //   · 关闭音在面板内部播（面板自己知道自己被关了）
    //   · **打开子界面不停战斗** —— 挂机游戏的收益不该因为看菜单而中断；
    //     而且这些面板改的都是"下一场建队时才读"的数据（编队/装备/武学/天赋），
    //     碰不到正在跑的 `_engine`，天然安全
    //   · **唯一例外是存档面板**：读档会整份替换状态机，必须停（见 OnOpenSave）

    // ============ 武学装配（M2.4）============
    private void OnOpenSkills()
    {
        AudioManager.PlayUISFX("ui_open");

        SkillsPanel.BindData(_loadout, ActiveRoster(), _lookup, _skillPool, OnCloseSkills);
        UIManager.ShowPanel<SkillsPanel>();
    }

    /// <summary>关武学面板：只落盘，不碰战斗。</summary>
    private void OnCloseSkills()
    {
        SaveProgress();   // 武学装配是跨世资产，改完立刻落盘
    }

    // ============ 装备（M2.5）============
    private void OnOpenEquips()
    {
        AudioManager.PlayUISFX("ui_open");

        EquipPanel.BindData(_equips, _equipPool, ActiveRoster(), _silver, _equipSetPool, _equipAffixPool, OnCloseEquips);
        UIManager.ShowPanel<EquipPanel>();
    }

    private void OnCloseEquips()
    {
        SaveProgress();   // 穿脱 / 强化都在面板里改，关闭时才写盘
    }

    // ============ 天赋 ============
    /// <summary>
    /// 打开天赋树。天赋是**跨世永久**资产，点了立刻写盘 —— 所以关闭时必须存一次，
    /// 不能等"下次结算才存"，否则玩家点完天赋直接关窗口就丢了。
    /// </summary>
    private void OnOpenTalent()
    {
        AudioManager.PlayUISFX("ui_open");

        // 一行诊断：天赋是第一次接入的面板，出问题（表没灌 / 节点没绑上）时这行能立刻定位
        GD.Print("[Talent] 打开天赋面板 · 表 " + Talent.Nodes.Count + " 节点 · 道行 "
                 + _dao.Amount.ToString("0.#"));

        TalentPanel.BindData(_dao, OnCloseTalent);
        UIManager.ShowPanel<TalentPanel>();
    }

    private void OnCloseTalent()
    {
        UIManager.ClosePanel<TalentPanel>();
        SyncTalentDerived();   // 可能刚点了「藏经」，武学槽要立刻变成 4 格
        SaveProgress();
        RefreshHeader();   // 道行余额在顶部战况行里，扣完要立刻刷新
    }

    // ============ 卡册（4 层视差卡面预览）============
    /// <summary>
    /// 打开卡册。**只读**：不碰任何状态机，所以不停战斗、不存档、不刷头部。
    /// 为什么单独做一个面板而不是塞进客栈：客栈是"招募/编队"的操作面板，
    /// 卡册是"看画"的浏览面板 —— 两者的注意力相反（一个要点按钮，一个只要鼠标晃）。
    /// </summary>
    private void OnOpenGallery()
    {
        AudioManager.PlayUISFX("ui_open");
        GalleryPanel.BindData(OnCloseGallery);
        UIManager.ShowPanel<GalleryPanel>();
    }

    private void OnCloseGallery()
    {
        // 卡册是只读的，关闭时没有任何状态要落盘，也不需要续跑战斗
    }

    // ============ 选关 ============
    private void OnOpenStage()
    {
        AudioManager.PlayUISFX("ui_open");

        StagePanel.BindData(_run, _materials, OnGoLayer, OnCloseStage, _inDajie);
        UIManager.ShowPanel<StagePanel>();
    }

    /// <summary>
    /// 选某层：锁层挂机 + 关面板 + 重开一场（换层必须重开，因为敌方阵容已变）。
    /// <para>
    /// **大劫进行中一律拒绝**：大劫战走的是独立引擎（<see cref="_dajieFoes"/> + 新的 BattleEngine），
    /// 换层会调 <c>StartBattle</c> 直接把它顶掉 —— 那就等于"花一次选关把强制战斗刷没了"，
    /// 而这个强制战斗正是转世的唯一触发器。所以它必须不能被跳过。
    /// </para>
    /// </summary>
    private void OnGoLayer(int layer)
    {
        if (_inDajie)
        {
            AudioManager.PlayUISFX("ui_cancel");
            DajieBanner.Flash("⚔ 大劫未过 · 现在不能换层（打赢或打输结算之后才行）");
            return;
        }

        _run.JumpTo(layer);
        UIManager.ClosePanel<StagePanel>();   // 选完即销毁，下次重开时按新层重建
        SaveProgress();                       // 层数已变，立刻落盘
        StartBattle();
    }

    private void OnCloseStage()
    {
        SaveProgress();
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

        SettingPanel.BindData(OnCloseSettings);
        UIManager.ShowPanel<SettingPanel>();
    }

    private void OnCloseSettings()
    {
        // 设置面板不暂停战斗，关闭时无需续跑（音量/主题都是即时生效，不涉及存档数据）
    }

    /// <summary>
    /// 关掉所有子界面。**转世**这类"把所有状态换掉"的操作前必须调 ——
    /// 面板里持有的是状态机引用，状态被换掉之后它们读到的全是过期数据。
    /// <para>
    /// 为什么现在才需要：2026-11-02 起打开子界面不再暂停战斗，大劫输掉触发的转世
    /// 可能正好发生在玩家看客栈/装备的时候。以前战斗停着就不会撞上。
    /// </para>
    /// `UIManager.ClosePanel` 对没打开的面板是安全空操作（查字典失败直接返回）。
    /// </summary>
    private void CloseAllSubPanels()
    {
        UIManager.ClosePanel<InnPanel>();
        UIManager.ClosePanel<SkillsPanel>();
        UIManager.ClosePanel<EquipPanel>();
        UIManager.ClosePanel<StagePanel>();
        UIManager.ClosePanel<TalentPanel>();
        UIManager.ClosePanel<GalleryPanel>();
        UIManager.ClosePanel<SettingPanel>();
        UIManager.ClosePanel<SavePanel>();

        // 客栈打开时会把入口按钮置灰防重复打开；面板被强关时回调不会触发，这里手动还原
        if (_TInnBtn != null)
        {
            _TInnBtn.Disabled = false;
        }
    }

    /// <summary>
    /// 恢复战斗节奏 —— **只有存档面板还在用它**（读档会整份替换状态机，必须先停）。
    /// 其余子界面 2026-11-02 起不再暂停战斗（用户："打开子界面时，战斗不要停止"），
    /// 所以它们的关闭回调只需要落盘，不需要续跑。
    /// </summary>
    private void ResumeBattle()
    {
        if (_engine != null && !_engine.IsOver)
        {
            _clock.StartBeat();
        }
        else if (_resultShown)
        {
            _clock.StartAdvance(BattleClock.AdvanceSec);
        }
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
        {
            if (_inDajie)
            {
                _TTitle.Text = "《轮回大侠》· 大劫上门（第 " + (_dajieWins + 1) + " 场）";
            }
            else
            {
                _TTitle.Text = "《轮回大侠》· 第 " + _run.Layer + " 层 " + _run.Wave + "/" + _run.WavesInLayer + " 波" + waveTag + mode;
            }
        }

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

        // 境界：名字 + 当前乘区（没灌表时 Realm.Current 为 null，这一格就空着，不显示假数据）
        string realmTag = "";
        RealmConfig realmCur = Realm.Current;
        if (realmCur != null)
        {
            realmTag = "境界 " + realmCur.Name + "×" + realmCur.Mult.ToString("0") + "　｜　";
        }
        // 道行：只有攒过才显示（开局显示"道行 0"会让新手以为有个空系统）
        if (_dao.TotalGained > 0)
        {
            realmTag += "道行 " + _dao.Amount.ToString("0.#") + "　｜　";
        }

        // 大劫倒计时（打大劫时不计时，所以显示"进行中"而不是继续倒数）
        if (_inDajie)
        {
            realmTag = "⚔ 大劫中　｜　" + realmTag;
        }
        else
        {
            double remain = DajieRule.Remain(_clock.DajieElapsed);
            realmTag = "大劫 " + ((int)(remain / 60)).ToString("00") + ":"
                     + ((int)(remain % 60)).ToString("00") + "（点此迎战）　｜　" + realmTag;
        }

        // 转世按钮：够门槛就明说能换多少道行，不够就还是"重开本世"
        RefreshNextButtonText();

        if (_TExp != null)
        {
            _TExp.Text = realmTag
                       + "最高 " + _run.BestLayer + " 层 · 已战 " + _run.Battles + " 场　｜　"
                       + "银两 " + BigNum.From(_silver.Amount).ToString(2) + "（本世累计 " + BigNum.From(_silver.TotalGained).ToString(2) + "）"
                       + "　修为累计 " + BigNum.From(_cult.TotalGained).ToString(2) + "　｜　" + lv;
        }
    }

    // ============ 事件订阅 ============
    private void HookEvents()
    {
        _engine.Bus.OnDamage += (a, t, amt, skill) =>
        {
            if (amt <= 0)
            {
                return;
            }
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
        if (!TryWorldAnchor(tgt, out Vector2 pos))
        {
            return;
        }

        bool isSkill = !string.IsNullOrEmpty(skillName);
        string sfx = isSkill ? "hit_heavy" : "hit_light";

        AudioManager.SfxPriority priority = AudioManager.SfxPriority.Hit;
        if (isSkill)
        {
            priority = AudioManager.SfxPriority.Crit;
        }

        AudioManager.PlaySFXAt(sfx, pos, priority);
    }

    private void PlayDeathSound(BattleUnit u)
    {
        if (!TryWorldAnchor(u, out Vector2 pos))
        {
            return;
        }
        AudioManager.PlaySFXAt("death", pos, AudioManager.SfxPriority.Death);
    }

    /// <summary>取单位的世界坐标（音频定位用）：先问侠客卡，再问召唤堆叠卡。</summary>
    private bool TryWorldAnchor(BattleUnit u, out Vector2 world)
    {
        if (_cards.TryGetWorldAnchor(u, out world))
        {
            return true;
        }
        if (_stacks.TryGetWorldAnchor(u, out world))
        {
            return true;
        }
        world = Vector2.Zero;
        return false;
    }

    /// <summary>
    /// 敌方单位被击杀 → 掉落（**规则在 DropRule**）+ 飘字（表现在这里）。
    /// 面板对"掉多少、掉不掉装备"不做任何判断，只把规则算出的结果翻成人话。
    /// </summary>
    private void OnUnitDied(BattleUnit u)
    {
        PlayDeathSound(u);   // 必须在移除锚点之前取位置

        // 召唤物死亡不留"尸体"：引擎已把它移出队伍，这里同步堆叠卡（减 1，减到 0 就撤卡）
        if (u.IsSummon)
        {
            _stacks.Sync(_engine, _areaX);
            return;
        }

        if (_engine?.B == null || !_engine.B.Contains(u))
        {
            return;   // 只结算敌方
        }

        // 四本账（修为 / 银两 / 材料 / 装备）一次性入账，返回的只是一份读数
        KillDrop drop = DropRule.GrantKill(_rule, _run.Layer);

        // 装备单独飘一条：带稀有度色和套装名，它是"惊喜"，不该混在资源流水里
        if (drop.Equip != null)
        {
            string setTag = "";
            if (drop.Equip.SetId > 0)
            {
                EquipSetConfig sc = SetById(drop.Equip.SetId);
                if (sc != null)
                {
                    setTag = "〔" + sc.Name + "〕";
                }
            }

            SpawnFloat(u, "得 " + setTag + drop.Equip.Name + "（" + EquipRules.RarityName(drop.Equip.Rarity) + "）",
                       UISignalColors.RarityOf(drop.Equip.Rarity));
        }

        SpawnFloat(u, "修为+" + drop.ExpPerHead.ToString("0.#") + " 银两+" + BigNum.From(drop.Silver).ToString(2)
                      + " " + MaterialCatalog.NameOf(drop.MatTier) + "+" + drop.MatCount,
                   new Color(1f, 0.92f, 0.45f));
        RefreshHeader();
    }

    private void OnSummoned(BattleUnit owner, BattleUnit unit)
    {
        _stacks.Sync(_engine, _areaX);   // 召唤物走堆叠卡：立刻同步一次，让飘字马上有锚点（否则要等下一个 tick）

        // 同步之后再取锚点：否则召唤物还没有堆叠卡，拿不到位置
        if (TryWorldAnchor(unit, out Vector2 pos))
        {
            AudioManager.PlaySFXAt("spawn", pos, AudioManager.SfxPriority.Summon);
        }
    }

    private void OnEnded(int r)
    {
        if (_resultShown)
        {
            return;
        }
        _resultShown = true;
        SyncClockPause();   // 结算展示期间不计大劫时间（避免"结算中突然被大劫打断"）

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

        // 大劫战：不推进图状态（不改层/波），直接走自己的结算
        if (_inDajie)
        {
            OnDajieEnded(win);
            return;
        }

        _run.Settle(_engine);   // 胜→层内进下一波 / 打穿收尾波→进层；负→退回上一层并转入挂机

        // 层内续战的血量提示（波间只回 10%，阵亡者不复生）—— 人头统计归规则层
        SurvivorCount left = BattleSettle.CountSurvivors(_engine);
        string hpTag = left.Alive + "/" + left.Total + " 人续战（回 10% 气血）";

        if (win)
        {
            // 打穿整层 → 额外材料奖励（收尾波获胜时结算一次）
            string matTag = "";
            string realmTag = "";
            if (_run.LastSettleClearedLayer)
            {
                // 清层额外材料：规则层发，面板只报数
                int bonus = DropRule.GrantClearBonus(_rule, layer);
                if (bonus > 0)
                {
                    matTag = " · " + MaterialCatalog.NameOf(MaterialCatalog.TierOf(layer)) + "+" + bonus;
                }

                // 境界突破：门槛是"已达最高层数"，所以只可能在清层时满足
                realmTag = TryAutoBreakRealm();

                // 全屏事件音：首领波用更重的音，普通清层用轻的（都是居中、不参与方位）
                if (finisher)
                {
                    AudioManager.PlayEventSFX("boss_down");
                }
                else AudioManager.PlayEventSFX("clear_layer");
            }

            if (_TResult != null)
            {
                _TResult.Text = _run.LastSettleClearedLayer
                    ? "🏆 破第 " + layer + " 层（" + totalWaves + " 波） · " + dropTag + upTag + matTag + realmTag
                    : "✔ 第 " + layer + " 层 " + wave + "/" + totalWaves + " 波（" + (finisher ? "首领" : "普通") + "）清 · " + hpTag;
                _TResult.AddThemeColorOverride("font_color", new Color(0.4f, 1f, 0.5f));
                _TResult.Visible = true;
            }
            _clock.StartAdvance(BattleClock.AdvanceSec);
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
            _clock.StartAdvance(BattleClock.AdvanceSec);        // 卡关不停机：退回上一层继续刷
        }
        RefreshHeader();
        SaveProgress();   // 每场结算都落一次：场次 = 放置游戏的最小有意义进度单位
    }

    // ============ 飘字锚点（编排：先问侠客卡，再问召唤堆叠卡） ============
    private bool TryAnchor(BattleUnit u, out Vector2 pos)
    {
        if (_cards.TryGetAnchor(u, out pos))
        {
            return true;
        }
        if (_stacks.TryGetAnchor(u, out pos))
        {
            return true;
        }
        pos = Vector2.Zero;
        return false;
    }

    private void SpawnFloat(BattleUnit tgt, string text, Color color)
    {
        if (!TryAnchor(tgt, out Vector2 p))
        {
            return;
        }
        _floats.Spawn(p, text, color);
    }

    // ============ 逐帧驱动 ============
    /// <summary>
    /// 大劫到点（时钟喊的，不是本类算的）。秒数累计与暂停判定都在 <see cref="BattleClock"/> 里：
    /// 面板只回答"现在能不能开打" —— 时刻表是机制，前提条件才是玩法。
    /// </summary>
    private void OnDajieDue()
    {
        if (!_built)
        {
            return;   // Init/读档还没跑完，状态机不全
        }
        if (_run == null || _run.Layer <= 0)
        {
            return;   // 层数没建立（新一世刚归零那一瞬），下一跳再试
        }

        StartDajie();
    }

    /// <summary>
    /// 暂停/恢复大劫计时：**打大劫期间与结算展示期间都不计**（用户："Boss 战斗不算入时间"）。
    /// 两个条件合起来判断，避免在四处状态变更点各写一份布尔运算。
    /// </summary>
    private void SyncClockPause()
    {
        _clock.SetDajiePaused(_inDajie || _resultShown);
    }

    /// <summary>
    /// 大劫上门：这一场从哪档选、对手怎么造、给什么奖励，全在 <see cref="DajieRule"/>；
    /// 面板只做"锁档 → 置状态 → 提示 → 开打"。
    /// </summary>
    /// 大劫上门：从**独立表**（DajieBossConfig）选档造一个 Boss。
    /// 属性就是表里的绝对值（用户 2026-11-01："Boss 写死绝对值"）—— 不随推图层成长，也不乘 k^wins。
    /// 只造一个单位：大劫是"仇家上门"，不是一伙人围剿。
    /// 表没灌入时回退到当前层收尾波（不让战斗因为缺表而崩）。
    /// </summary>
    private void StartDajie()
    {
        if (_inDajie || _run == null || _run.Layer <= 0)
        {
            return;
        }

        // 悬赏清单先定下来：这一档打赢给什么，开始时就锁死，赢的时候不用再查表 ——
        // 免得中途档位被改（或表热重载），发奖与结算栏对不上。
        _dajieStage = DajieFactory.Pick(_dajieWins);

        // ⚠️ 用**全池**（_skillPool）而不是小怪池（_enemyPool）：
        //   小怪池按 `EffectType <= 14` 过滤（EnemyFactory.BuildSkillPool），
        //   而大劫表的配招里有按盾加伤（26）、护盾保持率（19）这类高阶效果词 ——
        //   传小怪池会让这些卡被静默跳过，Boss 变成只会普攻的沙包。
        //   回退规则（表没灌入时用当前层收尾波 × PowerOf）也在规则层里。
        _dajieFoes = DajieRule.BuildFoes(_run.Layer, _dajieWins, _skillPool, BuildEnemyWave);
        if (_dajieFoes == null)
        {
            _clock.ResetDajie();   // 造不出对手 → 这一场跳过，计时重新攒
            return;
        }

        _inDajie = true;
        _clock.ResetDajie();
        SyncClockPause();   // 打大劫期间不计时（大劫本身就是"暂停的这段"）

        AudioManager.PlayEventSFX("boss_down");   // 上门要有动静

        // 顶部闪一行：玩家此刻可能正开着客栈 / 装备 / 天赋（2026-11-02 起子界面不停战斗），
        // 不给提示就会"莫名其妙输掉并转世"。横幅在 CanvasLayer(100) 上，任何面板都盖不住它。
        DajieBanner.Flash("⚔ 大劫上门（第 " + (_dajieWins + 1) + " 场）· 打开面板不会暂停战斗");

        // 选关面板如果正开着（大劫前打开的），立刻锁上换层 —— 换层会把这场强制战斗顶掉
        StagePanel.RefreshLockIfOpen(true);

        StartBattle();
    }

    /// <summary>
    /// 战斗节拍（时钟 0.1s 喊一次）。本类只做"推一步 + 刷新表现"，
    /// 没有任何时间判断 —— 什么时候该喊是 <see cref="BattleClock"/> 的事。
    /// </summary>
    private void OnBeat()
    {
        if (_galleryShotDir.Length > 0)
        {
            TickGalleryShot();
        }
        if (_battleShotDir.Length > 0)
        {
            TickBattleShot();
        }

        if (_engine == null)
        {
            return;
        }
        if (!_engine.IsOver)
        {
            _engine.Step(SimDt);
        }
        _cards.Refresh(_engine);
        _stacks.Sync(_engine, _areaX);
        _floats.Update(BattleClock.TickSec);
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
        {
            EnsureBackdrop();
        }

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
        {
            return;
        }

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
        {
            return;
        }

        if (_backdrop != null && GodotObject.IsInstanceValid(_backdrop))
        {
            return;
        }

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
        foreach (HeroConfig h in _roster)
        {
            if (h.Id == id)
            {
                return h.Name;
            }
        }
        return "#" + id;
    }
}
