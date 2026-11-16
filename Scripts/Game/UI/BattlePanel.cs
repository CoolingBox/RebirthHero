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
        _fx.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        int idx = GameObject.GetChildCount() - 1;
        if (_TFxLayer != null)
        {
            idx = _TFxLayer.GetIndex();
        }
        GameObject.MoveChild(_fx, idx);
    }

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
    /// </summary>
    public void PlayHitFx(BattleUnit att, BattleUnit tgt, DamageKind kind, Category cat, double amt)
    {
        if (_fx == null || tgt == null)
        {
            return;
        }

        Vector2 tp;
        if (!TryFxAnchor(tgt, out tp))
        {
            return;   // 目标没有锚点（已清场）就不画，别在屏幕角落放野特效
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

        // 真伤额外叠一道裂：相性管"用什么打"，真伤管"无视防御"，是两个独立维度
        if (kind == DamageKind.TrueDamage)
        {
            _fx.Play(BattleFxLayer.FxKind.Crack, tp, tp, FxTrue, 0.20f, power);
        }
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
    /// 命中音：普攻轻、武学重。
    /// 只做"轻重两档"，不按伤害数值分档 —— 引擎没有暴击概念，
    /// 凭数值猜档位会让同一件武器忽轻忽重，反而破坏空间一致性。
    /// </summary>
    public void PlayHitSound(BattleUnit tgt, string skillName)
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

    public void PlayUiSound(string key)
    {
        AudioManager.PlayUISFX(key);
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
