"""大劫 + 道行公式改版（用户 2026-11-01）。

用户口径：
  · 每 2 分钟强制一场 Boss 战，失败直接结算重开，Boss 战不算入时间
  · 不要求最高层数 ≥ 15 才加道行，**3 层起就能拿**，每高一层**叠乘 1.5 倍**

改动：
  · DaoMinLayer 15 → 3
  · 新增 DaoPerLayer = 1.5（道行 = 1.5^(层数 − 3)：层 3 = 1 点、层 4 = 1.5、层 10 ≈ 11.4、层 20 ≈ 657）
  · 新增 DajieInterval = 120（秒）、DajieK = 1.8（每次打赢后下一场强度 ×1.8）
"""
import openpyxl

PATH = r"D:\Godot\Projects\RebirthHero\addons\TinyTools\ConfigGenerator\TuningConfig.xlsx"
wb = openpyxl.load_workbook(PATH)
ws = wb.worksheets[0]

KEY, VALUE, NOTE = 2, 3, 7

# 1) 改 DaoMinLayer
for r in range(1, ws.max_row + 1):
    if ws.cell(r, KEY).value == "DaoMinLayer":
        cur = ws.cell(r, VALUE).value
        if abs(float(cur) - 15) < 1e-9:
            ws.cell(r, VALUE).value = 3
            ws.cell(r, NOTE).value = ("可转世的最低最高层数。2026-11-01 用户改口径：「不要求最高层数 ≥ 15，可以 3 层 1 开始」"
                                      " —— 层 3 就能转世，配合 DaoPerLayer=1.5 的指数道行，早转晚转的取舍交给玩家")
            print("DaoMinLayer: 15 -> 3")
        else:
            print("SKIP DaoMinLayer（当前", cur, "）")

# 2) 新增行
existing = set()
for r in range(1, ws.max_row + 1):
    v = ws.cell(r, KEY).value
    if isinstance(v, str):
        existing.add(v)

max_id = 0
for r in range(5, ws.max_row + 1):
    v = ws.cell(r, 1).value
    if isinstance(v, (int, float)) and v > max_id:
        max_id = int(v)

NEW = [
    ["DaoPerLayer", 1.5, "转世", True, "用户口径 2026-11-01",
     "道行 = 本值^(最高层数 − DaoMinLayer)：层 3 = 1 点、层 4 = 1.5、层 10 ≈ 11.4、层 20 ≈ 657。"
     "⚠️ 指数道行 + 推图本身也是指数（敌方 1.16^层）→ 正反馈强，通胀风险由天赋树成本曲线吸收 [待验证]"],
    ["DajieInterval", 120, "大劫", True, "用户口径 2026-11-01",
     "每多少秒强制一场 Boss 战。**Boss 战期间不计时**（用户口径「Boss战斗不算入时间」）"],
    ["DajieK", 1.8, "大劫", True, "GDD §3.1",
     "每次**打赢**大劫后，下一场强度 ×本值。打输即转世，所以本世赢的次数就是强度档位"],
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
print("added", added)
