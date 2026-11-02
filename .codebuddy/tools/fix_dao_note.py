"""DaoRebirthCoef 的备注改成"总倍率"口径（公式已从 sqrt 改成 1.5^层）。"""
import openpyxl

PATH = r"D:\Godot\Projects\RebirthHero\addons\TinyTools\ConfigGenerator\TuningConfig.xlsx"
wb = openpyxl.load_workbook(PATH)
ws = wb.worksheets[0]

NEW_NOTE = ("道行**整体倍率**（调全局收益用，默认 1.0 不改公式形状）。"
            "2026-11-01 起公式改为 道行 = 本值 × DaoPerLayer^(层数 − DaoMinLayer)，"
            "原 sqrt 亚线性口径作废（用户要指数）")

for r in range(1, ws.max_row + 1):
    if ws.cell(r, 2).value == "DaoRebirthCoef":
        ws.cell(r, 7).value = NEW_NOTE
        print("updated note at row", r)
        break

wb.save(PATH)
print("saved")
