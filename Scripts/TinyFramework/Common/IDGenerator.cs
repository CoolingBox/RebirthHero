// 文件：IDGenerator.cs
// 作者：急冻雪柜
// 描述：
// 日期：2025/11/20 16:19

using System;

namespace TinyFramework;

public class IDGenerator
{
    private static long _lastTimestamp = -1L;
    private static int _sequence = 0;
    private static readonly object _lock = new object();
    
    /// <summary>
    /// 生成ID，格式：毫秒级时间戳 + 4位自增位
    /// </summary>
    /// <returns>18位数字ID（13位时间戳 + 4位序列号）</returns>
    public static long GenerateId()
    {
        lock (_lock)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            if (timestamp == _lastTimestamp)
            {
                _sequence++;
                if (_sequence >= 10000)
                {
                    // 如果序列号溢出，等待到下一毫秒
                    while (timestamp <= _lastTimestamp)
                    {
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }
                    _sequence = 0;
                }
            }
            else
            {
                _sequence = 0;
            }
            
            _lastTimestamp = timestamp;
            
            // 组合时间戳和序列号：时间戳 * 10000 + 序列号
            return timestamp * 10000 + _sequence;
        }
    }
    
    /// <summary>
    /// 生成字符串格式的ID
    /// </summary>
    /// <returns>字符串格式的ID</returns>
    public static string GenerateIdString()
    {
        return GenerateId().ToString();
    }
    
    /// <summary>
    /// 从ID中提取时间戳
    /// </summary>
    /// <param name="id">生成的ID</param>
    /// <returns>时间戳（毫秒）</returns>
    public static long ExtractTimestamp(long id)
    {
        return id / 10000;
    }
    
    /// <summary>
    /// 从ID中提取序列号
    /// </summary>
    /// <param name="id">生成的ID</param>
    /// <returns>序列号</returns>
    public static int ExtractSequence(long id)
    {
        return (int)(id % 10000);
    }
}