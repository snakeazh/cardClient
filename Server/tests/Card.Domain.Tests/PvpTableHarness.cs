using System.Collections.Generic;
using CardShare.Contracts.Config;

namespace CardShare.Domain.Tests;

/// <summary>测试夹具：把 Relic 行灌进 PvpRelic 通道（生产不回落 RelicConfig）。</summary>
internal static class PvpTableHarness
{
    public static PvpRelicConfig[] Relics(IReadOnlyList<RelicConfig> relics)
    {
        var list = new List<PvpRelicConfig>(relics.Count);
        for (var i = 0; i < relics.Count; i++)
        {
            var src = relics[i];
            if (src == null || src.Id <= 0)
            {
                continue;
            }

            list.Add(new PvpRelicConfig
            {
                Id = src.Id,
                Type = src.Type,
                RefreshProbability = src.RefreshProbability,
                Name = src.Name,
                Icon = src.Icon,
                Price = src.Price,
                SellingPrice = src.SellingPrice,
                MechanismId = src.MechanismId,
                BindingID = src.BindingID,
                UseType = src.UseType,
                SpecialDesc = src.SpecialDesc,
                Desc = src.Desc
            });
        }

        return list.ToArray();
    }

    public static PvpRelicEntryConfig[] Entries(IReadOnlyList<RelicEntryConfig> entries)
    {
        var list = new List<PvpRelicEntryConfig>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var src = entries[i];
            if (src == null || src.Id <= 0)
            {
                continue;
            }

            list.Add(new PvpRelicEntryConfig
            {
                Id = src.Id,
                Name = src.Name,
                Type = src.Type,
                Value = src.Value,
                Desc = src.Desc
            });
        }

        return list.ToArray();
    }
}
