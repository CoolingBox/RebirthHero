# -*- coding: utf-8 -*-
"""把卡面素材的导入压缩统一成 BC7（VRAM Compressed，compress/mode=2）。

为什么：新产线直接写出 PNG，Godot 默认用 Lossless（mode=0）导入，
显存占用是 BC7 的 4 倍 —— 实测战斗场景 212MB → 289MB 就是它。
旧素材（claude_n 等）早就是 mode=2，新素材漏了这一步。

用法：python cardgen_import_settings.py [--dry]
跑完必须 `--headless --import` 让它重新导入。
"""
import os
import re
import sys

ROOT = r"D:\Godot\Projects\RebirthHero\Resources\UI\Gallery"

MODE_RE = re.compile(r"^compress/mode=(\d+)$", re.M)
MIP_RE = re.compile(r"^mipmaps/generate=(true|false)$", re.M)


def main():
    dry = "--dry" in sys.argv
    changed = 0
    total = 0
    for dirpath, _dirnames, filenames in os.walk(ROOT):
        for fn in filenames:
            if not fn.endswith(".png.import"):
                continue
            p = os.path.join(dirpath, fn)
            with open(p, "r", encoding="utf-8") as f:
                text = f.read()
            total += 1
            new = MODE_RE.sub("compress/mode=2", text, count=1)
            new = MIP_RE.sub("mipmaps/generate=true", new, count=1)
            if new == text:
                continue
            changed += 1
            if not dry:
                with open(p, "w", encoding="utf-8", newline="\n") as f:
                    f.write(new)
    print("%s：检查 %d 个 .import，改写 %d 个" % ("预演" if dry else "已写入", total, changed))


if __name__ == "__main__":
    main()
