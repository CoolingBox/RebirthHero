// 文件：StageRun.cs
// 《轮回大侠》M2.1 推图 —— **层 + 波** 推进状态机
// 职责边界：只维护"当前层 / 层内波次 / 胜负结算 / 推进-挂机模式 / 波间血量延续"，
//           不碰战斗数学、不碰表现。
//
// 推进规则（2026-10-15 用户口径）：
//   · "推图失败返回上一层重复刷图" —— 负/平 → **退回上一层**、波次归 1、转入挂机模式
//   · "起始要刷 5 波普通敌人，才会打层主" + "每 10 层加 1 波" —— 一个层 = N 波普通 + 1 波收尾首领
//   · "每波敌人间只恢复 10% 的血量" —— 层内续战时只把血量按 10% 最大气血回补
//   · "普通层的层主是精英" —— 收尾波在普通层是精英、层主层是层主（编成见 EnemyFactory）
//   · 阵亡处理：**保持阵亡**（口径）—— 层内不复活；跨层时整队重建，才恢复满编
//
// 血量延续的实现：引擎 SetTeams 会 Clone 队伍（每场重置运行时状态），**唯一被允许跨波带入的
//   是血量**（BattleUnit.StartHp）。本类在每波结束时把终局血量写回 `_carry` 并回补 10%，
//   下一波直接用同一批单位对象再次 SetTeams。

using System;
using System.Collections.Generic;

public sealed class StageRun
{
    readonly Func<List<BattleUnit>> _playerFactory;
    readonly Func<int, int, bool, List<BattleUnit>> _enemyFactory;
    int _seedCounter;

    /// <summary>层内续战用的玩家队（引擎 Clone 后不会改到它，故这里就是"下一波的出场阵容"）。</summary>
    List<BattleUnit> _carry;

    /// <summary>当前所在层（从 1 开始）。</summary>
    public int Layer { get; private set; } = 1;

    /// <summary>层内第几波（1 起，含收尾首领波）。</summary>
    public int Wave { get; private set; } = 1;

    /// <summary>本世最高层（= 转载/结算用）。</summary>
    public int BestLayer { get; private set; } = 1;

    /// <summary>最近一场结果：0 平局/超时，1 玩家胜，2 玩家负。</summary>
    public int LastWinner { get; private set; }

    /// <summary>最近一次结算是否**打穿了整层**（= 打的是收尾波且胜）。</summary>
    public bool LastSettleClearedLayer { get; private set; }

    /// <summary>总场次（含超时）。</summary>
    public int Battles { get; private set; }

    /// <summary>是否处于"自动推图"模式；false = **挂机模式**（停在本层重复刷）。</summary>
    public bool AutoPush { get; private set; } = true;

    int _idle;   // 挂机模式下已刷的场次（用于自动重试计数）

    /// <param name="playerFactory">每次返回一支**全新**的玩家队伍（只在"新的一层开始时"调用）。</param>
    /// <param name="enemyFactory">(层, 波序号, 是否收尾波) → 该波敌方编成。</param>
    public StageRun(Func<List<BattleUnit>> playerFactory,
                    Func<int, int, bool, List<BattleUnit>> enemyFactory,
                    int baseSeed = 20261015)
    {
        _playerFactory = playerFactory ?? throw new ArgumentNullException(nameof(playerFactory));
        _enemyFactory = enemyFactory ?? throw new ArgumentNullException(nameof(enemyFactory));
        _seedCounter = baseSeed;
    }

    // ---------- 派生状态 ----------

    public int MobWaves => StageConfig.MobWaves(Layer);
    public int WavesInLayer => StageConfig.WavesInLayer(Layer);
    /// <summary>当前波是否为收尾波（普通层 = 精英，层主层 = 层主）。</summary>
    public bool IsFinisherWave => Wave >= WavesInLayer;
    /// <summary>本层收尾波是否为**层主**（每 10 层）。</summary>
    public bool IsBossLayer => EnemyFactory.IsBossLayer(Layer);

    /// <summary>开一场当前层当前波的战斗（层内续战会带上上一波的剩余血量）。</summary>
    public BattleEngine Begin()
    {
        _carry ??= _playerFactory();      // 层内第一波才重建队伍；后续波沿用同一批对象
        var eng = new BattleEngine();
        eng.SetTeams(_carry, _enemyFactory(Layer, Wave, IsFinisherWave), _seedCounter++);
        return eng;
    }

    /// <summary>
    /// 结算本场（需要传入引擎 —— 要从终局状态读取玩家队剩余血量）。
    /// 胜 → 层内进下一波（血量延续 + 回补 10%）；打穿收尾波 → 进下一层（整队重建）；
    /// 负/平 → 退回上一层、波次归 1、转入挂机模式。
    /// </summary>
    public void Settle(BattleEngine eng)
    {
        int winner = eng?.Result ?? 0;
        Battles++;
        LastWinner = winner;
        LastSettleClearedLayer = false;

        if (winner != 1)
        {
            // 失利：退回上一层，波次与队伍都归零，转入挂机
            _carry = null;
            if (Layer > 1) Layer--;
            Wave = 1;
            AutoPush = false;
            _idle = 0;
            return;
        }

        if (!IsFinisherWave)
        {
            // 层内续战：写回剩余血量 + 回补 10% 最大气血（阵亡者保持阵亡）
            CapturePlayerState(eng);
            Wave++;
        }
        else
        {
            // 打穿整层
            LastSettleClearedLayer = true;
            _carry = null;                 // 跨层 → 整队重建（满血、阵亡者归队）
            Wave = 1;
            if (AutoPush)
            {
                Layer++;
                if (Layer > BestLayer) BestLayer = Layer;
            }
            // 挂机模式下打完一层 → 停在原层，回到第 1 波重刷（"退回上一层重复刷图"）
        }

        if (!AutoPush)
        {
            // 挂机：每 RetryEvery 场（波）自动去试上面那层一次（放置手感，离线也能推进）
            _idle++;
            if (StageConfig.RetryEvery > 0 && _idle >= StageConfig.RetryEvery)
            {
                _idle = 0;
                AutoPush = true;
            }
        }
    }

    /// <summary>
    /// 把本波终局血量写回 <see cref="_carry"/>：存活者按**最大气血的
    /// <see cref="StageConfig.WaveHealRatio"/>** 回补（上限满血）；阵亡者写 0（下一波不复活）。
    /// </summary>
    void CapturePlayerState(BattleEngine eng)
    {
        if (_carry == null || eng == null) return;

        var bySlot = new Dictionary<int, BattleUnit>();
        foreach (var c in eng.A)
            if (!c.IsSummon && c.Slot >= 1) bySlot[c.Slot] = c;   // 召唤物不延续

        foreach (var src in _carry)
        {
            if (!bySlot.TryGetValue(src.Slot, out var fin)) continue;
            // 下限夹 0：引擎已保证 Hp ≥ 0，这里再夹一次做防御（StartHp 的 −1 哨兵语义对负数敏感）
            double hp = Math.Max(0, fin.Hp);
            if (hp > 0) hp = Math.Min(src.BaseHp, hp + src.BaseHp * StageConfig.WaveHealRatio);
            src.Hp = hp;
            src.StartHp = hp;
        }
    }

    /// <summary>继续推图（结束挂机，从当前层往上冲）。</summary>
    public void ResumePush()
    {
        AutoPush = true;
        _idle = 0;
    }

    public void Reset()
    {
        Layer = 1;
        Wave = 1;
        BestLayer = 1;
        LastWinner = 0;
        LastSettleClearedLayer = false;
        Battles = 0;
        AutoPush = true;
        _idle = 0;
        _carry = null;
    }
}
