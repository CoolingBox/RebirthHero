# GenUICode.py
# 《轮回大侠》UI 代码生成器（Python 版，镜像 addons/TinyTools/UICodeGenerator.cs 的流程）
#
# 为什么有这个脚本：
#   UICodeGenerator.cs 是 #if TOOLS 的编辑器期工具，由 TinyToolsPlugin 的「UI代码生成」按钮触发，
#   需要打开 Godot 编辑器才能跑。AI 环境起不了编辑器，故用这个 Python 脚本在外部复刻同一套逻辑，
#   产出与 C# 生成器字节级一致的代码，可复现、可纳入版本库。
#
# 流程（与 C# 版一致）：
#   1) 扫 Resources/UI/*.tscn
#   2) 对每个面板，若 Scripts/Game/UI/<Name>.cs 不存在则生成骨架（partial BasePanel，OnShow/OnHide 桩）
#      —— 已存在则跳过（开发者在那里写逻辑，不被覆盖）
#   3) 总是重新生成 Scripts/Game/UI/Generate/<Name>Bind.cs：
#      扫描 .tscn 里 name 以 T 开头的节点，声明 _Txxx 字段，并在 OnBind 里 FindChild 绑定
#
# 用法：在工程根目录执行  python .codebuddy/tools/GenUICode.py
#       （脚本以自身所在目录为 ROOT 解析 Resources/UI 与 Scripts/Game/UI，故从任意 cwd 运行均可）

import os
import re

# 脚本位于 <工程根>/.codebuddy/tools/GenUICode.py，向上找到 Godot 工程根（含 project.godot 的目录）
def _find_project_root(start):
    cur = os.path.abspath(start)
    while True:
        if os.path.exists(os.path.join(cur, "project.godot")):
            return cur
        parent = os.path.dirname(cur)
        if parent == cur:
            return os.path.dirname(os.path.abspath(__file__))
        cur = parent

ROOT = _find_project_root(os.path.dirname(os.path.abspath(__file__)))
RES_UI = os.path.join(ROOT, "Resources", "UI")
UI_CODE = os.path.join(ROOT, "Scripts", "Game", "UI")
UI_CODE_BIND = os.path.join(ROOT, "Scripts", "Game", "UI", "Generate")

NODE_RE = re.compile(r'\[node name="([^"]+)" type="([^"]+)"')

# 跳过名单：这些 .tscn 暂不走生成器流程（目前无，留空集合）。
# 注：BattlePanel 已于 2026-10-16 迁到框架约定（BattlePanel.tscn 壳内 T 节点 + Scripts/Game/UI/BattlePanel.cs
#     partial 逻辑 + Generate/BattlePanelBind.cs 绑定），故已从跳过名单移除。
SKIP = set()


def collect_tscn(res_dir):
    files = []
    if not os.path.isdir(res_dir):
        return files
    for fn in sorted(os.listdir(res_dir)):
        if fn.endswith(".tscn"):
            files.append(os.path.join(res_dir, fn))
    return files


def t_nodes(text):
    """返回 [(name, type)]，仅保留 name 以 T 开头的节点（与 UICodeGenerator.FilterTNodes 一致）。"""
    out = []
    for line in text.splitlines():
        m = NODE_RE.match(line.strip())
        if not m:
            continue
        name, typ = m.group(1), m.group(2)
        if name.startswith("T"):
            out.append((name, typ))
    return out


def gen_skeleton(name):
    """仅当文件不存在时生成（对应 GenerateUiCode 的 skip 分支）。"""
    path = os.path.join(UI_CODE, name + ".cs")
    if os.path.exists(path):
        print("[跳过] " + name + ".cs 已存在（保留开发者逻辑）")
        return
    with open(path, "w", encoding="utf-8") as f:
        f.write("using Godot;\n")
        f.write("using TinyFramework;\n\n")
        f.write("public partial class " + name + " : BasePanel\n")
        f.write("{\n")
        f.write("\tpublic override void OnShow()\n")
        f.write("\t{\n")
        f.write("\t\tbase.OnShow();\n")
        f.write("\t}\n\n")
        f.write("\tpublic override void OnHide()\n")
        f.write("\t{\n")
        f.write("\t\tbase.OnHide();\n")
        f.write("\t}\n")
        f.write("}\n")
    print("[生成] 骨架 " + name + ".cs")


def gen_bind(name, nodes):
    """总是重新生成（对应 GenerateUiCodeBind，无 skip）。"""
    os.makedirs(UI_CODE_BIND, exist_ok=True)
    path = os.path.join(UI_CODE_BIND, name + "Bind.cs")
    with open(path, "w", encoding="utf-8") as f:
        f.write("using Godot;\n")
        f.write("using TinyFramework;\n\n")
        f.write("public partial class " + name + " : BasePanel\n")
        f.write("{\n")
        for (nm, typ) in nodes:
            f.write("\tprivate " + typ + " _" + nm + ";\n")
        f.write("\tpublic override void OnBind(Control control)\n")
        f.write("\t{\n")
        f.write("\t\tbase.OnBind(control);\n")
        for (nm, typ) in nodes:
            f.write('\t\t_' + nm + ' = GameObject.FindChild("' + nm + '") as ' + typ + ';\n')
        f.write("\t}\n")
        f.write("}\n")
    print("[生成] 绑定 " + name + "Bind.cs")


def main():
    os.makedirs(UI_CODE, exist_ok=True)
    os.makedirs(UI_CODE_BIND, exist_ok=True)
    files = collect_tscn(RES_UI)
    if not files:
        print("[警告] 未在 " + RES_UI + " 找到任何 .tscn")
        return
    for full in files:
        name = os.path.splitext(os.path.basename(full))[0]
        if name in SKIP:
            print("[跳过] " + name + ".tscn 在跳过名单中")
            continue
        with open(full, encoding="utf-8") as f:
            text = f.read()
        nodes = t_nodes(text)
        gen_skeleton(name)
        gen_bind(name, nodes)


if __name__ == "__main__":
    main()
