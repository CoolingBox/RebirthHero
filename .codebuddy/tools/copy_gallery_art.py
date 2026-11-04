"""把 4 层卡面素材拷进《轮回大侠》：Resources/UI/Gallery/<key>/L1..L4.png

与 CardParallaxDemo 的 textures/p4 同一套目录名，方便以后两边同步替换。
三兽没有卡框（只有 3 层），不补 L4 —— 图鉴里它们就是 3 层视差。

以后要把"试验素材"换成正式立绘：按侠客 Id 建目录（hero_01 … hero_20），
把 L1..L4.png 丢进去，再把 GalleryPanel 的条目表改指向即可，代码零改动。
"""
import os, shutil

CARD5 = r'C:\Users\XJ\Desktop\武侠AI娘图\_card5'
BEAST = r'C:\Users\XJ\Desktop\武侠三兽'
DST = r'D:\Godot\Projects\RebirthHero\Resources\UI\Gallery'

MAP = [
    ('Claude_Q版', 'claude_q', CARD5), ('Claude_娘', 'claude_n', CARD5),
    ('DeepSeek_Q版', 'deepseek_q', CARD5), ('DeepSeek_娘', 'deepseek_n', CARD5),
    ('Gemini_Q版', 'gemini_q', CARD5), ('Gemini_娘', 'gemini_n', CARD5),
    ('GLM_Q版', 'glm_q', CARD5), ('GLM_娘', 'glm_n', CARD5),
    ('GPT_Q版', 'gpt_q', CARD5), ('GPT_娘', 'gpt_n', CARD5),
    ('Grok_Q版', 'grok_q', CARD5), ('Grok_娘', 'grok_n', CARD5),
    ('Kimi_Q版', 'kimi_q', CARD5), ('Kimi_娘', 'kimi_n', CARD5),
    ('Qwen_Q版', 'qwen_q', CARD5), ('Qwen_娘', 'qwen_n', CARD5),
    ('feidudu_肥嘟嘟', 'beast_feidudu', BEAST),
    ('naiwa_奶蛙', 'beast_naiwa', BEAST),
    ('niulai_牛来', 'beast_niulai', BEAST),
]

SRC = [('L1_bg_far.png', 'L1.png'), ('L2_char.png', 'L2.png'),
       ('L3_fg_near.png', 'L3.png'), ('L4_frame.png', 'L4.png')]

os.makedirs(DST, exist_ok=True)
n = 0
for src_name, key, root in MAP:
    sd = os.path.join(root, src_name)
    dd = os.path.join(DST, key)
    os.makedirs(dd, exist_ok=True)
    for s, d in SRC:
        sp = os.path.join(sd, s)
        if os.path.exists(sp):
            shutil.copyfile(sp, os.path.join(dd, d))
            n += 1

total = 0
for r, ds, fs in os.walk(DST):
    for f in fs:
        total += os.path.getsize(os.path.join(r, f))
print('copied', n, 'files, %.1f MB' % (total / 1024.0 / 1024.0), 'groups', len(os.listdir(DST)))
