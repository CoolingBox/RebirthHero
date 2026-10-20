// 文件：FloatLayer.cs
// 《轮回大侠》表现层 —— **战斗飘字**组件（Godot 节点：Control 子类，挂在 BattlePanel.tscn 的 TFxLayer 上）
// 职责：在本层内生成 / 上浮 / 淡出 伤害·治疗·护盾·掉落 文字；自持飘字列表与寿命。
// 解耦边界：只接收**已解析好的锚点坐标**，不认识侠客卡 / 召唤物堆叠卡（锚点由 BattlePanel 编排解析）。

using System.Collections.Generic;
using Godot;

public partial class FloatLayer : Control
{
    private const int MaxFloats = 80;
    private const float Lifetime = 0.7f;

    private sealed class Item
    {
        public Label Lbl;
        public Vector2 Pos;
        public float Age;
    }

    private readonly List<Item> _items = new();

    public void Clear()
    {
        foreach (var f in _items) f.Lbl.QueueFree();
        _items.Clear();
    }

    /// <summary>在指定锚点生成一条飘字。</summary>
    public void Spawn(Vector2 pos, string text, Color color)
    {
        if (_items.Count >= MaxFloats) { var old = _items[0]; old.Lbl.QueueFree(); _items.RemoveAt(0); }
        var lbl = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        lbl.AddThemeFontSizeOverride("font_size", 21);
        lbl.AddThemeColorOverride("font_color", color);
        lbl.Position = pos;
        AddChild(lbl);
        _items.Add(new Item { Lbl = lbl, Pos = pos });
    }

    /// <summary>逐帧上浮 + 淡出；dt = 本次推进的秒数（由面板 Tick 驱动）。</summary>
    public void Update(float dt)
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var f = _items[i];
            f.Pos.Y -= 40f * dt;
            f.Age += dt;
            f.Lbl.Position = f.Pos;
            f.Lbl.Modulate = new Color(1, 1, 1, Mathf.Clamp(1f - f.Age / Lifetime, 0f, 1f));
            if (f.Age >= Lifetime) { f.Lbl.QueueFree(); _items.RemoveAt(i); }
        }
    }
}
