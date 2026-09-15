namespace ConfigExporter;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            Console.WriteLine($"配置目录: {options.ConfigDir}");
            Console.WriteLine($"CSV 输出: {options.CsvDir}");
            Console.WriteLine($"JSON 输出: {options.JsonDir}");
            if (!string.IsNullOrEmpty(options.CsharpDir))
                Console.WriteLine($"C# 输出: {options.CsharpDir}");
            if (!string.IsNullOrEmpty(options.UnityJsonDir))
                Console.WriteLine($"Unity JSON: {options.UnityJsonDir}");
            if (!string.IsNullOrEmpty(options.ContractsDir))
                Console.WriteLine($"Contracts C#: {options.ContractsDir}");
            Console.WriteLine();

            var tables = ExcelToCsvConverter.ConvertDirectory(options.ConfigDir, options.CsvDir);
            var enumTable = tables.SingleOrDefault(table => table.Kind == ConfigTableKind.Enum);
            var enums = EnumDefinitions.FromTable(enumTable);
            Console.WriteLine();
            CsvToJsonConverter.ConvertDirectory(options.CsvDir, options.JsonDir, enums);

            if (!string.IsNullOrEmpty(options.CsharpDir))
            {
                Console.WriteLine();
                CsharpGenerator.Generate(tables, enums, options.CsharpDir);
            }

            if (!string.IsNullOrEmpty(options.ContractsDir))
            {
                Console.WriteLine();
                CsharpGenerator.GenerateShared(tables, enums, options.ContractsDir);
            }

            if (!string.IsNullOrEmpty(options.UnityJsonDir))
            {
                Console.WriteLine();
                CopyJsonToUnity(options.JsonDir, options.UnityJsonDir);
            }

            Console.WriteLine();
            Console.WriteLine("导出完成。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"导出失败: {ex.Message}");
            return 1;
        }
    }

    private static void CopyJsonToUnity(string jsonDir, string unityJsonDir)
    {
        Directory.CreateDirectory(unityJsonDir);
        var files = Directory.GetFiles(jsonDir, "*.json");
        if (files.Length == 0)
            throw new InvalidOperationException($"未找到 JSON: {jsonDir}");

        foreach (var file in files)
        {
            if (ConfigTable.IsEnumName(Path.GetFileNameWithoutExtension(file)))
                continue;

            var dest = Path.Combine(unityJsonDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            Console.WriteLine($"[JSON→Unity] {Path.GetFileName(file)} → {dest}");
        }
    }

    private static ExportOptions ParseArgs(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var configDir = Path.Combine(repoRoot, "Config");
        var tempConfigDir = Path.Combine(repoRoot, "TempConfig");
        var csvDir = Path.Combine(tempConfigDir, "csv");
        var jsonDir = Path.Combine(tempConfigDir, "json");
        var csharpDir = Path.Combine(repoRoot, "Card", "Assets", "App", "Config", "Generated");
        var unityJsonDir = Path.Combine(repoRoot, "Card", "Assets", "Res", "Config");
        var contractsDir = Path.Combine(repoRoot, "Server", "src", "Card.Contracts", "Config");

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException($"参数 {arg} 缺少值");
                return args[++i];
            }

            switch (arg)
            {
                case "--config":
                case "-c":
                    // 只改 Excel 源目录；中间产物仍默认落在 TempConfig
                    configDir = Path.GetFullPath(Next());
                    break;
                case "--csv":
                    csvDir = Path.GetFullPath(Next());
                    break;
                case "--json":
                    jsonDir = Path.GetFullPath(Next());
                    break;
                case "--csharp":
                    csharpDir = Path.GetFullPath(Next());
                    break;
                case "--no-csharp":
                    csharpDir = null!;
                    break;
                case "--unity-json":
                    unityJsonDir = Path.GetFullPath(Next());
                    break;
                case "--no-unity-json":
                    unityJsonDir = null!;
                    break;
                case "--contracts":
                    contractsDir = Path.GetFullPath(Next());
                    break;
                case "--no-contracts":
                    contractsDir = null!;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"未知参数: {arg}");
            }
        }

        if (!Directory.Exists(configDir))
            throw new DirectoryNotFoundException($"配置目录不存在: {configDir}");

        return new ExportOptions(configDir, csvDir, jsonDir, csharpDir, unityJsonDir, contractsDir);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var configPath = Path.Combine(dir.FullName, "Config");
            if (Directory.Exists(configPath) &&
                Directory.EnumerateFiles(configPath, "*.xlsx").Any(f => !Path.GetFileName(f).StartsWith("~$")))
                return dir.FullName;
            dir = dir.Parent;
        }

        var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        if (Directory.Exists(Path.Combine(fallback, "Config")))
            return fallback;

        return Directory.GetCurrentDirectory();
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            ConfigExporter - Excel 配置导出工具
            流程: xlsx → csv → json → C# / Unity

            用法:
              dotnet run --project Tools/ConfigExporter
              dotnet run --project Tools/ConfigExporter -- --config <目录>

            参数:
              -c, --config        配置目录（默认: 仓库/Config，Excel 源）
                  --csv           CSV 输出目录（默认: TempConfig/csv）
                  --json          JSON 输出目录（默认: TempConfig/json）
                  --csharp        C# 输出目录（默认: Card/Assets/App/Config/Generated）
                  --no-csharp     不生成 C#
                  --contracts     共享配表 class 目录（默认: Server/src/Card.Contracts/Config）
                  --no-contracts  不生成共享配表 class
                  --no-unity-json 不复制 JSON 到 Unity
              -h, --help          显示帮助

            Excel 约定:
              [普通表] 文件名不含 Const
                第1行 字段名
                第2行 字段类型（int/string/bool/float/int[]/...）
                第3行 备注
                其后非空行为数据
                以 # 开头的列（如 ##var）会被跳过

              [常量表] 文件名含 Const（如 GameConst.xlsx）
                第1列 字段名
                第2列 类型
                第3列 备注
                第4列 数据
                可有表头行；以 # 开头的行会跳过
                JSON 导出为 { "字段名": 值, ... }

              [枚举表] 文件名必须为 EnumConfig.xlsx
                第1列 Enum：枚举类型名
                第2列 Name：枚举成员名
                第3列 Value：int 数值
                第4列 Desc：成员备注（生成 C# XML 注释）
                配置字段类型可写 ItemType 或 ItemType[]

              [类型]
                标量: int/long/float/double/bool/string
                数组: int[]/long[]/float[]/double[]/bool[]/string[]
                数组单元格用 | 分隔，如 1|2|3；空单元格导出 []
            """);
    }

    private sealed record ExportOptions(
        string ConfigDir,
        string CsvDir,
        string JsonDir,
        string? CsharpDir,
        string? UnityJsonDir,
        string? ContractsDir);
}
