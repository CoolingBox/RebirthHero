#if TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;

using Godot;
using TinyFramework;

/// <summary>
/// 配置生成器。读取 res://addons/TinyTools/ConfigGenerator 下的 .xlsx(OpenXML),
/// 1) 为每个 Sheet 生成字段类(Scripts/Game/Config/Generate/,继承 BaseConfig);
/// 2) 生成 JSON 配置数据(Resources/Config/)。
/// Excel 布局:第 0 行=字段名,第 1 行=类型,第 2 行=注释,第 3 行跳过,第 4 行起为数据。
/// 由 TinyToolsPlugin 的「配置生成」按钮调用。
///
/// 读取方式为纯原生(.NET 内置 System.IO.Compression + System.Xml),不依赖任何第三方包:
/// .xlsx 本质是一个 ZIP 包,内部为 XML(sharedStrings.xml / workbook.xml / worksheets/sheetN.xml)。
/// 这些 API 来自共享框架(默认 ALC),不会加载进可重载的 AssemblyLoadContext,因此不会触发 issue 78513。
/// 旧实现曾引入 ExcelDataReader + System.Text.Encoding.CodePages,并因手动调用
/// Encoding.RegisterProvider 把静态引用从共享框架指向可重载 ALC 内的程序集,导致程序集无法卸载。
/// 现改为原生实现后,该依赖与风险一并消除。(.xls 二进制格式为死代码,不再支持。)
/// </summary>
public static class ConfigGenerator
{
    public static string ExcelFilePath => ProjectSettings.GlobalizePath("res://addons/TinyTools/ConfigGenerator");
    public static string ExcelFieldClassPath => ProjectSettings.GlobalizePath("res://Scripts/Game/Config/Generate/");
    public static string ExcelConfigPath => ProjectSettings.GlobalizePath("res://Resources/Config/");

    // Sheet名 -> 行数据(每行=列字符串列表)。约定:第0行字段名/第1行类型/第2行注释/第3行跳过/第4行起数据。
    private static readonly Dictionary<string, List<List<string>>> _sheetDict = new Dictionary<string, List<List<string>>>();

    public static void Generate()
    {
        _sheetDict.Clear();
        GD.PrintRich("[color=Yellow]开始生成配置[/color]");
        ReadExcel();
        
        GenerateFieldClass();
        
        GenerateJsonFile();
        GD.PrintRich("[color=Yellow]生成完毕[/color]");
    }

    private static void GenerateJsonFile()
    {
        if (!Directory.Exists(ExcelConfigPath))
        {
            Directory.CreateDirectory(ExcelConfigPath);
        }

        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new BigNumJsonConverter() } };

        foreach (var kv in _sheetDict)
        {
            string name = kv.Key;
            List<List<string>> sheet = kv.Value;

            var list = new List<Dictionary<string, object>>();

            List<string> rowName = sheet[0];
            List<string> rowType = sheet[1];

            for (int i = 4; i < sheet.Count; i++)
            {
                List<string> dataRow = sheet[i];
                if (dataRow.Count == 0 || string.IsNullOrEmpty(dataRow[0]))
                {
                    // id行没数据,直接跳过
                    continue;
                }

                var row = new Dictionary<string, object>();

                for (int j = 0; j < rowName.Count && j < dataRow.Count; j++)
                {
                    string field = rowName[j];
                    string type = rowType[j];
                    string value = dataRow[j];
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(field))
                    {
                        row[field] = ConvertValue(type, value);
                    }
                }

                list.Add(row);
            }

            // 生成 Json 字符串(类型化输出,以兼容 System.Text.Json 严格反序列化)
            string json = JsonSerializer.Serialize(list, options);
            File.WriteAllText(ExcelConfigPath + name + ".json", json);
            GD.PrintRich($"[color=Orange]已生成配置:[/color] {name}.json (共 {list.Count} 条)");
        }
    }

    private static object ConvertValue(string type, string value)
    {
        value = value.Trim();
        switch (type)
        {
            case "int":
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;
            case "long":
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : 0L;
            case "float":
                return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
            case "double":
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0d;
            case "bool":
                return ParseBool(value);
            case "string":
                return value;
            case "int[]":
                return ParseIntArray(value);
            case "long[]":
                return ParseLongArray(value);
            case "float[]":
                return ParseFloatArray(value);
            case "double[]":
                return ParseDoubleArray(value);
            case "bool[]":
                return ParseBoolArray(value);
            case "string[]":
                return ParseStringArray(value);
            default:
                // 未知类型视为枚举:Excel 单元格存的是枚举数值,输出数字以便 System.Text.Json 严格反序列化
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int e) ? e : (object)value;
        }
    }

    private static List<int> ParseIntArray(string value)
    {
        var list = new List<int>();
        foreach (var p in value.Split('|'))
        {
            var s = p.Trim();
            if (s.Length == 0)
                continue;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                list.Add(v);
        }
        return list;
    }

    private static List<long> ParseLongArray(string value)
    {
        var list = new List<long>();
        foreach (var p in value.Split('|'))
        {
            var s = p.Trim();
            if (s.Length == 0)
                continue;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                list.Add(v);
        }
        return list;
    }

    private static List<float> ParseFloatArray(string value)
    {
        var list = new List<float>();
        foreach (var p in value.Split('|'))
        {
            var s = p.Trim();
            if (s.Length == 0)
                continue;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                list.Add(v);
        }
        return list;
    }

    private static List<double> ParseDoubleArray(string value)
    {
        var list = new List<double>();
        foreach (var p in value.Split('|'))
        {
            var s = p.Trim();
            if (s.Length == 0)
                continue;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                list.Add(v);
        }
        return list;
    }

    private static bool ParseBool(string value)
    {
        if (bool.TryParse(value, out bool b))
            return b;
        // 兼容 Excel 数值型布尔:1/yes/true -> true,0/no/false -> false
        return value == "1" || value.ToLowerInvariant() == "yes" || value.ToLowerInvariant() == "true";
    }

    private static List<bool> ParseBoolArray(string value)
    {
        var list = new List<bool>();
        foreach (var p in value.Split('|'))
        {
            var s = p.Trim();
            if (s.Length == 0)
                continue;
            list.Add(ParseBool(s));
        }
        return list;
    }

    private static List<string> ParseStringArray(string value)
    {
        var list = new List<string>();
        foreach (var p in value.Split('|'))
        {
            var s = p.Trim();
            if (s.Length == 0)
                continue;
            list.Add(s);
        }
        return list;
    }

    private static void GenerateFieldClass()
    {
        if (!Directory.Exists(ExcelFieldClassPath))
        {
            Directory.CreateDirectory(ExcelFieldClassPath);
        }

        foreach (var kv in _sheetDict)
        {
            string name = kv.Key;
            List<List<string>> sheet = kv.Value;
            List<string> rowName = sheet[0];
            List<string> rowType = sheet[1];
            List<string> rowDesc = sheet[2];

            var sb = new StringBuilder();
            sb.AppendLine("using TinyFramework;");
            sb.AppendLine($"public class {name} : BaseConfig");
            sb.AppendLine("{");
            // 忽略第一列 Id(已在 BaseConfig 中定义)
            for (int i = 1; i < rowName.Count; i++)
            {
                string desc = i < rowDesc.Count ? rowDesc[i] : "";
                sb.AppendLine($"\t/// <summary> {desc} </summary>");
                sb.AppendLine($"\tpublic {rowType[i]} {rowName[i]};");
            }

            sb.Append('}');
            File.WriteAllText($"{ExcelFieldClassPath}{name}.cs", sb.ToString());
            GD.PrintRich($"[color=Orange]已生成字段类:[/color] {name}.cs");
        }
    }

    // ---------- 原生 .xlsx 读取(OpenXML: ZIP + XML,无需任何第三方包) ----------

    private static void ReadExcel()
    {
        DirectoryInfo directory = Directory.CreateDirectory(ExcelFilePath);
        foreach (FileInfo fileInfo in directory.GetFiles())
        {
            if (fileInfo.Extension.ToLowerInvariant() != ".xlsx")
            {
                continue;
            }

            GD.PrintRich($"[color=Orange]正在读取Excel:[/color] {fileInfo.Name}");
            ReadXlsx(fileInfo.FullName);
        }
    }

    private static void ReadXlsx(string path)
    {
        using var archive = ZipFile.OpenRead(path);

        // 1) 共享字符串表 xl/sharedStrings.xml
        var sharedStrings = new List<string>();
        var ssEntry = archive.GetEntry("xl/sharedStrings.xml");
        if (ssEntry != null)
        {
            using var ssStream = ssEntry.Open();
            var ssDoc = new XmlDocument();
            ssDoc.Load(ssStream);
            foreach (XmlNode si in ssDoc.SelectNodes("//*[local-name()='si']"))
            {
                var sb = new StringBuilder();
                foreach (XmlNode t in si.SelectNodes(".//*[local-name()='t']"))
                {
                    sb.Append(t.InnerText);
                }
                sharedStrings.Add(sb.ToString());
            }
        }

        // 2) workbook.xml + xl/_rels/workbook.xml.rels,建立 sheet名 -> worksheet 条目路径
        var relTargetById = new Dictionary<string, string>();
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            using var relsStream = relsEntry.Open();
            var relsDoc = new XmlDocument();
            relsDoc.Load(relsStream);
            foreach (XmlNode rel in relsDoc.SelectNodes("//*[local-name()='Relationship']"))
            {
                string id = rel.Attributes?["Id"]?.Value;
                string target = rel.Attributes?["Target"]?.Value;
                if (id != null && target != null)
                {
                    relTargetById[id] = target;
                }
            }
        }

        var wbEntry = archive.GetEntry("xl/workbook.xml");
        if (wbEntry == null)
        {
            return;
        }

        using (var wbStream = wbEntry.Open())
        {
            var wbDoc = new XmlDocument();
            wbDoc.Load(wbStream);
            foreach (XmlNode sheetNode in wbDoc.SelectNodes("//*[local-name()='sheet']"))
            {
                string sheetName = sheetNode.Attributes?["name"]?.Value;
                string rid = null;
                if (sheetNode.Attributes != null)
                {
                    foreach (XmlAttribute a in sheetNode.Attributes)
                    {
                        if (a.LocalName == "id")
                        {
                            rid = a.Value;
                            break;
                        }
                    }
                }

                if (sheetName == null || rid == null || !relTargetById.TryGetValue(rid, out var target))
                {
                    continue;
                }

                // target 形如 worksheets/sheet1.xml,相对 xl/
                string entryPath = "xl/" + target.TrimStart('/');
                var wsEntry = archive.GetEntry(entryPath);
                if (wsEntry == null)
                {
                    continue;
                }

                using var wsStream = wsEntry.Open();
                List<List<string>> rows = ReadWorksheet(wsStream, sharedStrings);
                if (!_sheetDict.TryAdd(sheetName, rows))
                {
                    GD.PrintRich($"[color=Red]表名重复请修改!:[/color]{sheetName}");
                }
            }
        }
    }

    private static List<List<string>> ReadWorksheet(Stream stream, List<string> sharedStrings)
    {
        var doc = new XmlDocument();
        doc.Load(stream);

        // 行号 -> (列号 -> 值),用 SortedDictionary 保证按行序
        var rowMap = new SortedDictionary<int, Dictionary<int, string>>();
        int maxCol = -1;

        foreach (XmlNode rowNode in doc.SelectNodes("//*[local-name()='row']"))
        {
            string rAttr = rowNode.Attributes?["r"]?.Value;
            if (rAttr == null)
            {
                continue;
            }

            int rowIdx = ParseRowIndex(rAttr);
            var colMap = new Dictionary<int, string>();
            foreach (XmlNode c in rowNode.SelectNodes("*[local-name()='c']"))
            {
                string cref = c.Attributes?["r"]?.Value;
                if (cref == null)
                {
                    continue;
                }

                int colIdx = ParseColIndex(cref);
                string type = c.Attributes?["t"]?.Value;
                colMap[colIdx] = ReadCellValue(c, type, sharedStrings);
                if (colIdx > maxCol)
                {
                    maxCol = colIdx;
                }
            }

            rowMap[rowIdx] = colMap;
        }

        // 物化为连续 List<List<string>>(缺失单元格补空串)
        var result = new List<List<string>>();
        for (int r = 0; r < rowMap.Count; r++)
        {
            var line = new List<string>();
            for (int c = 0; c <= maxCol; c++)
            {
                line.Add(rowMap[r].TryGetValue(c, out var v) ? v : "");
            }
            result.Add(line);
        }
        return result;
    }

    private static string ReadCellValue(XmlNode cell, string type, List<string> sharedStrings)
    {
        if (type == "s")
        {
            // 共享字符串: <v> 为索引
            string v = ChildText(cell, "v");
            if (v != null && int.TryParse(v, out int idx) && idx >= 0 && idx < sharedStrings.Count)
            {
                return sharedStrings[idx];
            }
            return "";
        }

        if (type == "inlineStr")
        {
            // 内联字符串: <is><t>...</t></is>
            XmlNode t = cell.SelectSingleNode(".//*[local-name()='t']");
            return t?.InnerText ?? "";
        }

        if (type == "str")
        {
            // 公式缓存字符串
            return ChildText(cell, "v") ?? "";
        }

        if (type == "b")
        {
            // 布尔: <v>1</v> 或 <v>true</v>
            string v = ChildText(cell, "v");
            return v == "1" || v == "true" ? "1" : "0";
        }

        // 默认:数值或普通文本,取 <v>
        return ChildText(cell, "v") ?? "";
    }

    private static string ChildText(XmlNode node, string name)
    {
        foreach (XmlNode n in node.ChildNodes)
        {
            if (n.Name == name)
            {
                return n.InnerText;
            }
        }
        return null;
    }

    // 单元格引用 "AB12" -> 列索引(从0开始)
    private static int ParseColIndex(string cellRef)
    {
        int idx = 0;
        foreach (char c in cellRef)
        {
            if (c >= 'A' && c <= 'Z')
            {
                idx = idx * 26 + (c - 'A' + 1);
            }
            else if (c >= 'a' && c <= 'z')
            {
                idx = idx * 26 + (c - 'a' + 1);
            }
            else
            {
                break;
            }
        }
        return idx - 1;
    }

    // 单元格引用 "AB12" -> 行索引(从0开始)
    private static int ParseRowIndex(string cellRef)
    {
        int i = 0;
        while (i < cellRef.Length && !char.IsDigit(cellRef[i]))
        {
            i++;
        }
        return i < cellRef.Length ? int.Parse(cellRef.Substring(i)) - 1 : 0;
    }
}
#endif
