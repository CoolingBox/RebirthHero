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
    private const float SimDt = 0.15f;   // 每个 tick 推进的模拟秒数（~1.8x 观赏速度）

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
        HookEvents();

        StatsPanel.BindBattle(_engine, _engine.A, _run.Layer);
        _view.BuildCards(_engine);

        _resultShown = false;
        SyncClockPause();     // 结算展示结束 → 大劫计时恢复（若这场就是大劫，则继续停着）
        _main.RefreshHeader();
        _clock.StartBeat();
        _view.RefreshCards(_engine);
        _view.SyncStacks(_engine);
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

        if (_engine == null)
        {
            return;
        }
        if (!_engine.IsOver)
        {
            _engine.Step(SimDt);
        }

        _view.RefreshCards(_engine);
        _view.SyncStacks(_engine);
        _view.UpdateFloats(BattleClock.TickSec);
        StatsPanel.RefreshCurrent();   // 面板隐藏时内部直接返回
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
        if (_engine != null && !_engine.IsOver)
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
            _view.ShowDamage(t, amt);
            _view.PlayHitSound(t, skill);
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
    }

    /// <summary>敌方单位被击杀 → 掉落（规则在 DropRule）+ 飘字（视图负责）。</summary>
    private void OnUnitDied(BattleUnit u)
    {
        _view.PlayDeathSound(u);   // 必须在移除锚点之前取位置

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
        double perHeadTotal = headCount > 0 ? _cult.BattleGained / headCount : 0;
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
            _view.PlayUiSound("ui_click");
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
        _view.PlayUiSound("ui_confirm");

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
