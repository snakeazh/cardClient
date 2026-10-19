using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Domain;
using CardShare.Domain.Pvp;

namespace CardShare.Server.Pvp;

public sealed class PvpMatchHost
{
    public const int DefaultModeId = PvpSchedule.DefaultModeId;

    private const long FinishedRoomTtlMs = 5 * 60 * 1000;

    /// <summary>补进来的机器人隔这么久“点一次接受”，避免四个头像同时揭开。</summary>
    private const int BotAcceptGapMs = 1000;

    private readonly IGameTables _tables;
    private readonly object _gate = new object();
    private readonly Dictionary<Guid, PvpMatch> _byUser = new Dictionary<Guid, PvpMatch>();
    private readonly Dictionary<Guid, PvpMatch> _byRoom = new Dictionary<Guid, PvpMatch>();
    private readonly Dictionary<Guid, ReadyAccept> _acceptByUser = new Dictionary<Guid, ReadyAccept>();

    private sealed class ReadyAccept
    {
        public Guid RoomId { get; set; }

        public PvpMatch? Match { get; set; }

        public Guid[] Humans = Array.Empty<Guid>();

        public Guid[] Bots = Array.Empty<Guid>();

        public long[] BotDueUtcMs = Array.Empty<long>();

        public PlayerPublic[] Requeue = Array.Empty<PlayerPublic>();

        public long OpenedUtcMs { get; set; }

        public long DeadlineUtcMs { get; set; }

        public HashSet<Guid> Accepted { get; } = new HashSet<Guid>();

        public HashSet<Guid> BotAccepted { get; } = new HashSet<Guid>();

        public bool Closed { get; set; }
    }

    public readonly struct AcceptFailGroup
    {
        public AcceptFailGroup(PlayerPublic[] requeue, Guid[] humans)
        {
            Requeue = requeue ?? Array.Empty<PlayerPublic>();
            Humans = humans ?? Array.Empty<Guid>();
        }

        public PlayerPublic[] Requeue { get; }

        public Guid[] Humans { get; }
    }

    public readonly struct AcceptProgress
    {
        public AcceptProgress(Guid[] humans, string[] acceptedIds, bool allAccepted, PvpMatch? match)
        {
            Humans = humans ?? Array.Empty<Guid>();
            AcceptedIds = acceptedIds ?? Array.Empty<string>();
            AllAccepted = allAccepted;
            Match = match;
        }

        public Guid[] Humans { get; }

        public string[] AcceptedIds { get; }

        public bool AllAccepted { get; }

        public PvpMatch? Match { get; }
    }

    public PvpMatchHost(IGameTables tables)
    {
        _tables = tables;
    }

    public PvpMatch Open(PvpRoom room, IReadOnlyList<SeatSetup> combatSeats, int modeId = DefaultModeId)
    {
        lock (_gate)
        {
            if (_byRoom.TryGetValue(room.RoomId, out var existing))
            {
                return existing;
            }

            var match = PvpMatch.Open(room.RoomId, room.Seed, room.Players, _tables, combatSeats, modeId);
            _byRoom[room.RoomId] = match;
            foreach (var player in room.Players)
            {
                if (player.IsBot || !Guid.TryParse(player.UserId, out var userId))
                {
                    continue;
                }

                _byUser[userId] = match;
            }

            var humans = new List<Guid>();
            var bots = new List<Guid>();
            var requeue = new List<PlayerPublic>();
            foreach (var player in room.Players)
            {
                if (!Guid.TryParse(player.UserId, out var userId))
                {
                    continue;
                }

                if (player.IsBot)
                {
                    bots.Add(userId);
                    continue;
                }

                humans.Add(userId);
                requeue.Add(ClonePublic(player));
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var botDue = new long[bots.Count];
            for (var i = 0; i < botDue.Length; i++)
            {
                botDue[i] = now + (i + 1) * BotAcceptGapMs;
            }

            var gate = new ReadyAccept
            {
                RoomId = room.RoomId,
                Match = match,
                Humans = humans.ToArray(),
                Bots = bots.ToArray(),
                BotDueUtcMs = botDue,
                Requeue = requeue.ToArray(),
                OpenedUtcMs = now,
                DeadlineUtcMs = now + PvpRules.AcceptTimeoutMs
            };
            for (var i = 0; i < gate.Humans.Length; i++)
            {
                _acceptByUser[gate.Humans[i]] = gate;
            }

            return match;
        }
    }

    /// <summary>真人点接受。机器人不算在名单里。全员接受后关闭闸门，并把等待时长补回对局截止时间。</summary>
    public bool TryAccept(Guid userId, out bool allAccepted, out Guid[] humans, out string[] acceptedIds, out PvpMatch? match)
    {
        lock (_gate)
        {
            allAccepted = false;
            humans = Array.Empty<Guid>();
            acceptedIds = Array.Empty<string>();
            match = null;
            if (!_acceptByUser.TryGetValue(userId, out var gate) || gate.Closed)
            {
                return false;
            }

            gate.Accepted.Add(userId);
            humans = gate.Humans;
            var finished = TryFinish(gate, out match);
            allAccepted = finished;
            acceptedIds = SnapshotAccepted(gate);
            return true;
        }
    }

    /// <summary>到点的机器人逐个算作已接受，表现和真人点接受一样。</summary>
    public List<AcceptProgress> SweepBotAccepts(long nowUtcMs)
    {
        lock (_gate)
        {
            var result = new List<AcceptProgress>();
            var seen = new HashSet<ReadyAccept>();
            var gates = new List<ReadyAccept>();
            foreach (var gate in _acceptByUser.Values)
            {
                if (seen.Add(gate))
                {
                    gates.Add(gate);
                }
            }

            for (var g = 0; g < gates.Count; g++)
            {
                var gate = gates[g];
                if (gate.Closed)
                {
                    continue;
                }

                var changed = false;
                for (var i = 0; i < gate.Bots.Length; i++)
                {
                    if (nowUtcMs < gate.BotDueUtcMs[i])
                    {
                        continue;
                    }

                    if (gate.BotAccepted.Add(gate.Bots[i]))
                    {
                        changed = true;
                    }
                }

                if (!changed)
                {
                    continue;
                }

                var finished = TryFinish(gate, out var match);
                result.Add(new AcceptProgress(gate.Humans, SnapshotAccepted(gate), finished, match));
            }

            return result;
        }
    }

    private bool TryFinish(ReadyAccept gate, out PvpMatch? match)
    {
        match = null;
        if (gate.Closed
            || gate.Humans.Length == 0
            || gate.Accepted.Count < gate.Humans.Length
            || gate.BotAccepted.Count < gate.Bots.Length)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        gate.Match?.ShiftDeadline(now - gate.OpenedUtcMs);
        match = gate.Match;
        DetachGate(gate);
        return true;
    }

    private static string[] SnapshotAccepted(ReadyAccept gate)
    {
        var ids = new string[gate.Accepted.Count + gate.BotAccepted.Count];
        var index = 0;
        foreach (var id in gate.Accepted)
        {
            ids[index++] = id.ToString("N");
        }

        foreach (var id in gate.BotAccepted)
        {
            ids[index++] = id.ToString("N");
        }

        return ids;
    }

    /// <summary>有人没接受或主动拒绝。解散未确认的对局。requeueUser 为 false 时本人不回队列（离开页面）。</summary>
    public bool TryFailAccept(Guid userId, bool requeueUser, out AcceptFailGroup group)
    {
        lock (_gate)
        {
            group = default;
            if (!_acceptByUser.TryGetValue(userId, out var gate) || gate.Closed)
            {
                return false;
            }

            group = CloseGate(gate, requeueUser ? null : userId);
            return true;
        }
    }

    /// <summary>接受超时仍未全员确认：解散并让全部真人回优先队列。</summary>
    public List<AcceptFailGroup> SweepAcceptTimeouts(long nowUtcMs)
    {
        lock (_gate)
        {
            var result = new List<AcceptFailGroup>();
            var seen = new HashSet<ReadyAccept>();
            foreach (var gate in _acceptByUser.Values)
            {
                if (!seen.Add(gate) || gate.Closed || nowUtcMs < gate.DeadlineUtcMs)
                {
                    continue;
                }

                result.Add(CloseGate(gate, null));
            }

            return result;
        }
    }

    public bool IsTracked(Guid roomId)
    {
        lock (_gate)
        {
            return _byRoom.ContainsKey(roomId);
        }
    }

    private AcceptFailGroup CloseGate(ReadyAccept gate, Guid? skipRequeue)
    {
        DetachGate(gate);
        DropMatch(gate);

        if (skipRequeue == null)
        {
            return new AcceptFailGroup(gate.Requeue, gate.Humans);
        }

        var requeue = new List<PlayerPublic>(gate.Requeue.Length);
        for (var i = 0; i < gate.Requeue.Length; i++)
        {
            var player = gate.Requeue[i];
            if (player != null && Guid.TryParse(player.UserId, out var id) && id != skipRequeue.Value)
            {
                requeue.Add(player);
            }
        }

        return new AcceptFailGroup(requeue.ToArray(), gate.Humans);
    }

    private void DetachGate(ReadyAccept gate)
    {
        gate.Closed = true;
        for (var i = 0; i < gate.Humans.Length; i++)
        {
            _acceptByUser.Remove(gate.Humans[i]);
        }
    }

    private void DropMatch(ReadyAccept gate)
    {
        if (gate.Match == null)
        {
            return;
        }

        _byRoom.Remove(gate.RoomId);
        var bound = new List<Guid>();
        foreach (var pair in _byUser)
        {
            if (ReferenceEquals(pair.Value, gate.Match))
            {
                bound.Add(pair.Key);
            }
        }

        for (var i = 0; i < bound.Count; i++)
        {
            _byUser.Remove(bound[i]);
        }
    }

    private bool IsPendingAccept(Guid roomId)
    {
        foreach (var gate in _acceptByUser.Values)
        {
            if (!gate.Closed && gate.RoomId == roomId)
            {
                return true;
            }
        }

        return false;
    }

    private static PlayerPublic ClonePublic(PlayerPublic player)
    {
        return new PlayerPublic
        {
            UserId = player.UserId,
            NickName = player.NickName,
            AvatarUrl = player.AvatarUrl,
            IsBot = player.IsBot,
            BotConfigId = player.BotConfigId,
            PvpScore = player.PvpScore
        };
    }

    public bool TryGet(Guid userId, out PvpMatch match)
    {
        lock (_gate)
        {
            return _byUser.TryGetValue(userId, out match!);
        }
    }

    public bool IsBound(Guid userId)
    {
        lock (_gate)
        {
            return _byUser.ContainsKey(userId);
        }
    }

    /// <summary>该用户当前映射的是这一局，而不是后来新开的另一局。</summary>
    public bool IsBoundTo(Guid userId, PvpMatch match)
    {
        lock (_gate)
        {
            return match != null && _byUser.TryGetValue(userId, out var bound) && ReferenceEquals(bound, match);
        }
    }

    /// <summary>仅解除 user→match 映射，不 Abandon（用于已淘汰玩家停推/禁重连）。</summary>
    public void Unbind(Guid userId)
    {
        lock (_gate)
        {
            _byUser.Remove(userId);
        }
    }

    /// <summary>
    /// 淘汰后的真人从映射解绑：不再 sync/重连，也不再收后续对局推送。
    /// 须在「本批已把淘汰快照推给本人」之后调用。
    /// </summary>
    public void UnbindEliminated(PvpMatch match)
    {
        if (match == null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var fighter in match.Fighters)
            {
                if (fighter.Alive || fighter.IsBot || !Guid.TryParse(fighter.UserId, out var userId))
                {
                    continue;
                }

                if (_byUser.TryGetValue(userId, out var bound) && ReferenceEquals(bound, match))
                {
                    _byUser.Remove(userId);
                }
            }
        }
    }

    /// <summary>标记玩家断线/重连。match 为 null = 无对局；返回 true = 有状态变化需广播。</summary>
    public bool SetConnected(Guid userId, bool connected, out PvpMatch? match)
    {
        lock (_gate)
        {
            match = null;
            if (!_byUser.TryGetValue(userId, out var found))
            {
                return false;
            }

            var version = found.StateVersion;
            found.SetDisconnected(userId.ToString("N"), !connected);
            match = found;
            return found.StateVersion != version;
        }
    }

    public void Act(Guid userId, string action, int index, int[] indexes)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var match))
            {
                throw DomainException.Invalid("Not in a battle.");
            }

            try
            {
                match.Act(userId.ToString("N"), action, index, indexes);
            }
            catch (InvalidOperationException ex)
            {
                throw DomainException.Invalid(ex.Message);
            }
        }
    }

    public bool AdvanceIfReady(Guid userId)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var match))
            {
                return false;
            }

            var round = match.Round;
            var phase = match.Phase;
            match.AdvanceIfReady();
            return match.Round != round || match.Phase != phase;
        }
    }

    /// <summary>扫描所有房间，结算选牌超时的座位。返回有状态变化、需要广播的比赛。</summary>
    public List<PvpMatch> SweepTimeouts()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            var changed = new List<PvpMatch>();
            foreach (var pair in _byRoom)
            {
                if (IsPendingAccept(pair.Key))
                {
                    continue;
                }

                if (pair.Value.ApplyTimeouts(now))
                {
                    changed.Add(pair.Value);
                }
            }

            return changed;
        }
    }

    /// <summary>回收结束超过 5 分钟的房间。</summary>
    public void PurgeFinishedRooms(DateTimeOffset now)
    {
        var nowMs = now.ToUnixTimeMilliseconds();
        lock (_gate)
        {
            var stale = new List<PvpMatch>();
            foreach (var pair in _byRoom)
            {
                var match = pair.Value;
                if (match.Phase == PvpMatch.PhaseFinished
                    && match.FinishedUtcMs > 0
                    && nowMs - match.FinishedUtcMs > FinishedRoomTtlMs)
                {
                    stale.Add(match);
                }
            }

            foreach (var match in stale)
            {
                _byRoom.Remove(match.RoomId);
                var users = new List<Guid>();
                foreach (var pair in _byUser)
                {
                    if (ReferenceEquals(pair.Value, match))
                    {
                        users.Add(pair.Key);
                    }
                }

                foreach (var userId in users)
                {
                    _byUser.Remove(userId);
                }
            }
        }
    }

    /// <summary>离开对局：在局且存活则判负淘汰（Abandon），随后解除映射。返回 true = 有对局变化需广播，match 为该对局；纯排队/对局已结束则只清映射。</summary>
    public bool Leave(Guid userId, out PvpMatch? match)
    {
        lock (_gate)
        {
            match = null;
            if (!_byUser.Remove(userId, out var found))
            {
                return false;
            }

            if (found.Abandon(userId.ToString("N")))
            {
                match = found;
            }

            var stillSeated = false;
            foreach (var pair in _byUser)
            {
                if (ReferenceEquals(pair.Value, found))
                {
                    stillSeated = true;
                    break;
                }
            }

            if (!stillSeated)
            {
                _byRoom.Remove(found.RoomId);
            }

            return match != null;
        }
    }
}
