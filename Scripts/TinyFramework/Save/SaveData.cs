// 文件：SaveData.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/20 16:18

using System;

namespace TinyFramework;

public class SaveData
{
    public long Id=IDGenerator.GenerateId();
    public DateTime CreateTime=DateTime.Now;
    public DateTime UpdateTime=DateTime.Now;
    
    public void Update()
    {
        UpdateTime=DateTime.Now;
    }
}