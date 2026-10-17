// 文件：AudioManager.cs
// 作者：急冻雪柜
// 描述：音频管理器 —— 2D 空间音频（方位 / 距离 / 远场低通混响 / 并发预算）
// 日期：2025/11/18 12:46
//
// 总线结构（见 default_bus_layout.tres）：
//   Master ─┬─ BGM      背景音乐
//           ├─ SFX      战斗近场：干声 + 仅限幅器（刻意不加压缩器，避免不可试听的音染）
//           ├─ SFX_Far  战斗远场：低通 4kHz（空气吸收）+ 短混响（直达/混响比）
//           ├─ SFX_UI   界面音：纯干声、永不空间化
//           └─ Ambient  环境铺底：更湿的混响
//
// 空间感的三条来源（按贡献排序）：
//   1. 总线混响/低通 —— 决定"这个声音在不在一个空间里"
//   2. 方位与距离     —— AudioStreamPlayer2D + AudioListener2D，引擎代劳
//   3. 音色家族一致   —— 同一素材的微差随机（±6%），而非变声（旧参数 0.5~1.8 是 bug 级）
//
// ⚠️ 本类里的数值一律是 [PLACEHOLDER · 需实机试听校准]：
//    我能验证的是逻辑（去重/预算/抢占/总线选择），听感只能由人耳判定。

using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace TinyFramework;

public partial class AudioManager : SingletonNode<AudioManager>
{
    /// <summary>并发超预算时，低优先级先被丢弃 / 抢占。</summary>
    public enum SfxPriority
    {
        Ambient = 0,
        Hit = 1,
        Death = 2,
        Summon = 3,
        Crit = 4,
        Ui = 5,
        Event = 6,
    }

    // —— 总线名 ——
    private const string BusBgm = "BGM";
    private const string BusSfx = "SFX";
    private const string BusSfxFar = "SFX_Far";
    private const string BusSfxUi = "SFX_UI";
    private const string BusAmbient = "Ambient";
    private const string BusMaster = "Master";

    // —— 池与并发 ——
    // 16 个物理播放器：屏幕上限约 10 个单位 + 同帧多次命中的余量。
    private const int VoiceCount = 16;
    // 同时"正在播"的软上限：超了就有节奏地丢弃最低优先级，避免音效墙。
    private const int ActiveBudget = 10;
    // 同一素材 40ms 内不重复：同一 tick 内的多段伤害在听觉上应是一个事件。
    // 宁可少一次方位，也不要音效墙 —— 战斗爆发时同帧十几个音是糊的主因。
    private const double DedupMs = 40.0;

    // —— 空间参数 ——
    // MaxDistance 略大于屏宽 1920：屏幕之外即听不见。
    private const float MaxDistance = 2200f;
    // 距离超过 75%（1650）切远场总线：低通 + 混响介入，模拟空气吸收与混响占比上升。
    // ⚠️ 实测边界：1920×1080 布局下，卡片离屏幕中心最远约 1100px < 1650，
    //    所以**当前战斗里这条总线不会触发**。它服务于"屏幕外音源 / 镜头缩放 / 更大战场"，
    //    不要把"远场没生效"当成 bug —— 这是场景尺度决定的，不是参数写错。
    private const float FarRatio = 0.75f;
    // 声像强度 0.6：留 40% 余量，避免边缘音效完全偏到一只耳朵（听感像 bug）。
    private const float PanStrength = 0.6f;
    // 距离衰减陡度：1.2 比线性更"有距离感"，又不至于两屏外突然消失。
    private const float Attenuation = 1.2f;

    // —— 音色随机：同一素材的微差，不是变声 ——
    private const float PitchMin = 0.94f;
    private const float PitchMax = 1.06f;

    // —— 总线基础偏移（dB）：音量滑杆叠加在它之上，不覆盖它 ——
    private const float FarBusOffsetDb = -4f;
    private const float AmbientBusOffsetDb = -8f;

    private static readonly string[] AudioExts = { ".wav", ".ogg", ".mp3" };

    private sealed class Voice
    {
        public AudioStreamPlayer2D Player;
        public SfxPriority Priority;
        public ulong StartMs;
    }

    private AudioStreamPlayer _BGMPlayer;
    private Node2D _sfxRoot;
    private AudioListener2D _listener;
    private readonly List<Voice> _voices = new List<Voice>();
    private readonly Dictionary<string, ulong> _lastPlayMs = new Dictionary<string, ulong>();
    private string _lastBGMName;

    public override void Init()
    {
        base.Init();

        _BGMPlayer = new AudioStreamPlayer();
        _BGMPlayer.Name = "BGMPlayer";
        _BGMPlayer.SetBus(BusBgm);
        AddChild(_BGMPlayer);

        // 2D 音频需要一个 canvas 空间容器：所有空间音效都挂在这下面。
        _sfxRoot = new Node2D();
        _sfxRoot.Name = "SfxRoot";
        AddChild(_sfxRoot);

        _listener = new AudioListener2D();
        _listener.Name = "AudioListener2D";
        _sfxRoot.AddChild(_listener);

        for (int i = 0; i < VoiceCount; i++)
        {
            AudioStreamPlayer2D player = new AudioStreamPlayer2D();
            player.Name = "SfxVoice" + (i + 1);
            player.Bus = BusSfx;
            player.MaxDistance = MaxDistance;
            player.Attenuation = Attenuation;
            player.PanningStrength = PanStrength;
            player.MaxPolyphony = 1;   // 并发由本类统一管理，不交给引擎叠
            _sfxRoot.AddChild(player);

            Voice voice = new Voice();
            voice.Player = player;
            voice.Priority = SfxPriority.Ambient;
            _voices.Add(voice);
        }

        SetVolume(SettingManager.Data.Volume);
        SetBGMVolume(SettingManager.Data.BGMVolume);
        SetSFXVolume(SettingManager.Data.SFXVolume);

        LogBusStatus();
        WarmupSfx();
    }

    /// <summary>
    /// 预热并校验音效。两个作用：
    ///   1. 资源没导入 / 改名漏改 → 启动就报错，而不是战斗里静默无声；
    ///   2. 首次播放不必再走磁盘 IO，避免第一下命中卡顿。
    /// </summary>
    private void WarmupSfx()
    {
        string[] names =
        {
            "hit_light", "hit_heavy", "crit", "death", "spawn",
            "ui_click", "ui_open", "ui_close", "ui_confirm", "ui_cancel", "ui_upgrade",
            "clear_layer", "boss_down",
        };

        for (int i = 0; i < names.Length; i++)
        {
            string path = ResolveSfxPath(names[i]);

            if (!ResourceLoader.Exists(path, ""))
            {
                GD.PrintErr("[AudioManager] 音效缺失: " + path);
                continue;
            }

            AudioStream stream = ResourceLoader.Load<AudioStream>(path);
            if (stream == null)
                GD.PrintErr("[AudioManager] 音效加载失败: " + path);
        }
    }

    /// <summary>
    /// 启动时打印总线状态。总线名写错 / tres 没加载时，音效会静默无声 ——
    /// 这里把"缺失"变成明确报错，而不是等玩家反馈"没声音"再回头猜。
    /// </summary>
    private void LogBusStatus()
    {
        string[] buses = { BusMaster, BusBgm, BusSfx, BusSfxFar, BusSfxUi, BusAmbient };

        for (int i = 0; i < buses.Length; i++)
        {
            int index = AudioServer.GetBusIndex(buses[i]);
            if (index < 0)
            {
                GD.PrintErr("[AudioManager] 总线缺失: " + buses[i] + " —— 检查 default_bus_layout.tres");
                continue;
            }

            int fxCount = AudioServer.GetBusEffectCount(index);
            GD.Print("[AudioManager] 总线 " + buses[i] + " #" + index + " 效果器 " + fxCount);
        }
    }

    public override void _Ready()
    {
        base._Ready();
        // 进入树后再确认一次当前监听者，避免 Init 时机早于 viewport 就绪。
        if (_listener != null)
            _listener.MakeCurrent();
    }

    public override void UnInit()
    {
        // 退出时断开 AudioStream 引用：否则静态单例还挂着 stream，
        // 引擎退出清理时会报 "resources still in use at exit"。
        if (_BGMPlayer != null)
        {
            _BGMPlayer.Stop();
            _BGMPlayer.Stream = null;
        }

        for (int i = 0; i < _voices.Count; i++)
        {
            AudioStreamPlayer2D player = _voices[i].Player;
            if (player == null)
                continue;

            player.Stop();
            player.Stream = null;
        }

        base.UnInit();
    }

    #region 音量

    public static void SetVolume(float volume)
    {
        SetBusDb(BusMaster, volume, 0f);
    }

    public static void SetBGMVolume(float volume)
    {
        SetBusDb(BusBgm, volume, 0f);
    }

    public static void SetSFXVolume(float volume)
    {
        // 三条音效总线一起缩放；各自的基础偏移保留（远场更轻、环境更轻）。
        SetBusDb(BusSfx, volume, 0f);
        SetBusDb(BusSfxFar, volume, FarBusOffsetDb);
        SetBusDb(BusSfxUi, volume, 0f);
        SetBusDb(BusAmbient, volume, AmbientBusOffsetDb);
    }

    /// <summary>volume 是 0~100 的线性滑杆值，offsetDb 是该总线固有的基础偏移。</summary>
    private static void SetBusDb(string busName, float volume, float offsetDb)
    {
        int index = AudioServer.GetBusIndex(busName);
        if (index < 0)
            return;

        float linear = Mathf.Clamp(volume / 100f, 0.0001f, 1f);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(linear) + offsetDb);
    }

    #endregion

    #region BGM

    public static void PlayBGM(AudioStream stream)
    {
        if (stream == null)
            return;

        EnableLoop(stream);

        if (Instance._BGMPlayer.Stream == stream)
            return;

        Instance._BGMPlayer.Stream = stream;
        Instance._BGMPlayer.Play();
    }

    public static void PlayBGM(string name)
    {
        if (name == Instance._lastBGMName)
            return;

        Instance._lastBGMName = name;

        string path = ResolveAudioPath(PathDefine.BGMPath, name);
        AudioStream stream = ResManager.Load<AudioStream>(path);
        if (stream == null)
            return;

        PlayBGM(stream);
    }

    /// <summary>
    /// BGM 必须循环。素材导入时若没勾 loop，播完就静音了 —— 这里按流类型兜底。
    /// （mp3 的循环接缝受编码器填充影响，接缝位置可在 .import 里调 loop offset）
    /// </summary>
    private static void EnableLoop(AudioStream stream)
    {
        AudioStreamMP3 mp3 = stream as AudioStreamMP3;
        if (mp3 != null)
        {
            mp3.Loop = true;
            return;
        }

        AudioStreamOggVorbis ogg = stream as AudioStreamOggVorbis;
        if (ogg != null)
        {
            ogg.Loop = true;
            return;
        }

        AudioStreamWav wav = stream as AudioStreamWav;
        if (wav != null)
        {
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        }
    }

    public static void StopBGM()
    {
        Instance._BGMPlayer.Stop();
    }

    public static void PauseBGM()
    {
        Instance._BGMPlayer.StreamPaused = !Instance._BGMPlayer.StreamPaused;
    }

    #endregion

    #region SFX

    /// <summary>
    /// 在指定【世界坐标】播一个空间音效（参与方位、距离衰减、远场切换）。
    /// 坐标用 Control 时请用 <see cref="WorldCenterOf"/>，不要用 GetGlobalRect（后者只是 Rect 布局）。
    /// </summary>
    public static void PlaySFXAt(string name, Vector2 worldPos, SfxPriority priority = SfxPriority.Hit)
    {
        AudioStream stream = LoadSfx(name);
        if (stream == null)
            return;

        PlaySFXAt(stream, worldPos, priority, name);
    }

    public static void PlaySFXAt(AudioStream stream, Vector2 worldPos, SfxPriority priority = SfxPriority.Hit, string dedupKey = null)
    {
        if (stream == null || Instance == null || Instance._listener == null)
            return;

        string key = dedupKey;
        if (string.IsNullOrEmpty(key))
            key = stream.ResourcePath;

        if (Instance.IsDuplicate(key))
            return;

        AudioManager manager = Instance;
        manager.SyncListener();

        Voice voice = manager.RentVoice(priority);
        if (voice == null)
            return;

        float distance = worldPos.DistanceTo(manager._listener.GlobalPosition);
        float ratio = Mathf.Clamp(distance / MaxDistance, 0f, 1f);

        AudioStreamPlayer2D player = voice.Player;
        player.Stream = stream;
        player.GlobalPosition = worldPos;
        player.Bus = ratio >= FarRatio ? BusSfxFar : BusSfx;
        player.PitchScale = manager.NextPitch();
        player.PanningStrength = PanStrength;

        voice.Priority = priority;
        voice.StartMs = Time.GetTicksMsec();

        player.Play();
        manager.MarkPlayed(key);
    }

    /// <summary>
    /// 界面音：居中、干声、不随机音调（按钮变调会让 UI 显得不可靠），走 SFX_UI。
    /// </summary>
    public static void PlayUISFX(string name)
    {
        AudioStream stream = LoadSfx(name);
        PlayCentered(stream, SfxPriority.Ui, 1f, BusSfxUi);
    }

    /// <summary>全屏事件音（清层 / 首领倒下）：居中、不随机、走 SFX 主总线。</summary>
    public static void PlayEventSFX(string name)
    {
        AudioStream stream = LoadSfx(name);
        PlayCentered(stream, SfxPriority.Event, 1f, BusSfx);
    }

    /// <summary>不定位的音效：在监听者位置播放（居中、距离 0）。保留旧接口兼容。</summary>
    public static void PlaySFX(AudioStream stream, bool isRandPitch = false)
    {
        float pitch = 1f;
        if (isRandPitch && Instance != null)
            pitch = Instance.NextPitch();

        PlayCentered(stream, SfxPriority.Hit, pitch, BusSfx);
    }

    public static void PlaySFX(string name, bool isRandPitch = false)
    {
        AudioStream stream = LoadSfx(name);
        if (stream == null)
            return;

        PlaySFX(stream, isRandPitch);
    }

    /// <summary>
    /// 居中播放（不参与空间定位）：位置钉在监听者上，pan 归零。
    /// UI 音与全屏事件音走这里 —— 它们不该有方位。
    /// </summary>
    private static void PlayCentered(AudioStream stream, SfxPriority priority, float pitch, string bus)
    {
        if (stream == null || Instance == null || Instance._listener == null)
            return;

        AudioManager manager = Instance;

        Voice voice = manager.RentVoice(priority);
        if (voice == null)
            return;

        manager.SyncListener();

        AudioStreamPlayer2D player = voice.Player;
        player.Stream = stream;
        player.GlobalPosition = manager._listener.GlobalPosition;
        player.Bus = bus;
        player.PitchScale = pitch;
        player.PanningStrength = 0f;

        voice.Priority = priority;
        voice.StartMs = Time.GetTicksMsec();

        player.Play();
    }

    /// <summary>取 Control 中心的世界坐标（空间音效定位用）。无 Camera2D 时即 UI 坐标。</summary>
    public static Vector2 WorldCenterOf(Control control)
    {
        if (control == null)
            return Vector2.Zero;

        return control.GetGlobalTransform() * (control.Size * 0.5f);
    }

    #endregion

    #region 内部

    /// <summary>监听者始终位于可视区中心；窗口比例变化时也要跟着走。</summary>
    private void SyncListener()
    {
        if (_listener == null)
            return;

        Viewport viewport = GetViewport();
        if (viewport == null)
            return;

        Vector2 center = viewport.GetVisibleRect().Size * 0.5f;
        if (_listener.GlobalPosition != center)
            _listener.GlobalPosition = center;
    }

    /// <summary>
    /// 取一个可用 voice。规则：
    ///   1. 未达并发预算且有空闲 → 直接用；
    ///   2. 已达预算（或池满）→ 只有优先级严格高于"当前最低"才抢占，否则丢弃。
    /// 为什么不是"先到先得"：战斗爆发时同帧十几个音，先到先得会让暴击被杂音顶掉。
    /// </summary>
    private Voice RentVoice(SfxPriority priority)
    {
        Voice free = null;
        Voice lowest = null;
        int active = 0;

        for (int i = 0; i < _voices.Count; i++)
        {
            Voice voice = _voices[i];

            if (!voice.Player.Playing)
            {
                if (free == null)
                    free = voice;
                continue;
            }

            active++;

            if (lowest == null)
            {
                lowest = voice;
                continue;
            }

            if (voice.Priority < lowest.Priority)
            {
                lowest = voice;
            }
            else if (voice.Priority == lowest.Priority && voice.StartMs < lowest.StartMs)
            {
                lowest = voice;
            }
        }

        if (free != null && active < ActiveBudget)
            return free;

        if (lowest != null && priority > lowest.Priority)
        {
            lowest.Player.Stop();
            return lowest;
        }

        // 超预算且优先级不高于最低者 → 丢弃。这条保证了"最重要的声音一定听得到"。
        return null;
    }

    private bool IsDuplicate(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        ulong last;
        if (!_lastPlayMs.TryGetValue(key, out last))
            return false;

        ulong now = Time.GetTicksMsec();
        return now - last < (ulong)DedupMs;
    }

    private void MarkPlayed(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        _lastPlayMs[key] = Time.GetTicksMsec();
    }

    private float NextPitch()
    {
        return (float)GD.RandRange((double)PitchMin, (double)PitchMax);
    }

    private static AudioStream LoadSfx(string name)
    {
        string path = ResolveSfxPath(name);
        AudioStream stream = ResManager.Load<AudioStream>(path);
        return stream;
    }

    /// <summary>允许只写音效名（不带扩展名），依次尝试 .wav / .ogg / .mp3。</summary>
    private static string ResolveSfxPath(string name)
    {
        return ResolveAudioPath(PathDefine.SFXPath, name);
    }

    /// <summary>目录 + 名字 → 真实资源路径。名字已带扩展名则原样返回。</summary>
    private static string ResolveAudioPath(string dir, string name)
    {
        string direct = Path.Combine(dir, name);

        for (int i = 0; i < AudioExts.Length; i++)
        {
            if (name.EndsWith(AudioExts[i], StringComparison.OrdinalIgnoreCase))
                return direct;
        }

        for (int i = 0; i < AudioExts.Length; i++)
        {
            string candidate = direct + AudioExts[i];
            if (ResourceLoader.Exists(candidate, ""))
                return candidate;
        }

        return direct;
    }

    #endregion
}
