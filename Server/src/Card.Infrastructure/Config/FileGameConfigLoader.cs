using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using CardShare.Domain.Config;

namespace CardShare.Infrastructure.Config;

public sealed class FileGameConfigLoader : IGameConfigLoader
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _directory;

    public FileGameConfigLoader(string directory)
    {
        _directory = directory;
    }

    public IGameTables Load()
    {
        if (string.IsNullOrWhiteSpace(_directory) || !Directory.Exists(_directory))
        {
            return GameTables.Fallback();
        }

        var gameConst = ReadObject<GameConst>("GameConst.json") ?? GameTables.Fallback().GameConst;
        GameConst.Load(gameConst);
        var tables = new GameTables(
            gameConst,
            ReadArray<HandScoreConfig>("HandScoreConfig.json"),
            ReadArray<LevelConfig>("LevelConfig.json"),
            ReadArray<HeroConfig>("HeroConfig.json"),
            ReadArray<RelicConfig>("RelicConfig.json"),
            ReadArray<UnlockConditionConfig>("UnlockConditionConfig.json"),
            ReadArray<TalentConfig>("TalentConfig.json"),
            ReadArray<MonsterConfig>("MonsterConfig.json"),
            ReadArray<ItemConfig>("ItemConfig.json"),
            HashLoaded(),
            ReadArray<HeroEntryConfig>("HeroEntryConfig.json"),
            ReadArray<TalentEntryConfig>("TalentEntryConfig.json"),
            ReadArray<RelicEntryConfig>("RelicEntryConfig.json"),
            ReadArray<MonsterGroupConfig>("MonsterGroupConfig.json"),
            ReadArray<PvpModeConfig>("PvpModeConfig.json"),
            ReadArray<PvpRoundConfig>("PvpRoundConfig.json"));
        if (tables.Levels.Count == 0)
        {
            return GameTables.Fallback();
        }

        return tables;
    }

    private T? ReadObject<T>(string fileName) where T : class
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
    }

    private IReadOnlyList<T> ReadArray<T>(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        if (!File.Exists(path))
        {
            return Array.Empty<T>();
        }

        return JsonSerializer.Deserialize<T[]>(File.ReadAllText(path), Json) ?? Array.Empty<T>();
    }

    private string HashLoaded()
    {
        var names = new[]
        {
            "GameConst.json", "HandScoreConfig.json", "LevelConfig.json", "HeroConfig.json",
            "HeroEntryConfig.json", "RelicConfig.json", "RelicEntryConfig.json", "UnlockConditionConfig.json",
            "TalentConfig.json", "TalentEntryConfig.json", "MonsterConfig.json",
            "MonsterGroupConfig.json", "PvpModeConfig.json", "PvpRoundConfig.json"
        };
        using var sha = SHA256.Create();
        foreach (var name in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            var path = Path.Combine(_directory, name);
            var bytes = Encoding.UTF8.GetBytes(name);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            if (File.Exists(path))
            {
                var body = File.ReadAllBytes(path);
                sha.TransformBlock(body, 0, body.Length, null, 0);
            }
        }

        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return BitConverter.ToString(sha.Hash!).Replace("-", string.Empty);
    }
}

public sealed class SharedGameConfig : IGameConfig
{
    public SharedGameConfig(IGameTables tables, TimeZoneInfo timeZone)
    {
        Tables = tables;
        TimeZone = timeZone;
        Balance = GameBalance.From(Tables.GameConst);
    }

    public IGameTables Tables { get; }

    public TimeZoneInfo TimeZone { get; }

    public GameBalance Balance { get; }

    public static SharedGameConfig Fallback()
        => Fallback(TimeZoneInfo.Utc);

    public static SharedGameConfig Fallback(TimeZoneInfo zone)
        => new SharedGameConfig(GameTables.Fallback(), zone);
}
