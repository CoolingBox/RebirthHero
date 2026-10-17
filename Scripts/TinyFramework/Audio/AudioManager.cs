// 文件：AudioManager.cs
// 作者：急冻雪柜
// 描述：音频管理器
// 日期：2025/11/18 12:46

using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace TinyFramework;

public partial class AudioManager : SingletonNode<AudioManager>
{
    private AudioStreamPlayer _BGMPlayer;
    private readonly List<AudioStreamPlayer> _SFXPlayers = new List<AudioStreamPlayer>();
    
    private int _SFXPlayerCount = 10;
    private int _SFXPlayerIndex = 0;
    private string _lastBGMName;

    private static readonly float _randPitchMax = 1.8f;
    private static readonly float _randPitchMin = 0.5f;
    
    public override void Init()
    {
        base.Init();
        _BGMPlayer = new AudioStreamPlayer();
        _BGMPlayer.Name = "BGMPlayer";
        _BGMPlayer.SetBus("BGM");
        AddChild(_BGMPlayer);

        for (int i = 0; i < _SFXPlayerCount; i++)
        {
            var player = new AudioStreamPlayer();
            player.Name = $"SFXPlayer{i+1}";
            player.SetBus("SFX");
            AddChild(player);
            _SFXPlayers.Add(player);
        }
        
        SetVolume(SettingManager.Data.Volume);
        SetBGMVolume(SettingManager.Data.BGMVolume);
        SetSFXVolume(SettingManager.Data.SFXVolume);
    }

    public static void SetVolume(float volume)
    {
        AudioServer.SetBusVolumeLinear(AudioServer.GetBusIndex("Master"), volume / 100);
    }

    public static void SetBGMVolume(float volume)
    {
        AudioServer.SetBusVolumeLinear(AudioServer.GetBusIndex("BGM"), volume / 100);
    }
    
    public static void SetSFXVolume(float volume)
    {
        AudioServer.SetBusVolumeLinear(AudioServer.GetBusIndex("SFX"), volume / 100);
        
    }

    #region BGM
    
    public static void PlayBGM(AudioStream stream)
    {
        if (Instance._BGMPlayer.Stream == stream)
        {
            return;
        }
        Instance._BGMPlayer.Stream = stream;
        Instance._BGMPlayer.Play();
    }

    public static void PlayBGM(string name)
    {
        if (name == Instance._lastBGMName)
            return;
        Instance._lastBGMName = name;

        string path = Path.Combine(PathDefine.BGMPath, name);
        
        var stream = ResManager.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PrintRich($"[color=Red]未找到音频文件:[/color]{name}");
            return;
        }
        PlayBGM(stream);
    }

    public static void StopBGM()
    {
        Instance._BGMPlayer.Stop();
    }

    public static void PauseBGM()
    {
        Instance._BGMPlayer.StreamPaused=!Instance._BGMPlayer.StreamPaused;
    }

    #endregion

    #region SFX
    
    /// <summary>
    /// 播放音效
    /// </summary>

    public static void PlaySFX(AudioStream stream,bool isRandPitch=false)
    {
        AudioStreamPlayer player = Instance._SFXPlayers[Instance._SFXPlayerIndex];
        player.Stream = stream;
        if (isRandPitch)
        {
            // 随机音调
            player.PitchScale =(float) GD.RandRange(_randPitchMin,_randPitchMax);
        }
        else
        {
            player.PitchScale = 1;
        }
        player.Play();
        Instance._SFXPlayerIndex = (Instance._SFXPlayerIndex + 1) % Instance._SFXPlayerCount;
    }   
    
    public static void PlaySFX(string name,bool isRandPitch=false)
    {
        string path = Path.Combine(PathDefine.SFXPath, name);
        
        var stream = ResManager.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PrintRich($"[color=Red]未找到音频文件:[/color]{name}");
            return;
        }
        PlaySFX(stream,isRandPitch);
    }
    

    #endregion
    
}