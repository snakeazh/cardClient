using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConfigExporter;

public static class CsvToJsonConverter
{
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> _enumValues =
        new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 将目录下所有 CSV 转为 JSON。
    /// </summary>
    public static IReadOnlyList<string> ConvertDirectory(
        string csvDir,
        string jsonDir,
        IReadOnlyList<EnumDefinition>? enums = null)
    {
        Directory.CreateDirectory(jsonDir);
        _enumValues = BuildEnumLookup(enums ?? Array.Empty<EnumDefinition>());

        var csvFiles = Directory.GetFiles(csvDir, "*.csv")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (csvFiles.Count == 0)
            throw new InvalidOperationException($"未找到 csv 文件: {csvDir}");

        var outputs = new List<string>();
        foreach (var csvPath in csvFiles)
        {
            var table = ReadCsv(csvPath);
            if (table.Kind == ConfigTableKind.Enum)
            {
                var staleJsonPath = Path.Combine(jsonDir, $"{table.Name}.json");
                if (File.Exists(staleJsonPath))
                    File.Delete(staleJsonPath);
                Console.WriteLine($"[CSV→JSON][Enum] {Path.GetFileName(csvPath)} → 仅生成 C#，不生成 JSON");
                continue;
            }

            var jsonPath = Path.Combine(jsonDir, $"{table.Name}.json");
            WriteJson(table, jsonPath);
            outputs.Add(jsonPath);
            var kindTag = table.Kind.ToString();
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

        if (ConfigTable.IsEnumName(name))
            return ReadEnumCsv(name, lines, csvPath);

        if (ConfigTable.IsConstName(name))
            return ReadConstCsv(name, lines, csvPath);

        return ReadDataCsv(name, lines, csvPath);
    }

    private static ConfigTable ReadEnumCsv(string name, List<string> lines, string csvPath)
    {
        if (lines.Count < 2)
            throw new InvalidOperationException($"枚举表 CSV 无有效数据: {csvPath}");

        var rows = new List<IReadOnlyList<string>>();
        for (var i = 1; i < lines.Count; i++)
        {
            var values = ParseCsvLine(lines[i]);
            while (values.Count < 4) values.Add(string.Empty);
            if (values.Take(4).All(string.IsNullOrWhiteSpace))
                continue;
            rows.Add(values.Take(4).ToList());
        }

        return new ConfigTable
        {
            Name = name,
            Kind = ConfigTableKind.Enum,
            FieldNames = new[] { "Enum", "Name", "Value", "Desc" },
            FieldTypes = new[] { "string", "string", "int", "string" },
            FieldComments = new[] { "枚举类型", "枚举成员", "枚举数值", "备注" },
            Rows = rows
        };
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
        if (table.Kind == ConfigTableKind.Enum)
            throw new InvalidOperationException("EnumConfig 仅用于生成 C# 枚举，不生成运行时 JSON。");

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
        var t = type.Trim();

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
        var normalizedType = type.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (!allowEmptyAsDefault)
                throw new FormatException("数组元素不能为空");

            return normalizedType switch
            {
                "int" or "int32" or "long" or "int64" or "float" or "single" or "double" or "number" => 0,
                "bool" or "boolean" => false,
                "string" => string.Empty,
                _ when _enumValues.TryGetValue(type, out var enumMembers) &&
                       enumMembers.Values.Contains(0) => 0,
                _ when _enumValues.ContainsKey(type) =>
                    throw new FormatException($"枚举 {type} 不能为空（未定义数值 0）"),
                _ => string.Empty
            };
        }

        return normalizedType switch
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
            _ => ParseEnum(raw, type)
        };
    }

    private static int ParseEnum(string raw, string type)
    {
        if (!_enumValues.TryGetValue(type, out var members))
            throw new FormatException($"不支持的类型: {type}，请在 EnumConfig.xlsx 中定义该枚举");

        if (members.TryGetValue(raw, out var value))
            return value;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
            members.Values.Contains(value))
            return value;

        throw new FormatException($"枚举 {type} 不存在成员或数值: {raw}");
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> BuildEnumLookup(
        IReadOnlyList<EnumDefinition> enums)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var definition in enums)
        {
            var members = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in definition.Members)
            {
                if (!members.TryAdd(member.Name, member.Value))
                    throw new FormatException($"枚举 {definition.Name} 成员重复: {member.Name}");
            }

            if (!result.TryAdd(definition.Name, members))
                throw new FormatException($"枚举类型重复: {definition.Name}");
        }

        return result;
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
