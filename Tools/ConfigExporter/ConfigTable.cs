namespace ConfigExporter;

public enum ConfigTableKind
{
    /// <summary>普通表：第1行字段名、第2行类型、第3行备注，其后为数据行。</summary>
    Data,

    /// <summary>常量表（文件名含 Const）：每行 字段名/类型/备注/数据。</summary>
    Const,

    /// <summary>枚举表 EnumConfig.xlsx：每行 枚举名/成员名/数值/备注。</summary>
    Enum
}

/// <summary>
/// 配置表内存模型。
/// </summary>
public sealed class ConfigTable
{
    public required string Name { get; init; }
    public required ConfigTableKind Kind { get; init; }

    /// <summary>普通表：横向字段名；常量表：各常量名。</summary>
    public required IReadOnlyList<string> FieldNames { get; init; }

    /// <summary>普通表：横向字段类型；常量表：各常量类型。</summary>
    public required IReadOnlyList<string> FieldTypes { get; init; }

    /// <summary>普通表：横向字段备注；常量表：各常量备注。</summary>
    public required IReadOnlyList<string> FieldComments { get; init; }

    /// <summary>普通表：数据行；常量表：每行仅含一个取值（与 FieldNames 对齐）。</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }

    public static bool IsConstName(string name) =>
        name.Contains("Const", StringComparison.OrdinalIgnoreCase);

    public static bool IsEnumName(string name) =>
        string.Equals(name, "EnumConfig", StringComparison.OrdinalIgnoreCase);
}

public sealed record EnumMemberDefinition(string Name, int Value, string Description);

public sealed record EnumDefinition(string Name, IReadOnlyList<EnumMemberDefinition> Members);

public static class EnumDefinitions
{
    public static IReadOnlyList<EnumDefinition> FromTable(ConfigTable? table)
    {
        if (table is null)
            return Array.Empty<EnumDefinition>();
        if (table.Kind != ConfigTableKind.Enum)
            throw new ArgumentException("配置表不是枚举表。", nameof(table));

        var result = new List<EnumDefinition>();
        foreach (var group in table.Rows.GroupBy(
                     row => row[0].Trim(),
                     StringComparer.OrdinalIgnoreCase))
        {
            var members = group.Select(row =>
            {
                if (!int.TryParse(row[2], out var value))
                    throw new FormatException($"[EnumConfig.{group.Key}.{row[1]}] Value 必须是 int: {row[2]}");
                return new EnumMemberDefinition(row[1].Trim(), value, row[3].Trim());
            }).ToList();
            result.Add(new EnumDefinition(group.First()[0].Trim(), members));
        }

        return result;
    }
}
