// 文件：PathDefine.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/15 2:03

using System.IO;
using Godot;

namespace TinyFramework;

public class PathDefine
{
    public static string UserPath = ProjectSettings.GlobalizePath("user://");
    
    public const string ResPath = "res://Resources/";
    public const string UIPath = ResPath + "UI/";
    public const string UIScriptPath = "res://Scripts/Game/UI/";
    
    public const string BGMPath = ResPath + "Audio/BGM/";
    public const string SFXPath = ResPath + "Audio/SFX/";
    
    public const string ConfigPath= ResPath+ "Config/";
  
}