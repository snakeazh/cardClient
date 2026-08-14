namespace ConfigExporter;

public enum ConfigTableKind
{
    /// <summary>普通表：第1行字段名、第2行类型、第3行备注，其后为数据行。</summary>
    Data,

    /// <summary>常量表（文件名含 Const）：每行 字段名/类型/备注/数据。</summary>
    Const
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
}
