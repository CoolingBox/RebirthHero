// 文件：FxPixel.cs
// Super Pixel Effects Gigapack 生成的序列帧精灵表加载与缓存。
//
// 素材授权（见素材包 license.txt）：商用 OK、随游戏打包 OK，
// 需在鸣谢里标注 "Pixel Art Assets - Will Tice / unTied Games"。
// 禁止用作 AI 训练材料；本项目仅裁切拼接后直接使用。
//
// 精灵表规格（.codebuddy/tools/GenFxPixel.py 生成）：
//   高 256、宽 = 帧数 × 256，所以 frames = width / 256，不需要额外元数据文件。
//   每种相性两张：fxp_<cat>_bolt.png（投射物）/ fxp_<cat>_impact.png（命中）。
//   投射物另有一份 _r 镜像：敌方从右往左飞时尾巴要朝右。

using System.Collections.Generic;
using Godot;

public static class FxPixel
{
    // 与 GenFxPixel.py 的 FRAME 保持一致。
    // 2026-11-17 从 128 提到 256：斩击类换成 slash*/png 高清版（内容 236~456px），
    // 128 格会把它们压掉一半以上像素；256 格能基本保留原像素密度。
    // 改了这里必须重新生成贴图，否则 frames = width/FrameSize 会算错。
    public const int FrameSize = 256;

    const string Dir = "res://Resources/UI/FxPixel/";

    static readonly Dictionary<string, Texture2D> _cache =
        new Dictionary<string, Texture2D>();

    public static Texture2D Bolt(Category cat)
    {
        return Get(KeyOf(cat), true, false);
    }

    /// <summary>
    /// flip = 敌方从右往左飞时取镜像版。
    /// 只有自带拖尾的贴图（见 <see cref="BoltHasTail"/>）才有镜像版，
    /// 其它相性回退到原图 —— 它们是对称的径向爆发，翻转没意义。
    /// </summary>
    public static Texture2D Bolt(Category cat, bool flip)
    {
        return Get(KeyOf(cat), true, flip);
    }

    public static Texture2D Impact(Category cat)
    {
        return Get(KeyOf(cat), false, false);
    }

    /// <summary>
    /// 该相性的投射物贴图**自带拖尾**，不需要代码再沿轨迹铺残影。
    ///
    /// 2026-11-17 起 13 相性全部换成自带尾的素材（能量弹 / 火球 / 彗星 / 水鞭 /
    /// 冰锥 / 锯轮…），所以现在恒为 true。保留这个查询而不是直接删掉调用，
    /// 是为了将来某个相性回退成「无尾的径向爆发」时只需改这里一行。
    /// </summary>
    public static bool BoltHasTail(Category cat)
    {
        return true;
    }

    static Texture2D Get(string key, bool bolt, bool flip)
    {
        string suffix = bolt ? "_bolt" : "_impact";
        if (flip)
        {
            suffix += "_r";
        }
        string file = "fxp_" + key + suffix + ".png";

        Texture2D tex;
        if (_cache.TryGetValue(file, out tex))
        {
            return tex;
        }

        tex = ResourceLoader.Load<Texture2D>(Dir + file, "");
        if (tex == null)
        {
            // 没有镜像版就回退原图；不要把 null 写进缓存，否则之后一直拿到空。
            if (flip)
            {
                return Get(key, bolt, false);
            }
            GD.PrintErr("[FxPixel] 精灵表缺失: " + file);
            return null;
        }

        _cache[file] = tex;
        return tex;
    }

    static string KeyOf(Category cat)
    {
        switch (cat)
        {
            case Category.Sword: return "sword";
            case Category.Saber: return "saber";
            case Category.Fist: return "fist";
            case Category.Palm: return "palm";
            case Category.Leg: return "leg";
            case Category.Whip: return "whip";
            case Category.Hidden: return "hidden";
            case Category.Lightness: return "lightness";
            case Category.Medicine: return "medicine";
            case Category.Poison: return "poison";
            case Category.Qimen: return "qimen";
            case Category.Zen: return "zen";
            case Category.Beast: return "beast";
        }
        return "sword";
    }
}
