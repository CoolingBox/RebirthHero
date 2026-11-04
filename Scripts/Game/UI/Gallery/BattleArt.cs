// 文件：BattleArt.cs
// 《轮回大侠》战斗卡面的素材映射 —— 侠客名 → Resources/UI/Gallery 下的 4 层视差素材目录
//
// 为什么需要这层映射：
//   战斗卡只拿得到 BattleUnit（里面有 Name，没有 heroId），而素材目录是按"模型/风格"命名的
//   （claude_n / gpt_q …），两边没有任何天然对应关系。
//   这里用 HeroConfig 的 Id 顺序做**确定性占位**：第 n 名侠客 → 第 n % 16 组素材。
//
// 为什么是占位而不是"正式对应"：
//   这 16 组是美术试验素材（各模型的武侠娘），不是 20 名侠客的定稿立绘。
//   强行一一对应会配出"老乞丐是粉发少女"这种读不懂的卡 —— 但战斗界面总得先有画面，
//   所以先用确定性映射顶上。立绘定稿后，把 Keys 换成按侠客命名的目录即可，**其它代码零改动**。
//
// 为什么不放三兽（beast_*）：
//   三兽素材是 1806×2322，单层未压缩约 16.8MB、四层 67MB；战斗里最多 8 张卡同时在场，
//   混进三兽会把显存推到 200MB 以上。三兽只留在卡册里预览。
//   （若将来要进战斗，先把导入参数压到 1152 以内再改这里。）

using System.Collections.Generic;
using Godot;
using TinyFramework;

public static class BattleArt
{
    /// <summary>素材根目录（与 GalleryCard 同源，卡册与战斗卡共用同一批图）。</summary>
    public const string Root = "res://Resources/UI/Gallery/";

    /// <summary>可用于战斗卡面的素材目录（16 组人物），按固定顺序排列。</summary>
    static readonly string[] Keys =
    {
        "claude_n",
        "claude_q",
        "deepseek_n",
        "deepseek_q",
        "gemini_n",
        "gemini_q",
        "glm_n",
        "glm_q",
        "gpt_n",
        "gpt_q",
        "grok_n",
        "grok_q",
        "kimi_n",
        "kimi_q",
        "qwen_n",
        "qwen_q",
    };

    static readonly Dictionary<string, string> _byName = new Dictionary<string, string>();
    static bool _ready;

    /// <summary>
    /// 建一次"侠客名 → 素材目录"表。必须早于第一次建卡（BattlePanel.InitStatic 里调）。
    /// 重复调用是安全的：表会被重建，不会累积。
    /// </summary>
    public static void Init()
    {
        _byName.Clear();

        List<HeroConfig> heroes = ConfigManager.GetAll<HeroConfig>();
        if (heroes != null)
        {
            // 按 Id 排序：JSON 的顺序将来若被改动，映射也不该跟着变
            List<HeroConfig> sorted = new List<HeroConfig>(heroes);
            sorted.Sort(CompareById);

            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] == null || string.IsNullOrEmpty(sorted[i].Name))
                {
                    continue;
                }
                _byName[sorted[i].Name] = Keys[i % Keys.Length];
            }
        }

        _ready = true;
    }

    static int CompareById(HeroConfig a, HeroConfig b)
    {
        return a.Id.CompareTo(b.Id);
    }

    /// <summary>取某单位的卡面素材目录；表没建 / 名字没登记 → 按名字散列兜底（同一名字永远同一张卡）。</summary>
    public static string KeyOf(string name)
    {
        if (!_ready)
        {
            Init();
        }

        if (!string.IsNullOrEmpty(name))
        {
            string key;
            if (_byName.TryGetValue(name, out key))
            {
                return key;
            }
        }

        int h = 0;
        if (name != null)
        {
            for (int i = 0; i < name.Length; i++)
            {
                h = (h * 31 + name[i]) & 0x7FFFFFFF;
            }
        }
        return Keys[h % Keys.Length];
    }

    /// <summary>该目录是否有 L2（人物层）—— 没有就说明素材缺失，调用方可以退化成纯色卡。</summary>
    public static bool Exists(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }
        return ResourceLoader.Exists(Root + key + "/L2.png");
    }
}
