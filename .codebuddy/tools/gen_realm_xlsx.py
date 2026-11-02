"""境界表改名：仙侠体系（炼气/筑基/金丹…）→ 武侠体系（江湖名头）。

口径（用户 2026-11-01）："境界用武侠风的，不用仙侠风的"。
武侠里没有"炼气化神"，有的是**江湖上的名头**：初入江湖 → 三流 → 二流 → 一流 → 绝顶 →
宗师 → 神话 → 天下第一。这条链的好处是玩家一眼知道自己在江湖里排第几，不需要懂修真术语。

数值（乘区 ×10 / 银两门槛 / 层数门槛）不变，只改名与文案。
"""
import openpyxl
import os

PATH = r"D:\Godot\Projects\RebirthHero\addons\TinyTools\ConfigGenerator\RealmConfig.xlsx"

HEADERS = ["Id", "Name", "Mult", "ReqSilver", "ReqLayer", "Unlock", "Desc"]
TYPES = ["int", "string", "double", "double", "int", "string", "string"]
NOTES = [
    "境界ID(1~8)",
    "境界名（武侠名头，不用仙侠境界）",
    "境界乘区：玩家 HP/ATK/DEF 同时 ×本值（初入江湖=1）",
    "突破消耗银两（0=开局自带）",
    "突破需已达的最高层数",
    "解锁项（本期只做数值乘区，解锁待实装）",
    "设计备注",
]

ROWS = [
    [1, "初入江湖", 1, 0, 0, "—（开局即 3 槽）", "开局：刚下山，江湖上还没人认得你"],
    [2, "三流身手", 10, 3000, 15, "武学槽 +1（待实装）", "首次突破，玩家第一次感知数量级跳档"],
    [3, "二流好手", 100, 100000, 35, "客栈栏位 +1（待实装）", "对应层 35 附近，约 9 层银两"],
    [4, "一流高手", 1000, 5000000, 60, "装备栏位 +1（待实装）", "约 12 层银两"],
    [5, "绝顶高手", 10000, 500000000, 90, "召唤物上限 +1（待实装）", "约 14 层银两；江湖上已少有敌手"],
    [6, "一代宗师", 100000, 5e10, 125, "客栈免费刷新 +1（待实装）", "开宗立派，长线"],
    [7, "武林神话", 1000000, 5e12, 165, "武学槽 +1（待实装）", "长线"],
    [8, "天下第一", 10000000, 5e14, 210, "大劫预告 +50%（待实装）", "长线终点"],
]

wb = openpyxl.Workbook()
ws = wb.active
ws.title = "RealmConfig"

for c, h in enumerate(HEADERS, start=1):
    ws.cell(1, c).value = h
for c, t in enumerate(TYPES, start=1):
    ws.cell(2, c).value = t
for c, n in enumerate(NOTES, start=1):
    ws.cell(3, c).value = n

for r, row in enumerate(ROWS, start=5):
    for c, v in enumerate(row, start=1):
        ws.cell(r, c).value = v

os.makedirs(os.path.dirname(PATH), exist_ok=True)
wb.save(PATH)
print("written", PATH, len(ROWS), "rows")
