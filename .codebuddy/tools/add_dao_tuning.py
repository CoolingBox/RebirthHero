"""Tuning 新增两行：道行换算系数 + 转世最低层数。

道行公式（GDD §3.2）：道行 = (此生最高层数)^0.5 × 系数。
亚线性是保命公式 —— 保证转世收益递减，逼玩家"转世→更快回到原进度→再推进一步→再转世"。
"""
import openpyxl

PATH = r"D:\Godot\Projects\RebirthHero\addons\TinyTools\ConfigGenerator\TuningConfig.xlsx"
wb = openpyxl.load_workbook(PATH)
ws = wb.worksheets[0]

existing = set()
for r in range(1, ws.max_row + 1):
    v = ws.cell(r, 2).value
    if isinstance(v, str):
        existing.add(v)

max_id = 0
for r in range(5, ws.max_row + 1):
    v = ws.cell(r, 1).value
    if isinstance(v, (int, float)) and v > max_id:
        max_id = int(v)

NEW = [
    ["DaoRebirthCoef", 1.0, "转世",
     True,
     "GDD §3.2",
     "道行 = (此生最高层数)^0.5 × 本值。亚线性是保命公式：转世收益递减，逼玩家多转几次。"
     "1.0 时层 20 → 4.5 点，层 40 → 6.3 点（[PLACEHOLDER] 待 M3 节奏调参：目标前 3 次转世各点 2~3 个天赋节点）"],
    ["DaoMinLayer", 15, "转世",
     True,
     "用户口径 2026-11-01",
     "可转世的最低最高层数。低于它转世收益太小（层 10 只有 3 点道行），且会让玩家在层 5 反复刷。"
     "15 与境界第一破（三流身手）同层，两个系统在同一层开始给正反馈"],
]

row = ws.max_row + 1
added = 0
for item in NEW:
    if item[0] in existing:
        print("SKIP", item[0])
        continue
    ws.cell(row, 1).value = max_id + 1 + added
    for c, v in enumerate(item, start=2):
        ws.cell(row, c).value = v
    row += 1
    added += 1
    print("added", item[0])

wb.save(PATH)
print("total added", added)
