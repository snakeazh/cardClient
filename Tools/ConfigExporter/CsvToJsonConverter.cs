using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConfigExporter;

public static class CsvToJsonConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 将目录下所有 CSV 转为 JSON。
    /// </summary>
    public static IReadOnlyList<string> ConvertDirectory(string csvDir, string jsonDir)
    {
        Directory.CreateDirectory(jsonDir);

        var csvFiles = Directory.GetFiles(csvDir, "*.csv")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (csvFiles.Count == 0)
            throw new InvalidOperationException($"未找到 csv 文件: {csvDir}");

        var outputs = new List<string>();
        foreach (var csvPath in csvFiles)
        {
            var table = ReadCsv(csvPath);
            var jsonPath = Path.Combine(jsonDir, $"{table.Name}.json");
            WriteJson(table, jsonPath);
            outputs.Add(jsonPath);
            var kindTag = table.Kind == ConfigTableKind.Const ? "Const" : "Data";
            Console.WriteLine($"[CSV→JSON][{kindTag}] {Path.GetFileName(csvPath)} → {jsonPath}");
        }

        return outputs;
    }

    public static ConfigTable ReadCsv(string csvPath)
    {
        var name = Path.GetFileNameWithoutExtension(csvPath);
        var lines = File.ReadAllLines(csvPath, Encoding.UTF8)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (ConfigTable.IsConstName(name))
            return ReadConstCsv(name, lines, csvPath);

        return ReadDataCsv(name, lines, csvPath);
    }

    private static ConfigTable ReadDataCsv(string name, List<string> lines, string csvPath)
    {
        if (lines.Count < 3)
            throw new InvalidOperationException($"CSV 表头不足 3 行: {csvPath}");

        var fieldNames = ParseCsvLine(lines[0]);
        var fieldTypes = ParseCsvLine(lines[1]);
        var fieldComments = ParseCsvLine(lines[2]);

        if (fieldNames.Count == 0)
            throw new InvalidOperationException($"CSV 无字段: {csvPath}");

        while (fieldTypes.Count < fieldNames.Count) fieldTypes.Add("string");
        while (fieldComments.Count < fieldNames.Count) fieldComments.Add(string.Empty);

        var rows = new List<IReadOnlyList<string>>();
        for (var i = 3; i < lines.Count; i++)
        {
            var values = ParseCsvLine(lines[i]);
            while (values.Count < fieldNames.Count) values.Add(string.Empty);
            if (values.Take(fieldNames.Count).All(string.IsNullOrWhiteSpace))
                continue;
            rows.Add(values.Take(fieldNames.Count).ToList());
        }

        return new ConfigTable
        {
            Name = name,
            Kind = ConfigTableKind.Data,
            FieldNames = fieldNames,
            FieldTypes = fieldTypes,
            FieldComments = fieldComments,
            Rows = rows
        };
    }

    private static ConfigTable ReadConstCsv(string name, List<string> lines, string csvPath)
    {
        if (lines.Count == 0)
            throw new InvalidOperationException($"常量表 CSV 为空: {csvPath}");

        var start = 0;
        var header = ParseCsvLine(lines[0]);
        if (header.Count >= 1 &&
            (string.Equals(header[0], "Name", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(header[0], "字段名", StringComparison.OrdinalIgnoreCase)))
        {
            start = 1;
        }

        var fieldNames = new List<string>();
        var fieldTypes = new List<string>();
        var fieldComments = new List<string>();
        var values = new List<string>();

        for (var i = start; i < lines.Count; i++)
        {
            var cols = ParseCsvLine(lines[i]);
            while (cols.Count < 4) cols.Add(string.Empty);

            var field = cols[0].Trim();
            if (string.IsNullOrWhiteSpace(field) || field.StartsWith('#'))
                continue;

            fieldNames.Add(field);
            fieldTypes.Add(string.IsNullOrWhiteSpace(cols[1]) ? "string" : cols[1].Trim());
            fieldComments.Add(cols[2]);
            values.Add(cols[3]);
        }

        if (fieldNames.Count == 0)
            throw new InvalidOperationException($"常量表 CSV 无有效数据: {csvPath}");

        return new ConfigTable
        {
            Name = name,
            Kind = ConfigTableKind.Const,
            FieldNames = fieldNames,
            FieldTypes = fieldTypes,
            FieldComments = fieldComments,
            Rows = new List<IReadOnlyList<string>> { values }
        };
    }

    public static void WriteJson(ConfigTable table, string jsonPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

        var json = table.Kind == ConfigTableKind.Const
            ? WriteConstJson(table)
            : WriteDataJson(table);

        File.WriteAllText(jsonPath, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static string WriteDataJson(ConfigTable table)
    {
        var array = new JsonArray();
        foreach (var row in table.Rows)
        {
            var obj = new JsonObject();
            for (var i = 0; i < table.FieldNames.Count; i++)
            {
                var name = table.FieldNames[i];
                var type = i < table.FieldTypes.Count ? table.FieldTypes[i] : "string";
                var raw = i < row.Count ? row[i] : string.Empty;
                obj[name] = ConvertField(table.Name, name, raw, type);
            }

            array.Add(obj);
        }

        return array.ToJsonString(JsonOptions);
    }

    private static string WriteConstJson(ConfigTable table)
    {
        var obj = new JsonObject();
        var values = table.Rows.Count > 0 ? table.Rows[0] : Array.Empty<string>();
        for (var i = 0; i < table.FieldNames.Count; i++)
        {
            var name = table.FieldNames[i];
            var type = i < table.FieldTypes.Count ? table.FieldTypes[i] : "string";
            var raw = i < values.Count ? values[i] : string.Empty;
            obj[name] = ConvertField(table.Name, name, raw, type);
        }

        return obj.ToJsonString(JsonOptions);
    }

    private static JsonNode? ConvertField(string tableName, string fieldName, string raw, string type)
    {
        try
        {
            return ConvertValue(raw, type);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"[{tableName}.{fieldName}] {ex.Message}");
        }
    }

    private static JsonNode? ConvertValue(string raw, string type)
    {
        var t = type.Trim().ToLowerInvariant();

        // 基础类型数组：int[] / string[] / bool[] ...，单元格用 | 分隔
        if (t.EndsWith("[]", StringComparison.Ordinal))
        {
            var elementType = t[..^2].Trim();
            if (string.IsNullOrWhiteSpace(elementType))
                throw new FormatException($"无效数组类型: {type}");

            var array = new JsonArray();
            if (string.IsNullOrWhiteSpace(raw))
                return array;

            var parts = raw.Split('|', StringSplitOptions.None);
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                try
                {
                    array.Add(ConvertScalar(part, elementType, allowEmptyAsDefault: false));
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"数组第 {i + 1} 个元素解析失败（类型 {elementType}[]）: {ex.Message}");
                }
            }

            return array;
        }

        return ConvertScalar(raw, t, allowEmptyAsDefault: true);
    }

    private static JsonNode? ConvertScalar(string raw, string type, bool allowEmptyAsDefault)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (!allowEmptyAsDefault)
                throw new FormatException("数组元素不能为空");

            return type switch
            {
                "int" or "int32" or "long" or "int64" or "float" or "single" or "double" or "number" => 0,
                "bool" or "boolean" => false,
                _ => string.Empty
            };
        }

        return type switch
        {
            "int" or "int32" => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                ? i
                : throw new FormatException($"无法解析 int: {raw}"),
            "long" or "int64" => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)
                ? l
                : throw new FormatException($"无法解析 long: {raw}"),
            "float" or "single" => float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? f
                : throw new FormatException($"无法解析 float: {raw}"),
            "double" or "number" => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : throw new FormatException($"无法解析 double: {raw}"),
            "bool" or "boolean" => ParseBool(raw),
            "string" => raw,
            _ => raw
        };
    }

    private static bool ParseBool(string raw)
    {
        if (bool.TryParse(raw, out var b))
            return b;
        if (raw is "1" or "是" or "Y" or "y")
            return true;
        if (raw is "0" or "否" or "N" or "n")
            return false;
        throw new FormatException($"无法解析 bool: {raw}");
    }

    public static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                    inQuotes = true;
                else if (ch == ',')
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }

        result.Add(sb.ToString());
        return result;
    }
}
