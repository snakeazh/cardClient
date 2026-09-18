using System;
using System.Collections.Generic;
using CardShare.Contracts;
using CardShare.Contracts.Config;

namespace CardShare.Battle;

public sealed class PvpFighter
{
    public int SeatIndex { get; init; }

    public string UserId { get; init; } = string.Empty;

    public string NickName { get; init; } = string.Empty;

    public SeatSetup Combat { get; init; } = new SeatSetup();

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int Gold { get; set; }

    public bool Alive { get; set; } = true;

    public int Rank { get; set; }

    public int RubLeft { get; set; }

    public int ReplaceLeft { get; set; }

    public int PeekLeft { get; set; }

    public bool IsBot { get; init; }
}

public sealed class PvpMatch
{
    public const string PhaseFight = "fight";
    public const string PhaseFinished = "finished";

    private readonly IGameTables _tables;
    private readonly PvpModeConfig _mode;
    private readonly IReadOnlyList<PvpRoundConfig> _rounds;
    private readonly PvpFighter[] _fighters;
    private readonly List<PvpDuelTable> _duels = new List<PvpDuelTable>();
    private readonly object _gate = new object();
    private int _pvpCycle;
    private PvpRoundConfig _row = null!;
    private PvpFightKind _kind;

    private PvpMatch(
        Guid roomId,
        int seed,
        IReadOnlyList<PlayerPublic> players,
        IGameTables tables,
        PvpModeConfig mode,
        IReadOnlyList<PvpRoundConfig> rounds,
        PvpFighter[] fighters)
    {
        RoomId = roomId;
        Seed = seed;
        Players = players;
        _tables = tables;
        _mode = mode;
        _rounds = rounds;
        _fighters = fighters;
        ModeId = mode.Id;
        Round = 1;
        Phase = PhaseFight;
    }

    public Guid RoomId { get; }

    public int Seed { get; }

    public int ModeId { get; }

    public string ModeName => _mode.Name ?? string.Empty;

    public int Round { get; private set; }

    public string Phase { get; private set; }

    public PvpFightKind FightKind => _kind;

    public IReadOnlyList<PlayerPublic> Players { get; }

    public IReadOnlyList<PvpFighter> Fighters => _fighters;

    public IReadOnlyList<PvpDuelTable> Duels => _duels;

    public static PvpMatch Open(
        Guid roomId,
        int seed,
        IReadOnlyList<PlayerPublic> players,
        IGameTables tables,
        IReadOnlyList<SeatSetup> combatSeats,
        int modeId = PvpSchedule.DefaultModeId)
    {
        if (!PvpSchedule.TryLoad(tables, modeId, out var mode, out var rounds))
        {
            throw new InvalidOperationException($"PvpModeConfig {modeId} not found.");
        }

        var fighters = new PvpFighter[players.Count];
        for (var i = 0; i < players.Count; i++)
        {
            var pub = players[i];
            var combat = i < combatSeats.Count ? combatSeats[i] : new SeatSetup
            {
                SeatId = i,
                UserId = pub.UserId,
                NickName = pub.NickName,
                IsHuman = !pub.IsBot,
                Alive = true,
                Hp = 1,
                MaxHp = 1,
                Attack = 1
            };
            var hp = combat.MaxHp > 0 ? combat.MaxHp : Math.Max(1, combat.Hp);
            fighters[i] = new PvpFighter
            {
                SeatIndex = i,
                UserId = pub.UserId,
                NickName = pub.NickName,
                Combat = combat,
                Hp = hp,
                MaxHp = hp,
                Gold = mode.InitialGold,
                Alive = true,
                IsBot = pub.IsBot
            };
        }

        var match = new PvpMatch(roomId, seed, players, tables, mode, rounds, fighters);
        match.StartRound();
        return match;
    }

    public int SeatOf(string userId)
    {
        for (var i = 0; i < _fighters.Length; i++)
        {
            if (PvpBattleTable.SameUser(_fighters[i].UserId, userId))
            {
                return i;
            }
        }

        return -1;
    }

    public void Showdown(string userId)
    {
        Act(userId, "showdown", 0, Array.Empty<int>());
        AdvanceIfReady();
    }

    public void AdvanceIfReady()
    {
        lock (_gate)
        {
            TryAdvanceRound();
        }
    }

    public void Act(string userId, string action, int index, int[] indexes)
    {
        lock (_gate)
        {
            if (Phase != PhaseFight)
            {
                throw new InvalidOperationException("Match is not in a fight.");
            }

            var duel = FindDuel(userId);
            if (duel == null)
            {
                throw new InvalidOperationException("Not in a duel.");
            }

            var fighter = FighterOf(userId);
            var seat = duel.ViewerSeat(userId);
            switch (action)
            {
                case "pick":
                    duel.Pick(seat, indexes ?? Array.Empty<int>());
                    break;
                case "rub":
                    if (fighter.RubLeft <= 0)
                    {
                        throw new InvalidOperationException("No rub left.");
                    }

                    duel.Rub(seat, index);
                    fighter.RubLeft--;
                    break;
                case "replace":
                    if (fighter.ReplaceLeft <= 0)
                    {
                        throw new InvalidOperationException("No replace left.");
                    }

                    duel.Replace(seat);
                    fighter.ReplaceLeft--;
                    break;
                case "peek":
                    if (fighter.PeekLeft <= 0)
                    {
                        throw new InvalidOperationException("No peek left.");
                    }

                    duel.Peek(seat);
                    fighter.PeekLeft--;
                    break;
                case "showdown":
                case "open":
                    duel.LockSeat(seat);
                    SettleResolvedDuels();
                    break;
                default:
                    throw new InvalidOperationException("Unknown battle action.");
            }
        }
    }

    public PvpMatchStateDto ViewFor(string userId)
    {
        lock (_gate)
        {
            PvpDuelTable? own = null;
            var summaries = new PvpDuelSummaryDto[_duels.Count];
            for (var i = 0; i < _duels.Count; i++)
            {
                var duel = _duels[i];
                summaries[i] = new PvpDuelSummaryDto
                {
                    LeftUserId = duel.LeftUserId,
                    RightUserId = duel.RightUserId,
                    MonsterName = duel.MonsterName,
                    Resolved = duel.Resolved
                };
                if (duel.Involves(userId))
                {
                    own = duel;
                }
            }

            var roster = new PvpFighterDto[_fighters.Length];
            for (var i = 0; i < _fighters.Length; i++)
            {
                var f = _fighters[i];
                roster[i] = new PvpFighterDto
                {
                    UserId = f.UserId,
                    NickName = f.NickName,
                    Hp = Math.Max(0, f.Hp),
                    MaxHp = f.MaxHp,
                    Gold = f.Gold,
                    Alive = f.Alive,
                    Rank = f.Rank,
                    RubLeft = f.RubLeft,
                    ReplaceLeft = f.ReplaceLeft,
                    PeekLeft = f.PeekLeft,
                    IsBot = f.IsBot
                };
            }

            return new PvpMatchStateDto
            {
                RoomId = RoomId.ToString("N"),
                Seed = Seed,
                ModeId = ModeId,
                ModeName = ModeName,
                Round = Round,
                Phase = Phase,
                FightKind = _kind == PvpFightKind.Monster ? "monster" : "pvp",
                Players = roster,
                Duels = summaries,
                Duel = own == null ? null : own.ViewFor(userId, RoomId.ToString("N"))
            };
        }
    }

    private void StartRound()
    {
        _duels.Clear();
        if (!PvpSchedule.TryGetRound(_rounds, Round, out _row))
        {
            FinishByHp();
            return;
        }

        var alive = AliveSeats();
        if (alive.Count <= 1)
        {
            FinishSurvivor();
            return;
        }

        _kind = PvpSchedule.EffectiveKind(_mode, _row, alive.Count);
        ResetSkills();
        IReadOnlyList<PvpPairSlot> slots;
        if (_kind == PvpFightKind.Monster)
        {
            slots = PvpPairing.ForMonster(alive);
        }
        else
        {
            slots = PvpPairing.ForPvp(alive, _pvpCycle);
            _pvpCycle++;
        }

        var monsterGroup = _row.MonsterGroup;
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var left = ToPlayerSeat(_fighters[slot.LeftSeat], 0);
            SeatSetup right;
            var vsPlayer = !slot.IsMonster;
            if (vsPlayer)
            {
                right = ToPlayerSeat(_fighters[slot.RightSeat!.Value], 1);
            }
            else
            {
                right = ToMonsterSeat(monsterGroup);
            }

            var duelSeed = unchecked(Seed * 397 ^ Round * 911 ^ i * 17);
            _duels.Add(PvpDuelTable.Open(duelSeed, left, right, _tables, vsPlayer));
        }

        Phase = PhaseFight;
        SettleResolvedDuels();
        TryAdvanceRound();
    }

    private void SettleResolvedDuels()
    {
        for (var d = 0; d < _duels.Count; d++)
        {
            var duel = _duels[d];
            if (!duel.Resolved || duel.HpApplied)
            {
                continue;
            }

            ApplyDuelDamage(duel);
            duel.MarkHpApplied();
        }
    }

    private void ApplyDuelDamage(PvpDuelTable duel)
    {
        var snap = duel.Engine.Snapshot;
        if (snap.Winners == null || snap.Winners.Count != 1)
        {
            return;
        }

        var win = snap.Winners[0];
        if (win != 0 && win != 1)
        {
            return;
        }

        var raw = snap.Damages[win];
        var damage = (int)Math.Floor(raw * (1f + _mode.DamageRoundScale * Round));
        if (damage < 0)
        {
            damage = 0;
        }

        var deathHp = new Dictionary<int, int>();
        ApplyToSeat(duel, 1 - win, damage, deathHp);
        RankDead(deathHp);
    }

    private void TryAdvanceRound()
    {
        if (Phase == PhaseFinished || !AllResolved())
        {
            return;
        }

        var alive = AliveSeats();
        if (alive.Count <= 1)
        {
            FinishSurvivor();
            return;
        }

        if (Round >= PvpSchedule.LastRound(_rounds))
        {
            FinishByHp();
            return;
        }

        Round++;
        StartRound();
    }

    private void ApplyToSeat(PvpDuelTable duel, int duelSeat, int damage, Dictionary<int, int> deathHp)
    {
        string userId;
        if (duelSeat == 0)
        {
            userId = duel.LeftUserId;
        }
        else if (duel.VsMonster)
        {
            return;
        }
        else
        {
            userId = duel.RightUserId;
        }

        var index = SeatOf(userId);
        if (index < 0)
        {
            return;
        }

        var fighter = _fighters[index];
        if (!fighter.Alive)
        {
            return;
        }

        fighter.Hp -= damage;
        if (fighter.Hp <= 0)
        {
            deathHp[index] = fighter.Hp;
            fighter.Alive = false;
        }
    }

    private void RankDead(Dictionary<int, int> deathHp)
    {
        if (deathHp.Count == 0)
        {
            return;
        }

        var order = new List<int>(deathHp.Keys);
        order.Sort((a, b) =>
        {
            var cmp = Math.Abs(deathHp[a]).CompareTo(Math.Abs(deathHp[b]));
            return cmp != 0 ? cmp : a.CompareTo(b);
        });

        var aliveAfter = 0;
        for (var i = 0; i < _fighters.Length; i++)
        {
            if (_fighters[i].Alive)
            {
                aliveAfter++;
            }
        }

        var rank = aliveAfter + 1;
        for (var i = 0; i < order.Count; i++)
        {
            _fighters[order[i]].Rank = rank++;
            if (_fighters[order[i]].Hp < 0)
            {
                _fighters[order[i]].Hp = 0;
            }
        }
    }

    private void FinishSurvivor()
    {
        Phase = PhaseFinished;
        for (var i = 0; i < _fighters.Length; i++)
        {
            if (_fighters[i].Alive && _fighters[i].Rank == 0)
            {
                _fighters[i].Rank = 1;
            }
        }
    }

    private void FinishByHp()
    {
        Phase = PhaseFinished;
        var living = new List<PvpFighter>();
        for (var i = 0; i < _fighters.Length; i++)
        {
            if (_fighters[i].Alive && _fighters[i].Rank == 0)
            {
                living.Add(_fighters[i]);
            }
        }

        living.Sort((a, b) =>
        {
            var cmp = b.Hp.CompareTo(a.Hp);
            return cmp != 0 ? cmp : a.SeatIndex.CompareTo(b.SeatIndex);
        });
        for (var i = 0; i < living.Count; i++)
        {
            living[i].Rank = i + 1;
        }
    }

    private bool AllResolved()
    {
        if (_duels.Count == 0)
        {
            return true;
        }

        for (var i = 0; i < _duels.Count; i++)
        {
            if (!_duels[i].Resolved)
            {
                return false;
            }
        }

        return true;
    }

    private List<int> AliveSeats()
    {
        var list = new List<int>(_fighters.Length);
        for (var i = 0; i < _fighters.Length; i++)
        {
            if (_fighters[i].Alive)
            {
                list.Add(i);
            }
        }

        return list;
    }

    private PvpDuelTable? FindDuel(string userId)
    {
        for (var i = 0; i < _duels.Count; i++)
        {
            if (_duels[i].Involves(userId))
            {
                return _duels[i];
            }
        }

        return null;
    }

    private PvpFighter FighterOf(string userId)
    {
        var index = SeatOf(userId);
        if (index < 0)
        {
            throw new InvalidOperationException("Not in a duel.");
        }

        return _fighters[index];
    }

    private void ResetSkills()
    {
        var rub = _tables.GameConst.DefaultSkillShuffleNum;
        var replace = _tables.GameConst.DefaultSkillReplaceNum;
        var peek = _tables.GameConst.DefaultSkillPerspectiveNum;
        if (rub <= 0)
        {
            rub = 3;
        }

        if (replace <= 0)
        {
            replace = 1;
        }

        if (peek <= 0)
        {
            peek = 1;
        }

        for (var i = 0; i < _fighters.Length; i++)
        {
            var fighter = _fighters[i];
            if (!fighter.Alive)
            {
                continue;
            }

            fighter.RubLeft = rub;
            fighter.ReplaceLeft = replace;
            fighter.PeekLeft = peek;
        }
    }

    private static SeatSetup ToPlayerSeat(PvpFighter fighter, int seatId)
    {
        var combat = fighter.Combat;
        return new SeatSetup
        {
            SeatId = seatId,
            UserId = fighter.UserId,
            NickName = fighter.NickName,
            IsHuman = !fighter.IsBot,
            Alive = true,
            Attack = combat.Attack,
            Hp = fighter.Hp,
            MaxHp = fighter.MaxHp,
            HeroId = combat.HeroId,
            Talents = CombatBonuses.CloneTalents(combat.Talents),
            RelicIds = CombatBonuses.CloneRelicIds(combat.RelicIds)
        };
    }

    private SeatSetup ToMonsterSeat(int groupId)
    {
        if (_tables.TryGetMonsterGroup(groupId, out var group)
            && _tables.TryGetMonster(group.MonsterId, group.MonsterLevel, out var monster))
        {
            return new SeatSetup
            {
                SeatId = 1,
                UserId = string.Empty,
                NickName = string.IsNullOrEmpty(monster.Name) ? "野怪" : monster.Name,
                IsHuman = false,
                Alive = true,
                Attack = Math.Max(1, monster.MonsterDamage),
                Hp = Math.Max(1, monster.MonsterHp),
                MaxHp = Math.Max(1, monster.MonsterHp)
            };
        }

        return new SeatSetup
        {
            SeatId = 1,
            NickName = "野怪",
            IsHuman = false,
            Alive = true,
            Attack = 1,
            Hp = 1,
            MaxHp = 1
        };
    }
}
