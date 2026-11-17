"""从本地 Kenney All-in-1 生成战斗打击音（命中 30 个 + 出手 8 个）。

素材：D:\\Res\\Kenney Game Assets All-in-1 3.8.0\\Audio
  · Impact Sounds/Audio/impact*      —— 命中音六类
  · Foley Sounds/Audio/Woosh/*       —— 出手破空声
许可：Kenney 全系 CC0（可商用、可修改、免署名），见各包 License.txt。

输出到 Resources/Audio/SFX/：
  hit_<group>_<0..4>.wav   六类 × 5 变体
  whoosh_<0..7>.wav        8 个出手声

关键处理：
  · 命中音做 onset 裁剪 —— Kenney 的 impactPunch_* 前 20ms 能量只有 0.04~0.10
    （开头有渐强/房间声），不裁的话接在命中帧上会"迟到"100ms 以上；
    裁后起音陡度提到 0.4 左右，才是正常的拳击起音。
  · woosh **不能**顺手做 onset 裁剪 —— 它本身是渐强型（起音能量 0.00），
    那个"前摇"正是气流由远及近的走向，裁掉就变成"啪"的短音。
  · 音量：命中归一化到 0.88（-1.1dB），woosh 压到 0.32（-10dB）当底衬。

执行顺序：本脚本出 wav → **Godot 重新导入**（--headless --editor --quit）
→ AudioManager.WarmupSfx 校验 → 战斗中按相性播放。
"""

import os

import numpy as np
import soundfile as sf

KENNEY = r"D:\Res\Kenney Game Assets All-in-1 3.8.0\Audio"
IMPACT = os.path.join(KENNEY, "Impact Sounds", "Audio")
WOOSH = os.path.join(KENNEY, "Foley Sounds", "Audio", "Woosh")

OUT = r"D:\Godot\Projects\RebirthHero\Resources\Audio\SFX"
SR = 44100

# 相性 → 命中音类别（与 BattlePanel.HitGroupOf 一一对应）
GROUPS = {
    "punch": "impactPunch_heavy",      # 拳掌腿：肉击 + 低频体
    "blade": "impactMetal_light",      # 刀剑鞭：金属铮
    "thrown": "impactMetal_medium",    # 暗器：小金属，短促
    "wind": "impactGeneric_light",     # 轻功：轻通用，最脆
    "internal": "impactPlate_heavy",   # 医毒奇门禅：钝击闷响
    "beast": "impactSoft_heavy",       # 兽：低频闷
}

# 出手声按长短分两档，让尾音大致落在命中附近
WOOSH_SHORT = ["woosh6.ogg", "woosh4.ogg", "woosh1.ogg", "woosh5.ogg"]   # 0.30~0.41s
WOOSH_LONG = ["woosh2.ogg", "woosh8.ogg", "woosh3.ogg", "woosh7.ogg"]    # 0.54~0.90s


def load(path):
    a, sr = sf.read(path, always_2d=True)
    a = a.mean(axis=1).astype(np.float32)
    if sr != SR:
        n = int(round(len(a) * SR / float(sr)))
        a = np.interp(np.linspace(0, len(a) - 1, n), np.arange(len(a)), a).astype(np.float32)
    return a


def onset_trim(a, thresh=0.30, pre=0.004, max_sec=0.55):
    """裁掉前摇与尾部余音。阈值 30% 是为了跳过渐强段，直接落到打击主体。"""
    peak = float(np.abs(a).max())
    if peak < 1e-5:
        return a

    idx = np.where(np.abs(a) > peak * thresh)[0]
    start = 0
    if len(idx) > 0:
        start = max(0, idx[0] - int(pre * SR))

    b = a[start:]
    peak = float(np.abs(b).max())
    if peak < 1e-5:
        return b

    tail = np.where(np.abs(b) > peak * 0.015)[0]
    end = len(b) if len(tail) == 0 else min(len(b), tail[-1] + int(0.040 * SR))
    b = b[:min(end, int(max_sec * SR))].copy()

    fade = min(len(b), int(0.018 * SR))
    b[-fade:] *= np.linspace(1.0, 0.0, fade)
    return b


def tail_trim(a, gain):
    """只裁尾部 + 淡出，保留渐强（woosh 用）。"""
    peak = float(np.abs(a).max())
    if peak > 1e-5:
        tail = np.where(np.abs(a) > peak * 0.01)[0]
        if len(tail) > 0:
            a = a[:min(len(a), tail[-1] + int(0.040 * SR))].copy()

    fade = min(len(a), int(0.030 * SR))
    a[-fade:] *= np.linspace(1.0, 0.0, fade)

    peak = float(np.abs(a).max())
    if peak > 1e-5:
        a = a / peak * gain
    return a


def main():
    os.makedirs(OUT, exist_ok=True)
    count = 0

    print("—— 命中音（六类 × 5 变体）——")
    for group, prefix in GROUPS.items():
        for i in range(5):
            src = os.path.join(IMPACT, "%s_%03d.ogg" % (prefix, i))
            if not os.path.exists(src):
                print("缺", src)
                continue
            a = onset_trim(load(src))
            peak = float(np.abs(a).max())
            if peak > 1e-5:
                a = a / peak * 0.88
            name = "hit_%s_%d.wav" % (group, i)
            sf.write(os.path.join(OUT, name), a, SR, subtype="PCM_16")
            count += 1
            print("  %-16s %5.2fs" % (name, len(a) / SR))

    print("—— 出手破空声（8 个）——")
    for idx, name in enumerate(WOOSH_SHORT + WOOSH_LONG):
        src = os.path.join(WOOSH, name)
        if not os.path.exists(src):
            print("缺", src)
            continue
        a = tail_trim(load(src), 0.32)
        out = "whoosh_%d.wav" % idx
        sf.write(os.path.join(OUT, out), a, SR, subtype="PCM_16")
        count += 1
        print("  %-16s %5.2fs" % (out, len(a) / SR))

    print("\n共 %d 个文件 → %s" % (count, OUT))
    print("下一步：--headless --editor --quit 重新导入，否则运行时仍是旧资源")


if __name__ == "__main__":
    main()
