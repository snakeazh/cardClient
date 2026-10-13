using System.Collections.Generic;
using CardShare.Contracts.Config;
using UnityEngine;

namespace App.Config
{
    /// <summary>
    /// <see cref="DialogueGroupConfig"/> 按 MonsterId 抽句。表按行 Id 索引，同一怪物有多条。
    /// </summary>
    public static class DialogueGroupLookup
    {
        private static readonly List<DialogueGroupConfig> Matches = new List<DialogueGroupConfig>(8);

        public static DialogueGroupConfig PickByMonster(int monsterId)
        {
            Matches.Clear();
            if (monsterId <= 0)
            {
                return null;
            }

            foreach (var pair in DialogueGroupConfig.All)
            {
                var row = pair.Value;
                if (row != null && row.MonsterId == monsterId)
                {
                    Matches.Add(row);
                }
            }

            if (Matches.Count == 0)
            {
                return null;
            }

            var rowPick = Matches[Random.Range(0, Matches.Count)];
            Matches.Clear();
            return rowPick;
        }

        public static bool TryPickTalk(int monsterId, out string monsterTalk, out string playerTalk)
        {
            var row = PickByMonster(monsterId);
            monsterTalk = row != null ? row.MonsterTalk : null;
            playerTalk = row != null ? row.PlayerTalk : null;
            return row != null;
        }
    }
}
