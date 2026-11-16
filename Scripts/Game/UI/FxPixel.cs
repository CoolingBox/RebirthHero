// 文件：FxPixel.cs
// Super Pixel Effects Gigapack 生成的序列帧精灵表加载与缓存。
//
// 素材授权（见素材包 license.txt）：商用 OK、随游戏打包 OK，
// 需在鸣谢里标注 "Pixel Art Assets - Will Tice / unTied Games"。
// 禁止用作 AI 训练材料；本项目仅裁切拼接后直接使用。
//
// 精灵表规格（.codebuddy/tools/GenFxPixel.py 生成）：
//   高 64、宽 = 帧数 × 64，所以 frames = width / 64，不需要额外元数据文件。
//   每种相性两张：fxp_<cat>_bolt.png（投射物）/ fxp_<cat>_impact.png（命中）。

using System.Collections.Generic;
using Godot;

public static class FxPixel
{
    // 与 GenFxPixel.py 的 FRAME 保持一致：large 变体等比居中放进 128px 帧格。
    public const int FrameSize = 128;

    const string Dir = "res://Resources/UI/FxPixel/";

    static readonly Dictionary<string, Texture2D> _cache =
        new Dictionary<string, Texture2D>();

    public static Texture2D Bolt(Category cat)
    {
        return Get(KeyOf(cat), true);
    }

    public static Texture2D Impact(Category cat)
    {
        return Get(KeyOf(cat), false);
    }

    static Texture2D Get(string key, bool bolt)
    {
        string file = "fxp_" + key + (bolt ? "_bolt.png" : "_impact.png");

        Texture2D tex;
        if (_cache.TryGetValue(file, out tex))
        {
            return tex;
        }

        tex = ResourceLoader.Load<Texture2D>(Dir + file, "");
        if (tex == null)
        {
            GD.PrintErr("[FxPixel] 精灵表缺失: " + file);
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
