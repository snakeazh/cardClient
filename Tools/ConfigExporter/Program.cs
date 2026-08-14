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
            Console.WriteLine();

            var tables = ExcelToCsvConverter.ConvertDirectory(options.ConfigDir, options.CsvDir);
            Console.WriteLine();
            CsvToJsonConverter.ConvertDirectory(options.CsvDir, options.JsonDir);

            if (!string.IsNullOrEmpty(options.CsharpDir))
            {
                Console.WriteLine();
                CsharpGenerator.Generate(tables, options.CsharpDir);
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
            var dest = Path.Combine(unityJsonDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            Console.WriteLine($"[JSON→Unity] {Path.GetFileName(file)} → {dest}");
        }
    }

    private static ExportOptions ParseArgs(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var configDir = Path.Combine(repoRoot, "Config");
        var csvDir = Path.Combine(configDir, "csv");
        var jsonDir = Path.Combine(configDir, "json");
        var csharpDir = Path.Combine(repoRoot, "Card", "Assets", "App", "Config", "Generated");
        var unityJsonDir = Path.Combine(repoRoot, "Card", "Assets", "Res", "Config");

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
                    configDir = Path.GetFullPath(Next());
                    csvDir = Path.Combine(configDir, "csv");
                    jsonDir = Path.Combine(configDir, "json");
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

        return new ExportOptions(configDir, csvDir, jsonDir, csharpDir, unityJsonDir);
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
              -c, --config        配置目录（默认: 仓库/Config）
                  --csv           CSV 输出目录（默认: Config/csv）
                  --json          JSON 输出目录（默认: Config/json）
                  --csharp        C# 输出目录（默认: Card/Assets/App/Config/Generated）
                  --no-csharp     不生成 C#
                  --unity-json    Unity JSON 目录（默认: Card/Assets/Res/Config）
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
        string? UnityJsonDir);
}
