"""合成三个缺失的界面音（否定 / 选中 / 开关），音色与已有六个同族。

为什么不用现成素材：
  已有 `ui_click`（木块 425Hz）、`ui_confirm`（铜铃 875Hz）、`ui_open/close`（竹木 125/150Hz）
  全部是**模态合成**（按振动体模态比叠衰减正弦），实测谱峰干净、比例精确：
    ui_click  425 → 1000(×2.35) → 1650(×3.88)
    ui_confirm 875 → 1325(×1.51) → 2025(×2.31)
  而 Kenney `Interface Sounds` 的 error/select/toggle 是噪声型电子"哔/嗒"，
  FFT 上是一片 275~400Hz 的散峰。两者混用会立刻听出"这不是同一套界面的声音"
  （和 2026-11-17 命中音那次"音效不搭"是同一类问题）。
  所以这里沿用同一套合成配方，只补语义，不引入新材料。

设计（都按真实振动体，不用"噪声 + 包络"糊一个点）：
  · ui_deny   梆子双击，第二击低 70Hz —— 下行 = 否定。总长 0.20s，
              和 ui_cancel（单击 275Hz、0.20s、柔软"退"）在**击数**与**走向**上都区分开
  · ui_select 竹片单拍 0.05s、f0 760Hz、模态比 1:2.72:5.05（竹筒），
              比 ui_click 高一个八度、短一半 —— 连续切页签不会糊成一片
  · ui_toggle 木榫锁扣"咔哒"：两段相距 20ms 的瞬态，第二段更低 = 搭扣落位

响度口径：峰值归一到 0.72，与已有六个一致（0.70~0.74）。

用法：python GenUiSfx.py  →  产出 Resources/Audio/SFX/ui_{deny,select,toggle}.wav
      然后 Godot 重新导入（--headless --editor --quit）→ 启动时 WarmupSfx 校验。
"""

import os

import numpy as np
import soundfile as sf

OUT = r"D:\Godot\Projects\RebirthHero\Resources\Audio\SFX"
SR = 44100
PEAK = 0.72


def modal(f0, ratios, amps, decays, dur, start=0.0, total=None):
    """模态合成：每个模态一条衰减正弦，高阶模态衰减更快（与真实板/棒一致）。"""
    n = int(SR * dur)
    t = np.arange(n) / SR
    y = np.zeros(n, dtype=np.float64)
    for i in range(len(ratios)):
        phase = 0.35 * i            # 各模态起振不同相，避免起音处叠成一个尖峰
        y += amps[i] * np.exp(-decays[i] * t) * np.sin(2 * np.pi * f0 * ratios[i] * t + phase)
    return y, start, total


def click(dur_ms=2.0, amp=0.20, seed=7):
    """起音的空气声：极短噪声，让"敲"有接触感（纯正弦起音会像电子琴）。"""
    n = max(8, int(SR * dur_ms / 1000.0))
    rng = np.random.default_rng(seed)
    noise = rng.normal(0.0, 1.0, n)
    # 一阶差分成高频为主，再乘快速衰减
    noise = np.diff(noise, prepend=noise[0])
    env = np.exp(-np.linspace(0, 9, n))
    return noise * env * amp


def place(buf, seg, start):
    i = int(start * SR)
    end = min(len(buf), i + len(seg))
    buf[i:end] += seg[:end - i]


def normalize(y):
    peak = float(np.max(np.abs(y)))
    if peak <= 1e-9:
        raise RuntimeError("空波形")
    return y * (PEAK / peak)


def fade_out(y, ms=4.0):
    n = int(SR * ms / 1000.0)
    if n > 1 and len(y) > n:
        y[-n:] *= np.linspace(1.0, 0.0, n)
    return y


def build_deny():
    """梆子双击：400Hz → 330Hz，木块模态 1:2.32:3.9。"""
    total = int(SR * 0.20)
    buf = np.zeros(total, dtype=np.float64)

    for hit in range(2):
        f0 = 400.0 if hit == 0 else 330.0
        seg, _, _ = modal(f0, [1.0, 2.32, 3.90], [1.0, 0.42, 0.20], [38.0, 62.0, 96.0], 0.085)
        gain = 1.0 if hit == 0 else 0.88
        place(buf, seg * gain, 0.0 if hit == 0 else 0.072)
        place(buf, click(1.6, 0.16 if hit == 0 else 0.12, seed=11 + hit), 0.0 if hit == 0 else 0.072)

    return fade_out(normalize(buf))


def build_select():
    """竹片单拍：760Hz，模态比 1:2.72:5.05，0.05s。"""
    total = int(SR * 0.05)
    buf = np.zeros(total, dtype=np.float64)

    seg, _, _ = modal(760.0, [1.0, 2.72, 5.05], [1.0, 0.36, 0.18], [70.0, 110.0, 160.0], 0.05)
    place(buf, seg, 0.0)
    place(buf, click(1.2, 0.20, seed=21), 0.0)

    return fade_out(normalize(buf), 2.0)


def build_toggle():
    """木榫锁扣：两段瞬态相距 20ms，第二段更低 —— 搭扣落位。"""
    total = int(SR * 0.07)
    buf = np.zeros(total, dtype=np.float64)

    first, _, _ = modal(1080.0, [1.0, 2.10, 3.30], [1.0, 0.40, 0.18], [85.0, 130.0, 180.0], 0.032)
    place(buf, first * 0.85, 0.0)
    place(buf, click(1.2, 0.16, seed=31), 0.0)

    second, _, _ = modal(880.0, [1.0, 2.10, 3.30], [1.0, 0.46, 0.22], [70.0, 110.0, 160.0], 0.040)
    place(buf, second, 0.020)
    place(buf, click(1.4, 0.22, seed=32), 0.020)

    return fade_out(normalize(buf), 2.0)


def report(name, y):
    n = len(y)
    rms = float(np.sqrt(np.mean(y ** 2)))
    spec = np.abs(np.fft.rfft(y * np.hanning(n)))
    freqs = np.fft.rfftfreq(n, 1.0 / SR)
    centroid = float((spec * freqs).sum() / max(spec.sum(), 1e-9))
    print("%-10s %.3fs peak %.2f rms %.3f centroid %4.0fHz" % (name, n / SR, float(np.max(np.abs(y))), rms, centroid))


def main():
    os.makedirs(OUT, exist_ok=True)
    jobs = {
        "ui_deny": build_deny(),
        "ui_select": build_select(),
        "ui_toggle": build_toggle(),
    }
    for name, y in jobs.items():
        dst = os.path.join(OUT, name + ".wav")
        sf.write(dst, y, SR, subtype="PCM_16")
        report(name, y)


if __name__ == "__main__":
    main()
