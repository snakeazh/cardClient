using CardShare.Contracts;

#nullable enable

namespace CardShare.Battle
{
    /// <summary>
    /// 局内机器人消耗品：主动使用持有的可用消耗物；
    /// 含回血/加生命的仅在已扣血（Hp &lt; MaxHp）时使用。
    /// </summary>
    public static class PvpBotConsumables
    {
        /// <summary>下一个该用的消耗品 Id；没有则 null。</summary>
        public static int? PickToUse(PvpFighter fighter, IGameTables tables)
        {
            if (fighter == null || tables == null || !fighter.Alive)
            {
                return null;
            }

            for (var i = 0; i < fighter.OwnedRelicIds.Count; i++)
            {
                var relicId = fighter.OwnedRelicIds[i];
                if (relicId <= 0 || !tables.TryGetRelic(relicId, out var relic))
                {
                    continue;
                }

                if (!PvpRelicRuntime.IsUsableConsumable(tables, relic))
                {
                    continue;
                }

                if (PvpRelicRuntime.ConsumableHeals(tables, relic) && fighter.Hp >= fighter.MaxHp)
                {
                    continue;
                }

                return relicId;
            }

            return null;
        }
    }
}
