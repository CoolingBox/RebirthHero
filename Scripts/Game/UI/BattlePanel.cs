// 文件：BattlePanel.cs
// 《轮回大侠》—— **战斗显示面板**（BasePanel 子类，自己的场景 `Resources/UI/BattlePanel.tscn`）
//
// 2026-11-05 三次切分后的三层结构：
//   · MainPanel    （BasePanel）全局状态 / 存档 / 子界面开关 / 导航条 / 顶部栏 / 主题
//   · BattlePanel  （本文件，BasePanel）**战斗区画面**：背景 / 卡牌层 / 飘字层 / 结算栏 / 两个按钮
//   · BattleManager（普通类）**战斗逻辑**：推进、事件、掉落、大劫、转世
//
// 本类只做两件事：
//   ① 把场景节点准备好（卡牌/堆叠/飘字三层 + 背景），并维护自己的视口尺寸；
//   ② 给 BattleManager 一组**语义化**的显示方法（ShowDamage / ShowResult / PlayHitSound…），
//      让逻辑层不必知道颜色、锚点、字号，也不必 `using Godot` 的绘图部分。
//
// 反过来它也不认识存档、导航、天赋 —— 按钮点了就把事件转给 Manager，自己不做判断。
//
// 层级：本面板是**全屏底层**（含背景），MainPanel 是**顶部 HUD**（导航条 + 顶栏）。
// GameRoot 先 Show 本面板、再 Show MainPanel，后者自然在上层。

using Godot;
using TinyFramework;

/// <summary>结算栏的语气（逻辑层只说"这是赢/输/奖励"，颜色由显示层决定）。</summary>
public enum BattleResultTone
{
    Win,
    Lose,
    Reward,
}

public partial class BattlePanel : BasePanel
{
    /// <summary>当前实例（MainPanel 用它把 BattleManager 接上来）。UIManager 不提供按类型取面板。</summary>
    public static BattlePanel Current { get; private set; }

    private BattleManager _mgr;
    private HeroCardLayer _cards;
    private SummonStackLayer _stacks;
    private FloatLayer _floats;
    private BattleFxLayer _fx;
    private BackdropLayer _backdrop;
    private Vector2 _area = new Vector2(1920f, 1080f);
    private bool _built;

    public HeroCardLayer Cards { get { return _cards; } }

    public SummonStackLayer Stacks { get { return _stacks; } }

    public FloatLayer Floats { get { return _floats; } }

    /// <summary>视口尺寸（卡牌布局与飘字锚点都按它算）。</summary>
    public Vector2 Area { get { return _area; } }

    // ============ 生命周期 ============
    public override void OnShow()
    {
        base.OnShow();
        Current = this;

        if (!_built)
        {
            BuildView();
            _built = true;
        }
    }

    public override void OnHide()
    {
        base.OnHide();
    }

    /// <summary>由 MainPanel 在创建战斗逻辑后调用（它才持有状态机）。</summary>
    public void AttachManager(BattleManager mgr)
    {
        _mgr = mgr;
    }

    private void BuildView()
    {
        _area = GetArea();
        GameObject.Size = _area;
        GameObject.Position = Vector2.Zero;

        _cards = _TCards as HeroCardLayer;
        _stacks = _TStacks as SummonStackLayer;
        _floats = _TFxLayer as FloatLayer;

        EnsureFxLayer();

        if (_TPushBtn != null)
        {
            _TPushBtn.Pressed += OnPushPressed;
        }
        if (_TNextBtn != null)
        {
            _TNextBtn.Pressed += OnNextPressed;
        }

        EnsureBackdrop();
    }

    /// <summary>
    /// 特效层懒创建：插在卡牌层与飘字层之间。
    /// 层级必须是「卡 &lt; 特效 &lt; 飘字 &lt; 卡头横幅」—— 数字永远在特效上面，否则读不出来。
    /// </summary>
    private void EnsureFxLayer()
    {
        if (_fx != null && GodotObject.IsInstanceValid(_fx))
        {
            return;
        }

        _fx = new BattleFxLayer();
        _fx.Name = "TFx";
        GameObject.AddChild(_fx);
        int idx = GameObject.GetChildCount() - 1;
        if (_TFxLayer != null)
        {
            idx = _TFxLayer.GetIndex();
        }
        GameObject.MoveChild(_fx, idx);
    }

    // 层级完全靠场景树顺序，不设 ZIndex（2026-11-16 清理）：
    //   TBg → UnitsArea(TCards → TStacks) → TFx → TFxLayer → TResult → 按钮
    // 即「背景 < 卡 < 堆叠 < 特效 < 飘字 < 结算栏」。TFx 由 EnsureFxLayer 用
    // MoveChild 插到 TFxLayer 之前 —— 动这个顺序前先确认飘字仍在特效之上。
    // MainPanel 在 GameRoot 里比 BattlePanel 后 ShowPanel，HUD 与弹窗自然在其上。

    // ============ 打击特效（P0：斩线 / 环 / 裂 / 火花 / 死亡碎片） ============

    /// <summary>特效锚点：优先卡面中心（人身上），召唤物退化成堆叠卡锚点。</summary>
    private bool TryFxAnchor(BattleUnit u, out Vector2 pos)
    {
        if (_cards != null && _cards.TryGetFxAnchor(u, out pos))
        {
            return true;
        }
        if (_stacks != null && _stacks.TryGetAnchor(u, out pos))
        {
            return true;
        }
        pos = Vector2.Zero;
        return false;
    }

    /// <summary>
    /// 命中演出（v0.2 三段式）：起手一闪 → 相性投射物飞过去 → 目标处命中形。
    /// 命中形由投射物消亡时自动带出（见 BattleFxLayer._Process），这里不再手动放斩线/火花。
    ///
    /// 返回**投射物飞行真实秒数**，调用方拿它决定伤害飘字什么时候出
    /// （见 <see cref="ShowDamageAfter"/>）。没画特效时返回 0，飘字立刻出。
    /// </summary>
    public float PlayHitFx(BattleUnit att, BattleUnit tgt, DamageKind kind, Category cat, double amt)
    {
        if (_fx == null || tgt == null)
        {
            return 0f;
        }

        Vector2 tp;
        if (!TryFxAnchor(tgt, out tp))
        {
            return 0f;   // 目标没有锚点（已清场）就不画，别在屏幕角落放野特效
        }

        Vector2 ap;
        if (!TryFxAnchor(att, out ap))
        {
            // 攻击者是召唤物（只有堆叠卡）或已阵亡：给一个"从旁边打过来"的近似起点
            ap = tp + new Vector2(tp.X < _area.X * 0.5f ? 70f : -70f, -6f);
        }

        float power = 0.25f;
        if (tgt.BaseHp > 0)
        {
            power = (float)Mathf.Clamp((float)(amt / tgt.BaseHp), 0.05f, 1f);
        }

        // 起：先让释放者亮一下，"谁出手了"这件事要能被看见
        _fx.PlayCast(cat, ap);

        // 飞：投射物全距离飞过去。它是小弹体 + 短拖尾，不是 v0.1 否决的通栏长线
        _fx.PlayBolt(cat, ap, tp, power);

        // 出手破空声：与弹同时起步
        PlayWhoosh(ap, tp);

        // 飞行时长交给调用方去延迟飘字：数字必须等弹到了再出
        float fly = BattleFxLayer.BoltFlightTime(cat, ap, tp);

        // 微对撞延后到「弹快到时」再开始（2026-11-17 方案 2）：
        // 前冲 0.02 + 停顿 0.03 正好压在命中帧上。
        // 之前在攻击瞬间就撞，卡 0.16s 撞完回来还要干等 0.3~0.65s 弹才到，
        // 中间空一段，读起来像「卡挥空了一下，伤害随后才到」。
        if (_cards != null)
        {
            float lungeDelay = fly - BattleParallaxCard.HitLeadSec;
            if (lungeDelay > 0.03f)
            {
                ScheduleLunge(att, tgt, lungeDelay);
            }
            else
            {
                _cards.PlayAttackLunge(att, tgt);
            }
        }

        // 真伤额外叠一道裂：相性管"用什么打"，真伤管"无视防御"，是两个独立维度
        if (kind == DamageKind.TrueDamage)
        {
            _fx.Play(BattleFxLayer.FxKind.Crack, tp, tp, FxTrue, 0.20f, power);
        }

        return fly;
    }

    /// <summary>
    /// 延迟触发卡牌前冲。用真实时间 timer（ignoreTimeScale），与飘字/命中特效同一套时钟，
    /// 三者不会各走各的 —— 战斗加速时也不会出现「卡先撞、弹后到」。
    /// </summary>
    void ScheduleLunge(BattleUnit att, BattleUnit tgt, float delay)
    {
        SceneTreeTimer timer = GameObject.GetTree().CreateTimer(delay, true, false, true);
        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(GameObject) || _cards == null)
            {
                return;
            }
            _cards.PlayAttackLunge(att, tgt);
        };
    }

    /// <summary>死亡碎片。友方偏青灰、敌方偏暗红 —— 一眼看出倒的是哪边。</summary>
    public void PlayDeathFx(BattleUnit u, bool ally)
    {
        if (_fx == null || u == null)
        {
            return;
        }

        Vector2 p;
        if (!TryAnchor(u, out p))
        {
            return;
        }

        Color c = ally ? new Color(0.62f, 0.72f, 0.80f) : new Color(0.85f, 0.35f, 0.30f);
        _fx.Play(BattleFxLayer.FxKind.Death, p, p, c, 0.45f, 0.6f);
    }

    /// <summary>
    /// 闪避：一条从身侧掠过的空挥弧 + 淡影，**不叠火花** —— 没打中就不该炸。
    /// 颜色刻意取冷灰白而不是门派色：它表达的是"什么都没发生"，不该比打中还抢眼。
    /// </summary>
    public void PlayDodgeFx(BattleUnit tgt)
    {
        if (_fx == null || tgt == null)
        {
            return;
        }

        Vector2 p;
        if (!TryFxAnchor(tgt, out p))
        {
            return;
        }

        _fx.Play(BattleFxLayer.FxKind.Miss, p, p, FxDodge, 0.22f, 0.5f);
    }

    /// <summary>
    /// 持续伤害每跳：毒 = 上浮的泡（慢），火 = 上窜的火星（快）。
    /// 两者都不画斩线 —— DoT 没有"出手方向"，画线会让人以为有人打了他一下。
    /// </summary>
    public void PlayDotFx(BattleUnit tgt, bool burn, double amt)
    {
        if (_fx == null || tgt == null)
        {
            return;
        }

        Vector2 p;
        if (!TryFxAnchor(tgt, out p))
        {
            return;
        }

        float power = 0.25f;
        if (tgt.BaseHp > 0)
        {
            power = (float)Mathf.Clamp((float)(amt / tgt.BaseHp), 0.05f, 1f);
        }

        if (burn)
        {
            _fx.Play(BattleFxLayer.FxKind.Ember, p, p, FxBurn, 0.30f, power);
        }
        else
        {
            _fx.Play(BattleFxLayer.FxKind.Bubble, p, p, FxPoison, 0.55f, power);
        }
    }

    // 外功 = 暖金（刀风带火星）｜内功 = 青白（气浪）｜真伤 = 紫（破空）
    static readonly Color FxExternal = new Color(1f, 0.78f, 0.42f);
    static readonly Color FxInternal = new Color(0.55f, 0.85f, 1f);
    static readonly Color FxTrue = new Color(0.82f, 0.60f, 1f);
    // 持续伤害各有自己的色相：毒青绿、火橙（与"外功暖金"拉开，别混成刀光）
    static readonly Color FxPoison = new Color(0.52f, 0.92f, 0.55f);
    static readonly Color FxBurn = new Color(1f, 0.52f, 0.24f);
    static readonly Color FxDodge = new Color(0.78f, 0.86f, 0.94f);

    private void OnPushPressed()
    {
        // 「继续推图」是一次真正的提交（开下一场）；「转世」的确认在 BattleManager 里播
        UiSfx.Click();
        if (_mgr != null)
        {
            _mgr.ResumePush();
        }
    }

    private void OnNextPressed()
    {
        if (_mgr != null)
        {
            _mgr.OnNextPressed();
        }
    }

    // ============ 卡牌 / 飘字（逻辑层只喊"刷一下"） ============
    public void BuildCards(BattleEngine engine)
    {
        if (engine == null || _cards == null)
        {
            return;
        }
        _cards.Build(engine.A, engine.B, _area.X);
    }

    public void RefreshCards(BattleEngine engine)
    {
        if (engine == null || _cards == null)
        {
            return;
        }
        _cards.Refresh(engine);
    }

    public void SyncStacks(BattleEngine engine)
    {
        if (engine == null || _stacks == null)
        {
            return;
        }
        _stacks.Sync(engine, _area.X);
    }

    /// <summary>
    /// 出手反馈：把武学名报到卡牌头部（逻辑层只说"谁放了什么、打了几段"，位置与样式归这里）。
    /// </summary>
    public void ShowCast(BattleUnit u, string skill, int hits)
    {
        if (_cards != null)
        {
            _cards.ShowCast(u, skill, hits);
        }
    }

    public void UpdateFloats(double dt)
    {
        if (_floats != null)
        {
            _floats.Update((float)dt);
        }
    }

    public void ClearAll()
    {
        if (_cards != null)
        {
            _cards.Clear();
        }
        if (_stacks != null)
        {
            _stacks.Clear();
        }
        if (_floats != null)
        {
            _floats.Clear();
        }
        if (_fx != null)
        {
            _fx.Clear();
        }
    }

    // ============ 语义化表现 ============
    private static readonly Color DamageColor = new Color(1f, 0.4f, 0.3f);
    private static readonly Color HealColor = new Color(0.4f, 1f, 0.5f);
    private static readonly Color ShieldColor = new Color(0.4f, 0.9f, 1f);
    private static readonly Color GainColor = new Color(1f, 0.92f, 0.45f);
    private static readonly Color WinColor = new Color(0.4f, 1f, 0.5f);
    private static readonly Color LoseColor = new Color(1f, 0.45f, 0.35f);
    private static readonly Color RewardColor = new Color(1f, 0.85f, 0.35f);
    // DoT 与闪避的飘字色（2026-11-14）：毒青绿、火橙、闪避冷灰白
    private static readonly Color PoisonColor = new Color(0.45f, 0.95f, 0.55f);
    private static readonly Color BurnColor = new Color(1f, 0.58f, 0.28f);
    private static readonly Color DodgeColor = new Color(0.80f, 0.88f, 0.96f);

    public void ShowDamage(BattleUnit tgt, double amt)
    {
        SpawnFloat(tgt, "-" + amt.ToString("0"), DamageColor);
    }

    /// <summary>
    /// 承伤飘字延后到投射物到达时再出（2026-11-17）。
    ///
    /// 之前是 OnDamage 当拍就出字、弹还在飞，读起来像「隔空扣血」——
    /// 数字比命中先到，玩家的因果感是反的。延迟量直接用投射物的飞行时长，
    /// 与命中特效同一时刻触发（两者都由 BoltDuration 决定，不会各走各的）。
    ///
    /// 单位被清场 / 节点已释放时不补字（<see cref="SpawnFloat"/> 找不到锚点会自己跳过），
    /// 不做「数字飘在空地上」的兜底 —— 那比丢一个数字更怪。
    /// </summary>
    public void ShowDamageAfter(BattleUnit tgt, double amt, float delaySec)
    {
        if (delaySec <= 0.03f)
        {
            ShowDamage(tgt, amt);
            return;
        }

        // ignoreTimeScale = true：战斗速度只该改变模拟推进，不该改变「弹飞多久」的观感
        // 注意面板本身不是 Node（BasePanel 持有 GameObject 节点），取树要经 GameObject。
        SceneTreeTimer timer = GameObject.GetTree().CreateTimer(delaySec, true, false, true);
        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(GameObject) || tgt == null)
            {
                return;
            }
            ShowDamage(tgt, amt);
        };
    }

    public void ShowHeal(BattleUnit tgt, double amt)
    {
        SpawnFloat(tgt, "+" + amt.ToString("0"), HealColor);
    }

    public void ShowShield(BattleUnit tgt, double amt)
    {
        SpawnFloat(tgt, "🛡 " + amt.ToString("0"), ShieldColor);
    }

    /// <summary>
    /// 闪避飘字。用"闪"而不是"闪避"，两字在 10Hz 刷新的战场上是糊的；
    /// 也要与伤害数字区分 —— 它是**零**伤害，不该看起来像一个很小的伤害。
    /// </summary>
    public void ShowDodge(BattleUnit tgt)
    {
        SpawnFloat(tgt, "闪", DodgeColor);
    }

    /// <summary>
    /// 毒 / 火每跳的飘字。刻意与"承伤"同形（"-N"）但换色：
    /// 玩家对"我在掉血"这件事的读法是一样的，区别只在"是谁弄的"。
    /// </summary>
    public void ShowDot(BattleUnit tgt, double amt, bool burn)
    {
        Color c = burn ? BurnColor : PoisonColor;
        SpawnFloat(tgt, "-" + amt.ToString("0"), c);
    }

    /// <summary>掉落一件装备：稀有度色 + 套装名，它是"惊喜"，不该混在资源流水里。</summary>
    public void ShowEquipGain(BattleUnit tgt, EquipConfig tpl, EquipSetConfig set)
    {
        string setTag = "";
        if (set != null)
        {
            setTag = "〔" + set.Name + "〕";
        }
        SpawnFloat(tgt, "得 " + setTag + tpl.Name + "（" + EquipRules.RarityName(tpl.Rarity) + "）",
                   UISignalColors.RarityOf(tpl.Rarity));
    }

    /// <summary>击杀的常规产出：修为 / 银两 / 材料合成一条。</summary>
    public void ShowKillGain(BattleUnit tgt, KillDrop drop)
    {
        SpawnFloat(tgt, "修为+" + drop.ExpPerHead.ToString(1)
                      + " 银两+" + drop.Silver.ToString(2)
                      + " " + MaterialCatalog.NameOf(drop.MatTier) + "+" + drop.MatCount,
                   GainColor);
    }

    // ============ 音效（方位 / 距离 / 优先级） ============
    /// <summary>
    /// 相性 → 命中音类别（2026-11-17）。六大类各 5 个变体，见 Resources/Audio/SFX/hit_*。
    ///
    /// 为什么按**分系**而不是按伤害：伤害 = 武学基础值 × 攻方属性 × (1 - 目标防御)，
    /// 同一招打不同敌人能差 5 倍。若音效跟着伤害走，玩家听到的是"这一招忽强忽弱"，
    /// 但变的是敌人不是招式；音效该回答"谁用什么打了我"，这个问题只有分系能回答。
    /// 伤害已由飘字数字与命中特效（power = 伤害/目标最大血量）两条通道承载。
    /// </summary>
    static string HitGroupOf(Category cat)
    {
        switch (cat)
        {
            case Category.Fist:
            case Category.Palm:
            case Category.Leg:
                return "punch";       // 肉击 + 低频体

            case Category.Sword:
            case Category.Saber:
            case Category.Whip:
                return "blade";       // 金属铮

            case Category.Hidden:
                return "thrown";      // 小金属，短促

            case Category.Lightness:
                return "wind";        // 轻通用，最脆

            case Category.Medicine:
            case Category.Poison:
            case Category.Qimen:
            case Category.Zen:
                return "internal";    // 钝击闷响（内功/法门）

            case Category.Beast:
                return "beast";       // 低频闷，像重物/兽击
        }
        return "wind";
    }

    /// <summary>
    /// 命中音：按相性选类别，再在 5 个变体里随机取一个。
    ///
    /// 多采样比调音高更有效 —— 人耳对 ±5% 的音高差不敏感，同一段采样连播三次
    /// 听起来就是"贴片"；而 5 个真实变体轮换能直接消掉重复感。
    /// 音高随机由 AudioManager 统一做（±10%），音效名只负责"用哪一个"。
    /// </summary>
    public void PlayHitSound(BattleUnit tgt, string skillName, Category cat)
    {
        if (!TryWorldAnchor(tgt, out Vector2 pos))
        {
            return;
        }

        bool isSkill = !string.IsNullOrEmpty(skillName);

        string group = HitGroupOf(cat);
        int variant = _sfxVariant.Next(5);
        string sfx = "hit_" + group + "_" + variant;

        AudioManager.SfxPriority priority = AudioManager.SfxPriority.Hit;
        if (isSkill)
        {
            priority = AudioManager.SfxPriority.Crit;
        }

        AudioManager.PlaySFXAt(sfx, pos, priority);
    }

    /// <summary>
    /// 变体随机用独立实例：不碰全局 RNG，战斗回放的种子不受音频影响
    /// （声音不该改变逻辑结果，否则录像无法复现）。
    /// </summary>
    readonly System.Random _sfxVariant = new System.Random();

    /// <summary>
    /// 延迟播放命中音（2026-11-17）：打击声必须在打击那一刻响。
    ///
    /// 之前挂在 OnDamage 当拍，比命中早 0.43~0.81s，听感像「挥空了一声，
    /// 过一会儿才莫名其妙掉血」。延迟量与飘字、命中特效共用飞行时长，三者同拍。
    /// </summary>
    public void PlayHitSoundAfter(BattleUnit tgt, string skillName, Category cat, float delaySec)
    {
        if (delaySec <= 0.03f)
        {
            PlayHitSound(tgt, skillName, cat);
            return;
        }

        SceneTreeTimer timer = GameObject.GetTree().CreateTimer(delaySec, true, false, true);
        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(GameObject))
            {
                return;
            }
            PlayHitSound(tgt, skillName, cat);
        };
    }

    /// <summary>
    /// 出手破空声（2026-11-17）。放在**发射瞬间**，不是命中处。
    ///
    /// 关键区别：Kenney 的 woosh 本身就是渐强型（起音能量 0.00）——它的"前摇"
    /// 正是气流由远及近的过程，放在动作开始时才对；若放到命中处播，
    /// 就变成"已经打完了才听到风声"，方向是反的。
    ///
    /// 音量已压到打击音的 1/3（导出时归一化到 0.32），优先级用 Ambient：
    /// 声部不够时先牺牲它，绝不抢命中音；同屏 2~5 条弹重叠也只是一层底噪。
    /// </summary>
    void PlayWhoosh(Vector2 from, Vector2 to)
    {
        Vector2 mid = (from + to) * 0.5f;
        int idx = _sfxVariant.Next(8);
        string sfx = "whoosh_" + idx;
        AudioManager.PlaySFXAt(sfx, mid, AudioManager.SfxPriority.Ambient);
    }

    public void PlayDeathSound(BattleUnit u)
    {
        if (!TryWorldAnchor(u, out Vector2 pos))
        {
            return;
        }
        AudioManager.PlaySFXAt("death", pos, AudioManager.SfxPriority.Death);
    }

    public void PlaySpawnSound(BattleUnit u)
    {
        if (!TryWorldAnchor(u, out Vector2 pos))
        {
            return;
        }
        AudioManager.PlaySFXAt("spawn", pos, AudioManager.SfxPriority.Summon);
    }

    /// <summary>全屏事件音（清层 / 首领倒下），居中、不参与方位。</summary>
    public void PlayEventSound(string key)
    {
        AudioManager.PlayEventSFX(key);
    }

    // ============ 结算栏 ============
    public void ShowResult(string text, BattleResultTone tone)
    {
        if (_TResult == null)
        {
            return;
        }

        Color color = WinColor;
        if (tone == BattleResultTone.Lose)
        {
            color = LoseColor;
        }
        else if (tone == BattleResultTone.Reward)
        {
            color = RewardColor;
        }

        _TResult.Text = text;
        _TResult.AddThemeColorOverride("font_color", color);
        _TResult.Visible = true;
    }

    public void HideResult()
    {
        if (_TResult != null)
        {
            _TResult.Visible = false;
        }
        SetResultButtons(false, false);
    }

    public void SetResultButtons(bool push, bool next)
    {
        if (_TPushBtn != null)
        {
            _TPushBtn.Visible = push;
        }
        if (_TNextBtn != null)
        {
            _TNextBtn.Visible = next;
        }
    }

    /// <summary>转世按钮的文案（内容由 MainPanel 算，这里只管写与配色）。</summary>
    public void SetNextButtonText(string text, bool warn)
    {
        if (_TNextBtn == null)
        {
            return;
        }

        _TNextBtn.Text = text;
        if (warn)
        {
            _TNextBtn.AddThemeColorOverride("font_color", UISignalColors.Lose);
        }
        else
        {
            _TNextBtn.RemoveThemeColorOverride("font_color");
        }
    }

    // ============ 主题 / 背景 ============
    /// <summary>
    /// 换主题后重建表现层（卡牌底色写死在 ColorRect 上，只换 Theme 不会变）。
    /// 只重建节点、读现有 BattleUnit，不碰引擎状态与战斗进度。
    /// </summary>
    public void ApplyTheme(BattleEngine engine)
    {
        EnsureBackdrop();

        if (engine == null || _cards == null || _stacks == null)
        {
            return;
        }

        _cards.Clear();
        _cards.Build(engine.A, engine.B, _area.X);
        _cards.Refresh(engine);

        _stacks.Clear();
        _stacks.Sync(engine, _area.X);
    }

    /// <summary>
    /// 背景层懒创建：TBg 是纯色 Panel，作为底图未加载时的兜底；
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

    // ============ 锚点（飘字 / 音效定位） ============
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

    public bool TryWorldAnchor(BattleUnit u, out Vector2 world)
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

    private void SpawnFloat(BattleUnit tgt, string text, Color color)
    {
        if (!TryAnchor(tgt, out Vector2 p))
        {
            return;
        }
        _floats.Spawn(p, text, color);
    }

    /// <summary>视口截图（导出钩子用；逻辑层不碰 GameObject）。</summary>
    public void SaveViewportShot(string path)
    {
        Image img = GameObject.GetViewport().GetTexture().GetImage();
        img.SavePng(path);
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
}
