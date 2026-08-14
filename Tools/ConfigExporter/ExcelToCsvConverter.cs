using ClosedXML.Excel;

namespace ConfigExporter;

public static class ExcelToCsvConverter
{
    /// <summary>
    /// 将目录下所有 .xlsx 转为 CSV（跳过临时锁文件 ~$）。
    /// </summary>
    public static IReadOnlyList<ConfigTable> ConvertDirectory(string excelDir, string csvDir)
    {
        Directory.CreateDirectory(csvDir);

        var excelFiles = Directory.GetFiles(excelDir, "*.xlsx")
            .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (excelFiles.Count == 0)
            throw new InvalidOperationException($"未找到 xlsx 文件: {excelDir}");

        var tables = new List<ConfigTable>();
        foreach (var excelPath in excelFiles)
        {
            var table = ReadExcel(excelPath);
            var csvPath = Path.Combine(csvDir, $"{table.Name}.csv");
            WriteCsv(table, csvPath);
            tables.Add(table);
            var kindTag = table.Kind == ConfigTableKind.Const ? "Const" : "Data";
            Console.WriteLine($"[Excel→CSV][{kindTag}] {Path.GetFileName(excelPath)} → {csvPath}");
        }

        return tables;
    }

    public static ConfigTable ReadExcel(string excelPath)
    {
        var name = Path.GetFileNameWithoutExtension(excelPath);
        return ConfigTable.IsConstName(name)
            ? ReadConstExcel(excelPath, name)
            : ReadDataExcel(excelPath, name);
    }

    private static ConfigTable ReadDataExcel(string excelPath, string name)
    {
        using var stream = OpenReadShared(excelPath);
        using var workbook = new XLWorkbook(stream);
        var sheet = GetSheet(workbook);

        var used = sheet.RangeUsed()
            ?? throw new InvalidOperationException($"工作表为空: {excelPath} / {sheet.Name}");

        var rowCount = used.RowCount();
        var colCount = used.ColumnCount();
        if (rowCount < 3)
            throw new InvalidOperationException($"表头不足 3 行: {excelPath}");

        var columns = new List<int>();
        for (var col = 1; col <= colCount; col++)
        {
            var field = GetCellText(sheet.Cell(1, col));
            if (string.IsNullOrWhiteSpace(field))
                continue;
            if (IsMetaName(field))
                continue;
            columns.Add(col);
        }

        if (columns.Count == 0)
            throw new InvalidOperationException($"没有可导出字段: {excelPath}");

        var fieldNames = columns.Select(c => GetCellText(sheet.Cell(1, c))).ToList();
        var fieldTypes = columns.Select(c => GetCellText(sheet.Cell(2, c))).ToList();
        var fieldComments = columns.Select(c => GetCellText(sheet.Cell(3, c))).ToList();

        var rows = new List<IReadOnlyList<string>>();
        for (var row = 4; row <= rowCount; row++)
        {
            var values = columns.Select(c => GetCellText(sheet.Cell(row, c))).ToList();
            if (values.All(string.IsNullOrWhiteSpace))
                continue;
            rows.Add(values);
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

    private static ConfigTable ReadConstExcel(string excelPath, string name)
    {
        using var stream = OpenReadShared(excelPath);
        using var workbook = new XLWorkbook(stream);
        var sheet = GetSheet(workbook);

        var used = sheet.RangeUsed()
            ?? throw new InvalidOperationException($"工作表为空: {excelPath} / {sheet.Name}");

        var rowCount = used.RowCount();
        var fieldNames = new List<string>();
        var fieldTypes = new List<string>();
        var fieldComments = new List<string>();
        var values = new List<string>();

        for (var row = 1; row <= rowCount; row++)
        {
            var field = GetCellText(sheet.Cell(row, 1));
            var type = GetCellText(sheet.Cell(row, 2));
            var comment = GetCellText(sheet.Cell(row, 3));
            var value = GetCellText(sheet.Cell(row, 4));

            if (string.IsNullOrWhiteSpace(field) &&
                string.IsNullOrWhiteSpace(type) &&
                string.IsNullOrWhiteSpace(comment) &&
                string.IsNullOrWhiteSpace(value))
                continue;

            if (IsMetaName(field) || IsHeaderRow(field, type))
                continue;

            if (string.IsNullOrWhiteSpace(field))
                throw new InvalidOperationException($"常量表第 {row} 行字段名为空: {excelPath}");

            fieldNames.Add(field);
            fieldTypes.Add(string.IsNullOrWhiteSpace(type) ? "string" : type);
            fieldComments.Add(comment);
            values.Add(value);
        }

        if (fieldNames.Count == 0)
            throw new InvalidOperationException($"常量表无有效数据: {excelPath}");

        // 常量表 Rows 存一行，与 FieldNames 一一对应
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

    public static void WriteCsv(ConfigTable table, string csvPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

        using var writer = new StreamWriter(csvPath, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        if (table.Kind == ConfigTableKind.Const)
        {
            // 常量表 CSV：字段名,类型,备注,数据
            writer.WriteLine("Name,Type,Comment,Value");
            var values = table.Rows.Count > 0 ? table.Rows[0] : Array.Empty<string>();
            for (var i = 0; i < table.FieldNames.Count; i++)
            {
                var value = i < values.Count ? values[i] : string.Empty;
                writer.WriteLine(JoinCsv(new[]
                {
                    table.FieldNames[i],
                    i < table.FieldTypes.Count ? table.FieldTypes[i] : "string",
                    i < table.FieldComments.Count ? table.FieldComments[i] : string.Empty,
                    value
                }));
            }

            return;
        }

        writer.WriteLine(JoinCsv(table.FieldNames));
        writer.WriteLine(JoinCsv(table.FieldTypes));
        writer.WriteLine(JoinCsv(table.FieldComments));
        foreach (var row in table.Rows)
            writer.WriteLine(JoinCsv(row));
    }

    private static FileStream OpenReadShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    private static IXLWorksheet GetSheet(XLWorkbook workbook) =>
        workbook.Worksheets.FirstOrDefault(ws =>
            string.Equals(ws.Name, "data", StringComparison.OrdinalIgnoreCase))
        ?? workbook.Worksheets.First();

    private static bool IsMetaName(string name) =>
        name.StartsWith('#');

    private static bool IsHeaderRow(string field, string type)
    {
        static bool Eq(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // 兼容 GameConst 类表头：##var#column / 程序中的字段名称 / 类型 ...
        if (Eq(field, "Name") ||
            Eq(field, "字段名") ||
            Eq(field, "程序中的字段名称") ||
            Eq(field, "##var") ||
            Eq(field, "##var#column"))
            return true;

        if (Eq(type, "Type") ||
            Eq(type, "类型") ||
            Eq(type, "##") ||
            Eq(type, "##type"))
            return true;

        return false;
    }

    private static string GetCellText(IXLCell cell)
    {
        if (cell.IsEmpty())
            return string.Empty;

        if (cell.DataType == XLDataType.Number)
            return cell.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);

        return cell.GetFormattedString().Trim();
    }

    private static string JoinCsv(IEnumerable<string> values) =>
        string.Join(",", values.Select(EscapeCsv));

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\r') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
