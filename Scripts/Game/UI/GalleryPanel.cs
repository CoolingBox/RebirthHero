// 文件：GalleryPanel.cs
// 《轮回大侠》卡册 —— 4 层视差卡面预览（独立窗口，BasePanel 子类，由 UIManager 开关）
//
// 职责：左边一张大卡（鼠标移动 = 倾斜 = 4 层视差），右边 4 列缩略图切换。
//
// 为什么现在按"素材目录"组织，而不是按侠客：
//   这批图是**美术试验素材**（各模型的武侠娘 + 三兽），还没和 20 名侠客一一对应。
//   强行按侠客排会配出"老乞丐是粉发少女"这种读不懂的卡。
//   等立绘定稿，把目录改成 `hero_01 … hero_20`、把下面 Entries 的名字换成侠客名即可，
//   **代码零改动** —— 卡面只认目录名，不认身份。
//
// 读档/存档：卡册是只读的，不碰任何状态，所以不需要存档，也不暂停战斗。

using System;
using System.Collections.Generic;
using Godot;
using TinyFramework;

public partial class GalleryPanel : BasePanel
{
    static Action _ctxOnClose;

    public static void BindData(Action onClose)
    {
        _ctxOnClose = onClose;
    }

    /// <summary>条目表：key（= Resources/UI/Gallery 下的目录名） + 显示名。</summary>
    static readonly string[][] Entries =
    {
        new string[] { "claude_n", "Claude · 娘" },
        new string[] { "claude_q", "Claude · Q" },
        new string[] { "deepseek_n", "DeepSeek · 娘" },
        new string[] { "deepseek_q", "DeepSeek · Q" },
        new string[] { "gemini_n", "Gemini · 娘" },
        new string[] { "gemini_q", "Gemini · Q" },
        new string[] { "glm_n", "GLM · 娘" },
        new string[] { "glm_q", "GLM · Q" },
        new string[] { "gpt_n", "GPT · 娘" },
        new string[] { "gpt_q", "GPT · Q" },
        new string[] { "grok_n", "Grok · 娘" },
        new string[] { "grok_q", "Grok · Q" },
        new string[] { "kimi_n", "Kimi · 娘" },
        new string[] { "kimi_q", "Kimi · Q" },
        new string[] { "qwen_n", "Qwen · 娘" },
        new string[] { "qwen_q", "Qwen · Q" },
        new string[] { "beast_feidudu", "三兽 · 肥嘟嘟" },
        new string[] { "beast_naiwa", "三兽 · 奶蛙" },
        new string[] { "beast_niulai", "三兽 · 牛来" },
    };

    GalleryCard _card;

    static GalleryPanel _current;

    readonly List<TextureButton> _thumbs = new List<TextureButton>();
    int _index;
    bool _built;
    bool _wired;

    /// <summary>
    /// 把当前打开的卡册定格在某个倾斜角（use = false 恢复跟随鼠标）。
    /// 静态入口是因为面板由 UIManager 用类型开关，调用方（BattlePanel）拿不到实例。
    /// </summary>
    public static void SetForcedTilt(bool use, Vector2 tilt)
    {
        if (_current == null || _current._card == null)
        {
            return;
        }
        _current._card.UseForcedTilt = use;
        _current._card.ForcedTilt = tilt;
    }

    public override void OnClose()
    {
        _current = null;
        base.OnClose();
    }

    public override void OnShow()
    {
        base.OnShow();
        _current = this;

        // .tscn 里挂的是默认主题（供编辑器预览），运行时必须换成玩家当前选的那套
        UIThemeManager.ApplyTo(GameObject);

        if (!_wired)
        {
            Wire();
            _wired = true;
        }
        if (!_built)
        {
            Build();
            _built = true;
        }

        Select(0);
    }

    void Wire()
    {
        if (_TCloseBtn != null)
        {
            _TCloseBtn.Pressed += OnClosePressed;
        }
    }

    void OnClosePressed()
    {
        AudioManager.PlayUISFX("ui_close");
        UIManager.ClosePanel<GalleryPanel>();
        if (_ctxOnClose != null)
        {
            _ctxOnClose();
        }
    }

    void Build()
    {
        if (_TCardHolder != null)
        {
            _card = new GalleryCard();
            _card.Size = _TCardHolder.Size;
            _card.Position = Vector2.Zero;
            _TCardHolder.AddChild(_card);
        }

        if (_TGrid != null)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                TextureButton b = new TextureButton();
                b.IgnoreTextureSize = true;
                b.StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered;
                b.CustomMinimumSize = new Vector2(96f, 124f);
                b.TooltipText = Entries[i][1];

                // 缩略图优先用 128px 的 thumb.png（19 张合计 0.66MB）；
                // 没有才退回 L2 全尺寸（896×1152，单张 5MB —— 以前 19 张全载 ≈100MB 就为了看一排小图）。
                string p = "res://Resources/UI/Gallery/" + Entries[i][0] + "/thumb.png";
                if (!ResourceLoader.Exists(p))
                {
                    p = "res://Resources/UI/Gallery/" + Entries[i][0] + "/L2.png";
                }
                if (ResourceLoader.Exists(p))
                {
                    b.TextureNormal = GD.Load<Texture2D>(p);
                }

                int idx = i;
                b.Pressed += () => Select(idx);
                _TGrid.AddChild(b);
                _thumbs.Add(b);
            }
        }

        if (_THint != null)
        {
            _THint.Text = "鼠标移到卡上 = 倾斜（4 层视差）　·　点右侧缩略图切换　·　"
                          + "「三兽」只有 3 层（没有卡框），所以会是 3 层视差";
        }
    }

    void Select(int i)
    {
        if (Entries.Length == 0)
        {
            return;
        }

        _index = ((i % Entries.Length) + Entries.Length) % Entries.Length;
        string key = Entries[_index][0];
        string name = Entries[_index][1];

        if (_card != null)
        {
            _card.SetEntry(key);
        }

        if (_TTitle != null)
        {
            _TTitle.Text = "卡册 · " + name;
        }

        if (_TInfo != null && _card != null)
        {
            Vector2 tex = _card.TextureSizeOf(0);
            string sizeTag = tex == Vector2.Zero
                ? "素材缺失"
                : "素材 " + (int)tex.X + "×" + (int)tex.Y;
            _TInfo.Text = name + "　·　" + _card.LayerCount() + " 层　·　" + sizeTag
                          + "　·　第 " + (_index + 1) + " / " + Entries.Length + " 张";
        }

        for (int n = 0; n < _thumbs.Count; n++)
        {
            if (n == _index)
            {
                _thumbs[n].Modulate = new Color(1f, 1f, 1f, 1f);
            }
            else
            {
                // 只压一点亮度做区分：压太狠（0.5）会让未选中的缩略图像"空槽"，
                // 玩家会以为那些卡没加载出来。
                _thumbs[n].Modulate = new Color(0.74f, 0.76f, 0.82f, 1f);
            }
        }
    }

    // 键盘切换：**不做**。BasePanel 是普通类（不是 Node），没有 _UnhandledInput 可重写；
    // 要收键盘就得给 GameObject 抢焦点 + 走 GuiInput，会跟缩略图按钮的焦点互相打架。
    // 本项目的其它面板也都没有键盘导航，保持一致：点缩略图 + 「关闭」按钮就够了。
}
