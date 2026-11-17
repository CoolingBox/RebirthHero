// 文件：BattleManager.cs
// 《轮回大侠》—— **一场战斗的逻辑**：引擎推进、事件结算、掉落发放、大劫、转世。
//
// 2026-11-05 二次切分（用户："BattlePanel 还是继承 BasePanel，新建 BattleManager，
// 将战斗逻辑和显示分离"）：
//
//   · MainPanel   （BasePanel）全局状态 + 存档 + 子界面开关 + 导航 + 顶部栏
//   · BattlePanel （BasePanel）**战斗显示**：卡牌层 / 飘字层 / 结算栏 / 按钮（本文件的 _view）
//   · BattleManager（本类，普通类）**战斗逻辑**：什么时候推一步、谁掉什么、什么时候结算
//
// 三条边界（评审时就按这三条看）：
//   ① 本类**不 `using Godot`** —— 不碰节点、不碰 Color、不算锚点。凡是"长什么样"，
//      一律调 `_view` 的语义方法（ShowDamage / ShowEquip / PlayHitSound…）。
//   ② 本类**不持有状态机** —— 修为/银两/进度/装配全在 MainPanel，`Bind` 时接同一批实例。
//   ③ 显示层与主面板**互不认识**：BattlePanel 不知道存档与导航，MainPanel 不知道卡牌怎么摆。
//
// 唯一例外是命中音/飘字的位置：那要问卡牌层。所以由 `_view` 内部解析锚点，本类只传"打谁"。

using System;
using System.Collections.Generic;
using TinyFramework;   // BigNum（大数格式化工具，纯数值，没有 Godot 依赖）

public sealed class BattleManager
{
    /// <summary>
    /// 每拍往前**播**多少模拟秒（1x 时）。节拍是真实 0.1s，原值 0.15 = 1.5 倍速观赏。
    /// 2026-11-15：按需求把原始观赏速度降为原来的 1/2，0.15 → 0.075（= 0.75 倍真实时间）。
    /// 行动条 / 事件 / 伤害数字都由播片按模拟时间 T 驱动（见 BattleReplay.Advance 的 T += simDt），
    /// 改这一项它们会自动同步，**不需要单独调行动条速度**。
    /// 只决定"片放多快"，不决定刷新频率，也不决定战斗结果（结果入场就定了）。
    /// ⚠️ 副作用：每场回放时长翻倍，推图/挂机节奏随之变慢。
    /// </summary>
    private const double SimDtPerBeat = 0.075;

    /// <summary>
    /// 结算这一场时喂给引擎的**固定步长**（2026-11-12，2026-11-13 起只在录影阶段用）。
    /// <para>
    /// 为什么必须有：`BattleEngine.Step(dt)` 是"把整个 dt 一次性吃掉"的 ——
    /// 蓄力条、DoT、护盾衰减都按 dt 一次算，行动点会被量化到步长边界。
    /// 若步长随倍速变，"一大步 0.45"和"九小步 0.05"会算出不同的胜负。
    /// </para>
    /// <para>
    /// 现在整场在入场时按 0.05 一步算完、录成帧；播片只贴帧，因此**结果与倍速、帧率都无关**。
    /// 代价是结算与无头探针 `Run()` 仍有细微口径差（探针按"到下一次行动"精确切片）。
    /// </para>
    /// </summary>
    private const double FixedStep = 0.05;

    private double _battleSpeed = 1.0;

    /// <summary>这一场的录像（入场时算完，见 BattleReplay.cs）。</summary>
    private BattleReplay _replay;

    /// <summary>播片游标：按模拟时间往前推，把帧贴回单位、把事件重新抛给订阅者。</summary>
    private BattlePlayback _playback;

    /// <summary>
    /// 战斗倍速。1 = 标称速度（每拍 0.15 模拟秒），2/3 = 快进。
    /// 只影响模拟推进，**不影响**节拍频率、UI 刷新、大劫倒计时、自动保存。
    /// 夹在 0.25~4 之间：低于 0.25 是慢放（调试用），高于 4 会让敌我行动挤在同一帧。
    /// </summary>
    public double BattleSpeed
    {
        get { return _battleSpeed; }
        set
        {
            if (value < 0.25)
            {
                value = 0.25;
            }
            if (value > 4.0)
            {
                value = 4.0;
            }
            _battleSpeed = value;
        }
    }

    private MainPanel _main;
    private BattlePanel _view;

    // —— 注入的状态机（与 MainPanel 持有的是**同一个对象**，不是拷贝）——
    private StageRun _run;
    private Cultivation _cult;
    private Silver _silver;
    private Random _rng;
    private List<SkillConfig> _skillPool;
    private BattleClock _clock;
    private BattleRuleContext _rule;

    private BattleEngine _engine;
    private bool _resultShown;

    // —— 本层战果统计（2026-11-12 从 StatsPanel 挪到这里）——
    // 两个读者共用同一份：战报面板 + 卡牌详情。放在逻辑层还有个好处 ——
    // 面板没被打开过也照样在统计（原来只有第一次打开战报才开始记，等于默认丢数据）。
    private readonly StatsTracker _stats = new StatsTracker();
    private int _statLayer = -1;

    /// <summary>武学名 → 伤害类型（特效按它选"线/环/裂"）。每场清一次，避免跨场残留。</summary>
    private readonly Dictionary<string, DamageKind> _skillKind = new Dictionary<string, DamageKind>();

    // 武学名 → 相性（特效 v0.2 按它选投射物形态 / 命中形 / 配色）。
    // 相性只在 SkillConfig 里（BattleSkill 没这字段），所以表现层自己建索引，不改引擎。
    // 配置是只读的，索引建一次即可，不必像 _skillKind 那样每场清。
    private readonly Dictionary<string, Category> _skillCat = new Dictionary<string, Category>();
    private bool _skillCatReady;

    // 单位名 → 主修派系。普攻没有武学名，用它选特效；按该单位武学相性众数推导，算一次缓存。
    private readonly Dictionary<string, Category> _unitCat = new Dictionary<string, Category>();

    public StatsTracker Stats { get { return _stats; } }

    // —— 大劫（战斗形态）。倒计时的秒数在 BattleClock，档位与胜负在这里 ——
    private bool _inDajie;
    private int _dajieWins;
    private List<BattleUnit> _dajieFoes;
    private DajieBossConfig _dajieStage;
    private bool _pendingRebirth;

    // —— 转世按钮的二次确认 ——
    private bool _nextArmed;

    /// <summary>注入宿主与视图。只接引用，不接所有权。</summary>
    public void Bind(MainPanel main, BattlePanel view)
    {
        _main = main;
        _view = view;

        _run = main.Run;
        _cult = main.Cult;
        _silver = main.Money;
        _rng = main.Rng;
        _skillPool = main.SkillPool;

        _clock = main.Clock;
        _rule = main.Rule;
    }

    // ============ 对外读数（MainPanel 顶部栏 / 存档要用） ============
    public BattleEngine Engine { get { return _engine; } }

    public bool HasEngine { get { return _engine != null; } }

    public bool NextArmed { get { return _nextArmed; } }

    /// <summary>当前是不是大劫战。</summary>
    public bool InDajie
    {
        get { return _inDajie; }
    }

    /// <summary>本世已打赢几场大劫（决定下一场档位，也用于顶部标题）。</summary>
    public int DajieWins
    {
        get { return _dajieWins; }
    }

    /// <summary>读档时灌回大劫进度。</summary>
    public void RestoreDajie(int wins)
    {
        _dajieWins = wins > 0 ? wins : 0;
    }

    // ============ 开战 ============
    public void StartBattle()
    {
        _view.ClearAll();
        _clock.StopAdvance();
        _view.HideResult();

        _cult.BeginBattle();      // 本场掉落计数清零
        _silver.BeginBattle();

        // 大劫战：走独立引擎（满血新队 vs 强化对手），**不动推图的层/波状态**
        if (_inDajie && _dajieFoes != null)
        {
            _engine = new BattleEngine();
            _engine.SetTeams(_main.BuildPlayerTeam(), _dajieFoes, _rng.Next());
        }
        else
        {
            _engine = _run.Begin();
        }

        // ① **算**：入场即用种子把整场跑完（无渲染）。之后前端只是播。
        BattleRecorder recorder = new BattleRecorder(_engine);
        recorder.Attach();
        _replay = recorder.Run(FixedStep);
        recorder.Detach();

        // ② 回到开场再建卡：录音结束时引擎停在残局，直接建卡会全是尸体
        _playback = new BattlePlayback(_replay, _engine);
        _playback.SeekToStart();

        HookEvents();

        ResetStatsForLayer();
        _skillKind.Clear();   // 换一场就清：不同敌人可能带同名但不同伤害类型的卡
        _stats.Attach(_engine);
        StatsPanel.BindBattle(_engine, _engine.A, _run.Layer, _stats);
        _view.BuildCards(_engine);

        _resultShown = false;
        SyncClockPause();     // 结算展示结束 → 大劫计时恢复（若这场就是大劫，则继续停着）
        _main.RefreshHeader();
        _clock.StartBeat();
        _view.RefreshCards(_engine);
        _view.SyncStacks(_engine);
    }

    /// <summary>
    /// 统计的清算节奏：**换层才清零**（层内多波累计，口径与战报面板原有注释一致）。
    /// 大劫战不走推图的层数，单独记一档 —— 否则打赢一场大劫会把当前层的数字一起清掉。
    /// </summary>
    private void ResetStatsForLayer()
    {
        int key = _inDajie ? -2 : _run.Layer;
        if (key == _statLayer)
        {
            return;
        }

        _stats.Reset();
        _statLayer = key;
    }

    /// <summary>
    /// 战斗节拍（时钟 0.1s 喊一次，由 MainPanel 转发）。逻辑只做"推一步 + 让视图刷新"。
    /// </summary>
    public void OnBeat()
    {
        if (_main.BattleShotWanted && _main.Elapsed >= 1.4)
        {
            _main.SaveBattleShot();
            return;
        }

        if (_engine == null || _playback == null)
        {
            return;
        }

        AdvancePlayback();

        _view.RefreshCards(_engine);
        _view.SyncStacks(_engine);
        _view.UpdateFloats(BattleClock.TickSec);
        StatsPanel.RefreshCurrent();   // 面板隐藏时内部直接返回
    }

    /// <summary>
    /// 播片推进：每拍把"该播多少模拟秒"喂给播放器（1x = 0.075 模拟秒/拍，
    /// 见 SimDtPerBeat；2026-11-15 由 0.15 减半而来）。
    /// <para>
    /// 这里**不跑任何规则** —— 整场在入场时就算完了（见 StartBattle 的 ①）。
    /// 播放器只做两件事：把当前帧的血量/行动条贴回单位，把到点的事件重新抛一遍（战报靠它长数字）。
    /// </para>
    /// </summary>
    private void AdvancePlayback()    {
        if (_playback.Done)
        {
            return;
        }

        _playback.Advance(SimDtPerBeat * _battleSpeed);
    }

    /// <summary>结算后开下一场（推图模式：胜则已进层 / 挂机模式：原地重复刷）。</summary>
    public void OnAdvance()
    {
        // 大劫输了：这一拍不推图，直接转世（发道行 + 归零 + 重开）
        if (_pendingRebirth)
        {
            _pendingRebirth = false;
            _dajieWins = 0;   // 新一世，大劫从第 1 档重新爬
            OnNext();         // 计时清零在 OnLifeReset 里一并做
            return;
        }
        StartBattle();
    }

    /// <summary>存档面板关闭后恢复战斗节奏 —— 只有它还在用（读档会整份替换状态机）。</summary>
    public void ResumeAfterPause()
    {
        // 注意判的是"播片有没有播完"，不是"引擎有没有结束"：
        // 引擎在入场时就跑完了（IsOver 恒为 true），拿它做条件会让播片永远停住。
        if (_playback != null && !_playback.Done)
        {
            _clock.StartBeat();
        }
        else if (_resultShown)
        {
            _clock.StartAdvance(BattleClock.AdvanceSec);
        }
    }

    /// <summary>「继续推图」：结束挂机，从当前层继续往上推。</summary>
    public void ResumePush()
    {
        _run.ResumePush();
        StartBattle();
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
            // 飘字与命中音都要等投射物飞到再出（2026-11-17）：
            // PlayHitFx 返回飞行时长，两者共用同一个延迟，与命中特效同拍。
            // 相性也一并传下去 —— 命中音按分系选类别（不按伤害，见 PlayHitSound 注释）。
            Category cat = CatOf(a, skill);
            float fly = _view.PlayHitFx(a, t, KindOf(a, skill), cat, amt);
            _view.ShowDamageAfter(t, amt, fly);
            _view.PlayHitSoundAfter(t, skill, cat, fly);
        };
        _engine.Bus.OnDot += (a, t, amt, burn) =>
        {
            if (amt <= 0)
            {
                return;
            }
            // 不播打击音：毒火是"背景伤害"，每秒每单位一跳，给了音效会变成雨声
            _view.ShowDot(t, amt, burn);
            _view.PlayDotFx(t, burn, amt);
        };
        _engine.Bus.OnDodge += (a, t, skill) =>
        {
            _view.ShowDodge(t);
            _view.PlayDodgeFx(t);
        };
        _engine.Bus.OnHeal += (a, t, amt, skill) =>
        {
            if (amt > 0)
            {
                _view.ShowHeal(t, amt);
            }
        };
        _engine.Bus.OnShield += (a, t, amt) =>
        {
            if (amt > 0)
            {
                _view.ShowShield(t, amt);
            }
        };
        _engine.Bus.OnSummon += OnSummoned;
        _engine.Bus.OnDeath += OnUnitDied;      // **击杀即时掉落**
        _engine.Bus.OnEnd += OnEnded;
        _engine.Bus.OnSkill += OnSkillFired;    // 抬手瞬间在卡牌头部报武学名
    }

    /// <summary>
    /// 武学打完一次（引擎在重放循环结束后才抛，所以 <paramref name="hits"/> 是这一手的实际段数）。
    /// 召唤物没有卡，交给 HeroCardLayer 时会静默忽略 —— 这里不做特殊判断。
    /// </summary>
    private void OnSkillFired(BattleUnit u, string name, int hits)
    {
        _view.ShowCast(u, name, hits);
    }

    /// <summary>
    /// 这次伤害算哪种（外功/内功/真伤）—— 特效靠它决定画"线 / 环 / 裂"。
    /// <para>
    /// 事件里只带武学名，所以在攻击者的武学表里按名字反查；查不到（普攻、吸血、DoT）一律按外功。
    /// 按名字缓存：同一张卡每次都是同一个类型，不必每帧线性查找。
    /// </para>
    /// </summary>
    private DamageKind KindOf(BattleUnit att, string skillName)
    {
        if (att == null || string.IsNullOrEmpty(skillName))
        {
            return DamageKind.External;
        }

        DamageKind cached;
        if (_skillKind.TryGetValue(skillName, out cached))
        {
            return cached;
        }

        List<BattleSkill> list = att.Skills;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Name == skillName)
            {
                _skillKind[skillName] = list[i].DmgKind;
                return list[i].DmgKind;
            }
        }

        _skillKind[skillName] = DamageKind.External;
        return DamageKind.External;
    }

    /// <summary>
    /// 武学相性（特效 v0.2）。
    /// 普攻没有武学名，取该单位首门武学的相性当"惯用兵器"——使刀的人普攻也该是刀芒。
    /// 实在查不到就按伤害类型退化，保证任何攻击都有确定的投射物形态。
    /// </summary>
    private Category CatOf(BattleUnit att, string skillName)
    {
        EnsureSkillCat();

        if (!string.IsNullOrEmpty(skillName))
        {
            Category cat;
            if (_skillCat.TryGetValue(skillName, out cat))
            {
                return cat;                 // 武学伤害：用这门武学自己的相性
            }
        }

        // 普攻 / 名字查不到：用该单位的主修派系，避免所有普攻挤在第一门心法上
        if (att != null && att.Skills.Count > 0)
        {
            return UnitCatOf(att);
        }

        if (KindOf(att, skillName) == DamageKind.Internal)
        {
            return Category.Palm;
        }
        return Category.Sword;
    }

    /// <summary>
    /// 单位主修派系：统计该单位所带武学的相性，取出现次数最多的（平票取先出现的，保证播片可复现）。
    /// 配置表里 300 门武学的 Category 全部有效，所以这里不需要猜名字语义。
    /// </summary>
    private Category UnitCatOf(BattleUnit u)
    {
        if (u == null)
        {
            return Category.Sword;
        }

        Category cached;
        if (_unitCat.TryGetValue(u.Name, out cached))
        {
            return cached;
        }

        Dictionary<Category, int> votes = new Dictionary<Category, int>();
        Category best = Category.Sword;
        int bestN = 0;

        List<BattleSkill> list = u.Skills;
        for (int i = 0; i < list.Count; i++)
        {
            Category cat;
            if (!_skillCat.TryGetValue(list[i].Name, out cat))
            {
                continue;
            }

            int n;
            if (!votes.TryGetValue(cat, out n))
            {
                n = 0;
            }
            n = n + 1;
            votes[cat] = n;

            if (n > bestN)
            {
                bestN = n;
                best = cat;
            }
        }

        _unitCat[u.Name] = best;
        return best;
    }

    /// <summary>建"武学名 → 相性"索引。配置只读，建一次就够。</summary>
    private void EnsureSkillCat()
    {
        if (_skillCatReady)
        {
            return;
        }
        _skillCatReady = true;

        List<SkillConfig> all = ConfigManager.GetAll<SkillConfig>();
        if (all == null)
        {
            return;
        }

        for (int i = 0; i < all.Count; i++)
        {
            SkillConfig sc = all[i];
            if (sc == null || string.IsNullOrEmpty(sc.Name))
            {
                continue;
            }
            _skillCat[sc.Name] = sc.Category;
        }

        // 启动一次的诊断：配表漏填相性时这里会立刻暴露，不用等到看特效才发现
        Console.WriteLine("[BattleManager] 武学相性索引 " + _skillCat.Count + " 条 / " + all.Count);
    }

    /// <summary>敌方单位被击杀 → 掉落（规则在 DropRule）+ 飘字（视图负责）。</summary>
    private void OnUnitDied(BattleUnit u)
    {
        _view.PlayDeathSound(u);   // 必须在移除锚点之前取位置
        _view.PlayDeathFx(u, _engine != null && _engine.InA(u));

        // 召唤物死亡不留"尸体"：引擎已把它移出队伍，这里同步堆叠卡（减 1，减到 0 就撤卡）
        if (u.IsSummon)
        {
            _view.SyncStacks(_engine);
            return;
        }

        if (_engine?.B == null || !_engine.B.Contains(u))
        {
            return;   // 只结算敌方
        }

        KillDrop drop = DropRule.GrantKill(_rule, _run.Layer);

        // 装备单独飘一条：带稀有度色与套装名，它是"惊喜"，不该混在资源流水里
        if (drop.Equip != null)
        {
            EquipSetConfig sc = null;
            if (drop.Equip.SetId > 0)
            {
                sc = _main.SetById(drop.Equip.SetId);
            }
            _view.ShowEquipGain(u, drop.Equip, sc);
        }

        _view.ShowKillGain(u, drop);
        _main.RefreshHeader();
    }

    private void OnSummoned(BattleUnit owner, BattleUnit unit)
    {
        _view.SyncStacks(_engine);   // 立刻同步，让飘字马上有锚点
        _view.PlaySpawnSound(unit);
    }

    private void OnEnded(int r)
    {
        if (_resultShown)
        {
            return;
        }
        _resultShown = true;
        SyncClockPause();   // 结算展示期间不计大劫时间

        int layer = _run.Layer;
        int wave = _run.Wave;
        int totalWaves = _run.WavesInLayer;
        bool finisher = _run.IsFinisherWave;
        bool win = r == 1;

        // 修为：击杀时已即时进池，这里**统一结算升级**（避免战斗中途改等级）
        List<Cultivation.LevelUp> ups = _cult.SettleLevelUps(_main.RosterIds());
        string upTag = "";
        if (ups.Count > 0)
        {
            var parts = new List<string>();
            foreach (Cultivation.LevelUp u in ups)
            {
                parts.Add(_main.HeroName(u.HeroId) + "↑L" + u.To);
            }
            upTag = "　" + string.Join(" ", parts);
        }

        int headCount = _main.RosterIds().Count;
        BigNum perHeadTotal = BigNum.Zero;
        if (headCount > 0)
        {
            perHeadTotal = _cult.BattleGained / headCount;
        }
        string dropTag = "修为 +" + perHeadTotal.ToString(1) + "/人 · 银两 +" + _silver.BattleGained.ToString(2);

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

                realmTag = TryAutoBreakRealm();

                // 全屏事件音：首领波用更重的音，普通清层用轻的（都是居中、不参与方位）
                if (finisher)
                {
                    _view.PlayEventSound("boss_down");
                }
                else
                {
                    _view.PlayEventSound("clear_layer");
                }
            }

            string text = _run.LastSettleClearedLayer
                ? "🏆 破第 " + layer + " 层（" + totalWaves + " 波） · " + dropTag + upTag + matTag + realmTag
                : "✔ 第 " + layer + " 层 " + wave + "/" + totalWaves + " 波（" + (finisher ? "首领" : "普通") + "）清 · " + hpTag;
            _view.ShowResult(text, BattleResultTone.Win);
            _clock.StartAdvance(BattleClock.AdvanceSec);
        }
        else
        {
            string why = r == 0 ? "超时未决" : "全员阵亡";
            string text = "⛔ 第 " + layer + " 层 " + wave + "/" + totalWaves + " 波失利（" + why + "）· " + dropTag + upTag
                        + " · 退回第 " + _run.Layer + " 层挂机刷…";
            _view.ShowResult(text, BattleResultTone.Lose);
            _view.SetResultButtons(true, true);   // 继续推图 / 重开本世
            _clock.StartAdvance(BattleClock.AdvanceSec);   // 卡关不停机：退回上一层继续刷
        }

        _main.RefreshHeader();
        _main.SaveProgress();   // 每场结算都落一次：场次 = 放置游戏的最小有意义进度单位
    }

    /// <summary>
    /// 境界突破（**自动**）：清层后检查双门槛（银两 + 已达层数），满足就进一境。
    /// 判定在 `BattleSettle.TryBreakRealm`，这里只播音 + 拼提示。
    /// </summary>
    private string TryAutoBreakRealm()
    {
        RealmConfig nx = BattleSettle.TryBreakRealm(_silver, _run.BestLayer);
        if (nx == null)
        {
            return "";
        }

        _view.PlayEventSound("clear_layer");
        return " · 境界→" + nx.Name + "（属性 ×" + nx.Mult.ToString("0") + "）";
    }

    // ============ 转世 / 重开本世 ============
    /// <summary>
    /// 转世按钮**两下确认**：会把本世资产全部清零，而按钮就贴着「继续推图」，
    /// 挂机玩家边刷边点，误触一次就是一世的进度。
    /// </summary>
    public void OnNextPressed()
    {
        if (!_nextArmed)
        {
            _nextArmed = true;
            UiSfx.Click();
            _main.RefreshNextButtonText();
            _clock.StartConfirm();
            return;
        }

        DisarmNext();
        OnNext();
    }

    /// <summary>解除"等确认"状态（超时 / 已确认 / 结算刷新时都走这里）。</summary>
    public void DisarmNext()
    {
        _clock.StopConfirm();
        if (!_nextArmed)
        {
            return;
        }

        _nextArmed = false;
        _main.RefreshNextButtonText();
    }

    /// <summary>
    /// 转世 / 重开本世。折算与归零的**规则**在 `RebirthRule`；
    /// 面板级收尾（关子界面 / 清统计 / 清大劫档位 / 落盘）在 `MainPanel.OnLifeReset`。
    /// </summary>
    private void OnNext()
    {
        // 大劫打输也会走到这里（OnAdvance → OnNext）：先清掉"等确认"，否则新一世按钮停在警示文案上
        DisarmNext();
        UiSfx.Confirm();

        // ① 发道行（够门槛才有；天赋「天机」的乘区在规则里乘）
        RebirthRule.GrantDao(_rule, _run.BestLayer);

        // ② 清零本世资产（层 / 修为 / 银两 / 材料 / 装备 / 境界）
        RebirthRule.ResetLife(_rule);

        // ③ 面板收尾：关子界面、清战报、清大劫档位与倒计时、落盘
        _inDajie = false;
        _dajieWins = 0;
        _dajieFoes = null;
        _main.OnLifeReset();
        StartBattle();
    }

    // ============ 大劫 ============
    /// <summary>
    /// 大劫上门：这一场从哪档选、对手怎么造、给什么奖励，全在 `DajieRule`；
    /// 这里只做"锁档 → 置状态 → 提示 → 开打"。
    /// </summary>
    public void StartDajie()
    {
        if (_inDajie || _run == null || _run.Layer <= 0)
        {
            return;
        }

        // 悬赏清单先定下来：这一档打赢给什么，开始时就锁死 —— 免得中途档位被改，发奖与文案对不上
        _dajieStage = DajieFactory.Pick(_dajieWins);

        // ⚠️ 用**全池**（_skillPool）而不是小怪池：小怪池按 EffectType ≤ 14 过滤，
        //    会让 Boss 的按盾加伤 / 护盾保持率被静默跳过（详见 DajieRule.BuildFoes）。
        _dajieFoes = DajieRule.BuildFoes(_run.Layer, _dajieWins, _skillPool, _main.BuildEnemyWave);
        if (_dajieFoes == null)
        {
            _clock.ResetDajie();   // 造不出对手 → 这一场跳过，计时重新攒
            return;
        }

        _inDajie = true;
        _clock.ResetDajie();
        SyncClockPause();   // 打大劫期间不计时

        _view.PlayEventSound("boss_down");   // 上门要有动静

        // 顶部闪一行：玩家此刻可能正开着客栈 / 装备 / 天赋，不给提示就会"莫名其妙输掉并转世"
        DajieBanner.Flash("⚔ 大劫上门（第 " + (_dajieWins + 1) + " 场）· 打开面板不会暂停战斗");

        // 选关面板如果正开着（大劫前打开的），立刻锁上换层 —— 换层会把这场强制战斗顶掉
        StagePanel.RefreshLockIfOpen(true);

        StartBattle();
    }

    /// <summary>
    /// 大劫结算。赢 → 记一次胜利、重新计时、回推图；输 → 转世。
    /// 输的时候先让结算栏亮 2 秒再重开 —— 玩家得知道自己是"被大劫打死的"。
    /// </summary>
    private void OnDajieEnded(bool win)
    {
        _inDajie = false;
        _dajieFoes = null;
        // 大劫结束，但结算栏还在展示（_resultShown 仍为 true）→ 计时继续停着
        SyncClockPause();

        StagePanel.RefreshLockIfOpen(false);

        if (win)
        {
            string rewardTag = GrantDajieReward();
            _dajieWins++;
            _clock.ResetDajie();

            DajieBanner.Flash("⚔ 大劫已过（第 " + _dajieWins + " 场）· 奖励已入账");

            string text = "⚔ 大劫已过（第 " + _dajieWins + " 场）" + rewardTag
                        + " · " + (DajieRule.Interval() / 60).ToString("0.#") + " 分钟后再来";
            _view.ShowResult(text, BattleResultTone.Reward);
            _main.RefreshHeader();
            _main.SaveProgress();
            _clock.StartAdvance(BattleClock.AdvanceSec);   // 回推图
            return;
        }

        _view.ShowResult("☠ 大劫不敌（第 " + (_dajieWins + 1) + " 场）· 转世重来", BattleResultTone.Lose);
        _pendingRebirth = true;
        _clock.StartAdvance(BattleClock.AdvanceSecLonger);
    }

    /// <summary>大劫悬赏的**文案**（发奖本身在 `DajieRule.Grant`）。</summary>
    private string GrantDajieReward()
    {
        int layer = _run != null ? _run.Layer : 1;
        DajieReward reward = DajieRule.Grant(_rule, _dajieStage, layer);
        if (!reward.Any)
        {
            return "";
        }

        var parts = new List<string>();

        if (reward.Dao > BigNum.Zero)
        {
            parts.Add("道行 +" + reward.Dao.ToString(1));
        }

        if (reward.ExpPerHead > BigNum.Zero)
        {
            parts.Add("修为 +" + reward.ExpPerHead.ToString(1) + "/人");
            parts.Add("银两 +" + reward.Silver.ToString(2));
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
            parts.Add(_main.HeroName(up.HeroId) + "↑L" + up.To);
        }

        if (parts.Count == 0)
        {
            return "";
        }
        return " · " + string.Join(" · ", parts);
    }

    /// <summary>
    /// 暂停/恢复大劫计时：**打大劫期间与结算展示期间都不计**（用户："Boss 战斗不算入时间"）。
    /// 两个条件合起来判断，避免在四处状态变更点各写一份布尔运算。
    /// </summary>
    private void SyncClockPause()
    {
        _clock.SetDajiePaused(_inDajie || _resultShown);
    }
}
