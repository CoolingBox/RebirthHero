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
    }

    // ============ 语义化表现 ============
    private static readonly Color DamageColor = new Color(1f, 0.4f, 0.3f);
    private static readonly Color HealColor = new Color(0.4f, 1f, 0.5f);
    private static readonly Color ShieldColor = new Color(0.4f, 0.9f, 1f);
    private static readonly Color GainColor = new Color(1f, 0.92f, 0.45f);
    private static readonly Color WinColor = new Color(0.4f, 1f, 0.5f);
    private static readonly Color LoseColor = new Color(1f, 0.45f, 0.35f);
    private static readonly Color RewardColor = new Color(1f, 0.85f, 0.35f);

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
        SpawnFloat(tgt, "修为+" + drop.ExpPerHead.ToString("0.#")
                      + " 银两+" + BigNum.From(drop.Silver).ToString(2)
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
