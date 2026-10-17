// 文件：BigNum.cs
// 作者：急冻雪柜
// 描述：大数系统(尾数+指数, break_infinity 思路)。放置/增量游戏用,可稳定表示到 1e(1e308),且性能远好于 BigInteger。
// 日期：2026/09/23

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFramework;

/// <summary>
/// 大数显示模式。
/// Scientific = 科学表示法(K/M/B/T/aa 字母后缀,放置游戏惯例);
/// Chinese    = 中文计数法(万/亿/兆/京/垓/秭/穰/沟/涧/正/载 万进制单位)。
/// </summary>
public enum NumberFormat
{
    Scientific,
    Chinese,
}

/// <summary>
/// 大数类型。内部表示为 value = Mantissa * 10^Exponent,其中 Mantissa 归一化到 [1,10)(0 时 Mantissa=0)。
/// 直接公开字段以便 System.Text.Json(IncludeFields)序列化;算术运算均经 Normalize 保持归一化。
/// </summary>
public struct BigNum : IComparable<BigNum>, IEquatable<BigNum>
{
    public double Mantissa;
    public long Exponent;

    public static readonly BigNum Zero = new BigNum();
    public static readonly BigNum One = From(1);

    public bool IsZero => Mantissa == 0.0;

    /// <summary>全局显示模式,由 Setting 在启动时设置。默认科学表示法。</summary>
    public static NumberFormat DisplayMode { get; set; } = NumberFormat.Scientific;

    /// <summary>从 double 构造(仅在 double 可表示的范围内;超出 double 范围请用 Normalize/运算符)。</summary>
    public static BigNum From(double value)
    {
        if (value == 0.0 || double.IsNaN(value) || double.IsInfinity(value))
            return Zero;

        double abs = Math.Abs(value);
        long exp = (long)Math.Floor(Math.Log10(abs));
        double mant = abs / Math.Pow(10, exp);
        if (mant >= 10.0) { mant /= 10.0; exp += 1; }
        else if (mant < 1.0) { mant *= 10.0; exp -= 1; }
        return new BigNum { Mantissa = value < 0 ? -mant : mant, Exponent = exp };
    }

    /// <summary>由(尾数, 指数)直接构造并归一化。供 JSON 转换器解析对象形式时使用。</summary>
    public static BigNum FromParts(double mantissa, long exponent) => Normalize(mantissa, exponent);

    /// <summary>归一化:把任意 (mantissa, exponent) 收拢到 Mantissa∈[1,10)(0 收为 Zero)。</summary>
    private static BigNum Normalize(double mantissa, long exponent)
    {
        if (mantissa == 0.0 || double.IsNaN(mantissa) || double.IsInfinity(mantissa))
            return Zero;

        bool neg = mantissa < 0;
        double m = Math.Abs(mantissa);
        if (m >= 10.0)
        {
            while (m >= 10.0 && exponent < 1_000_000_000L) { m /= 10.0; exponent += 1; }
        }
        else if (m < 1.0)
        {
            while (m < 1.0 && m > 0.0 && exponent > -1_000_000_000L) { m *= 10.0; exponent -= 1; }
        }
        return new BigNum { Mantissa = neg ? -m : m, Exponent = exponent };
    }

    public BigNum Negate() => Normalize(-Mantissa, Exponent);
    public BigNum Abs() => Normalize(Math.Abs(Mantissa), Exponent);

    // ---------- 运算符 ----------
    public static BigNum operator +(BigNum a, BigNum b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        if (a.Exponent > b.Exponent)
        {
            long diff = a.Exponent - b.Exponent;
            if (diff > 15) return a; // b 小于 double 精度,可忽略
            return Normalize(a.Mantissa + b.Mantissa * Math.Pow(10, -diff), a.Exponent);
        }
        else if (b.Exponent > a.Exponent)
        {
            long diff = b.Exponent - a.Exponent;
            if (diff > 15) return b;
            return Normalize(b.Mantissa + a.Mantissa * Math.Pow(10, -diff), b.Exponent);
        }
        return Normalize(a.Mantissa + b.Mantissa, a.Exponent);
    }

    public static BigNum operator -(BigNum a, BigNum b) => a + b.Negate();

    public static BigNum operator *(BigNum a, BigNum b)
    {
        if (a.IsZero || b.IsZero) return Zero;
        return Normalize(a.Mantissa * b.Mantissa, a.Exponent + b.Exponent);
    }

    public static BigNum operator /(BigNum a, BigNum b)
    {
        if (b.IsZero) return Zero;       // 除以 0 视为 0(放置游戏通常不崩)
        if (a.IsZero) return Zero;
        return Normalize(a.Mantissa / b.Mantissa, a.Exponent - b.Exponent);
    }

    public static BigNum operator -(BigNum a) => a.Negate();

    public static implicit operator BigNum(int v) => From(v);
    public static implicit operator BigNum(long v) => From(v);
    public static implicit operator BigNum(double v) => From(v);
    public static explicit operator double(BigNum b) => b.IsZero ? 0 : b.Mantissa * Math.Pow(10, b.Exponent);

    public int CompareTo(BigNum o)
    {
        bool az = IsZero, oz = o.IsZero;
        if (az && oz) return 0;
        if (az) return o.Mantissa < 0 ? 1 : -1;
        if (oz) return Mantissa > 0 ? 1 : -1;
        bool aPos = Mantissa > 0, oPos = o.Mantissa > 0;
        if (aPos != oPos) return aPos ? 1 : -1;
        int c = Exponent.CompareTo(o.Exponent);
        if (c != 0) return aPos ? c : -c;
        c = Mantissa.CompareTo(o.Mantissa);
        return aPos ? c : -c;
    }

    public bool Equals(BigNum o) => Mantissa == o.Mantissa && Exponent == o.Exponent;
    public override bool Equals(object obj) => obj is BigNum o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Mantissa, Exponent);

    public static bool operator ==(BigNum a, BigNum b) => a.Equals(b);
    public static bool operator !=(BigNum a, BigNum b) => !a.Equals(b);
    public static bool operator <(BigNum a, BigNum b) => a.CompareTo(b) < 0;
    public static bool operator >(BigNum a, BigNum b) => a.CompareTo(b) > 0;
    public static bool operator <=(BigNum a, BigNum b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BigNum a, BigNum b) => a.CompareTo(b) >= 0;

    // ---------- 常用工具 ----------
    public static BigNum Max(BigNum a, BigNum b) => a >= b ? a : b;
    public static BigNum Min(BigNum a, BigNum b) => a <= b ? a : b;
    public static BigNum Clamp(BigNum v, BigNum min, BigNum max) => v < min ? min : (v > max ? max : v);

    /// <summary>以 10 为底的对数,返回 double(用于按数量级缩放显示等)。</summary>
    public double Log10() => IsZero ? double.NegativeInfinity : Exponent + Math.Log10(Math.Abs(Mantissa));

    // ---------- 显示 / 解析 ----------
    private static readonly string[] NamedSuffixes =
        { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc", "Ud", "Dd", "Cp", "Ep" };

    private static readonly Dictionary<string, long> _suffixExp =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    static BigNum()
    {
        for (int i = 1; i < NamedSuffixes.Length; i++)
            _suffixExp[NamedSuffixes[i]] = i * 3L;
    }

    private static string GetSuffix(long tier)
    {
        if (tier < NamedSuffixes.Length) return NamedSuffixes[tier];
        long n = tier - NamedSuffixes.Length;
        char a = (char)('a' + (n / 26));
        char b = (char)('a' + (n % 26));
        return "" + a + b;
    }

    private static long SuffixToExponent(string suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return 0;
        if (_suffixExp.TryGetValue(suffix, out long e)) return e;
        if (suffix.Length == 2 && char.IsLower(suffix[0]) && char.IsLower(suffix[1]))
        {
            long tier = NamedSuffixes.Length + (suffix[0] - 'a') * 26 + (suffix[1] - 'a');
            return tier * 3L;
        }
        return 0;
    }

    private static string FormatPlain(double v, int decimals)
    {
        if (v == Math.Floor(v)) return ((long)v).ToString(CultureInfo.InvariantCulture);
        if (v >= 100) return v.ToString("F0", CultureInfo.InvariantCulture);
        if (v >= 10) return v.ToString("F1", CultureInfo.InvariantCulture);
        return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static string FormatSuffix(double v, int decimals)
    {
        if (v >= 100) return v.ToString("F0", CultureInfo.InvariantCulture);
        if (v >= 10) return v.ToString("F1", CultureInfo.InvariantCulture);
        return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>按当前全局 DisplayMode 显示。decimals 控制小数位。</summary>
    public string ToString(int decimals) => ToString(decimals, DisplayMode);

    /// <summary>按指定模式显示。科学表示法如 1.5K/12.3M/4.56aa;中文计数法如 1.5万/12.3亿/4.56兆。</summary>
    public string ToString(int decimals, NumberFormat mode)
    {
        if (IsZero) return "0";
        bool neg = Mantissa < 0;
        double m = Math.Abs(Mantissa);
        string body = mode == NumberFormat.Chinese
            ? FormatChinese(m, Exponent, decimals)
            : FormatScientific(m, Exponent, decimals);
        return (neg ? "-" : "") + body;
    }

    public override string ToString() => ToString(2);

    private static string FormatScientific(double m, long e, int decimals)
    {
        if (e < 3)
            return FormatPlain(m * Math.Pow(10, e), decimals);
        long tier = e / 3;
        long rem = e - tier * 3;
        return FormatSuffix(m * Math.Pow(10, rem), decimals) + GetSuffix(tier);
    }

    // 中文万进制单位:索引 i 对应 10^(4i)。载(10^44)之后回退科学串。
    private static readonly string[] ChineseUnits =
        { "", "万", "亿", "兆", "京", "垓", "秭", "穰", "沟", "涧", "正", "载" };

    private static string FormatChinese(double m, long e, int decimals)
    {
        if (e < 4)
            return FormatPlain(m * Math.Pow(10, e), decimals);
        long tier = e / 4;
        if (tier >= ChineseUnits.Length)
            return m.ToString("G15", CultureInfo.InvariantCulture) + "E" + e; // 超出"载"回退科学串
        long rem = e - tier * 4;
        double v = m * Math.Pow(10, rem); // [1, 10000)
        return FormatChineseNumber(v, decimals) + ChineseUnits[tier];
    }

    private static string FormatChineseNumber(double v, int decimals)
    {
        if (v >= 1000) return v.ToString("F0", CultureInfo.InvariantCulture);
        if (v >= 100) return v.ToString("F1", CultureInfo.InvariantCulture);
        if (v >= 10) return v.ToString("F2", CultureInfo.InvariantCulture);
        return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    /// <summary>紧凑科学计数串,如 "1.5E3",用于 JSON 序列化(无损往返)。</summary>
    public string ToScientific()
    {
        if (IsZero) return "0";
        return Mantissa.ToString("G15", CultureInfo.InvariantCulture) + "E" + Exponent;
    }

    public static BigNum Parse(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return Zero;

        double sign = 1;
        if (s[0] == '-') { sign = -1; s = s.Substring(1); }
        else if (s[0] == '+') { s = s.Substring(1); }

        int eIdx = s.IndexOfAny(new[] { 'e', 'E' });
        if (eIdx >= 0)
        {
            double m = double.Parse(s.Substring(0, eIdx), CultureInfo.InvariantCulture);
            long e = long.Parse(s.Substring(eIdx + 1), CultureInfo.InvariantCulture);
            return Normalize(sign * m, e);
        }

        int i = s.Length - 1;
        while (i >= 0 && char.IsLetter(s[i])) i--;
        string num = s.Substring(0, i + 1);
        string suf = s.Substring(i + 1);
        double m2 = double.Parse(num, CultureInfo.InvariantCulture);
        long exp = SuffixToExponent(suf);
        return Normalize(sign * m2, exp);
    }
}

/// <summary>
/// BigNum 的 JSON 转换器:序列化为紧凑科学串(如 "1.5E3"),可无损往返;
/// 读取时同时兼容 数字字面量(配置里写 100 即可)与 科学串/后缀串(如 "2K")。
/// 在 JsonSerializerOptions.Converters 注册一次即可(配合项目 IncludeFields 设置)。
/// </summary>
public class BigNumJsonConverter : JsonConverter<BigNum>
{
    public override BigNum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return BigNum.Zero;
        if (reader.TokenType == JsonTokenType.Number) return BigNum.From(reader.GetDouble());
        if (reader.TokenType == JsonTokenType.String) return BigNum.Parse(reader.GetString()!);
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            double mant = 0; long exp = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string prop = reader.GetString()!;
                    reader.Read();
                    if (prop == "Mantissa") mant = reader.GetDouble();
                    else if (prop == "Exponent") exp = reader.GetInt64();
                }
            }
            return BigNum.FromParts(mant, exp);
        }
        throw new JsonException("Unexpected token type for BigNum.");
    }

    public override void Write(Utf8JsonWriter writer, BigNum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToScientific());
    }
}
