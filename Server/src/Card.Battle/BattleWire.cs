using System;
using System.Collections.Generic;
using CardShare.Contracts;

namespace CardShare.Battle;

/// <summary>内部 <see cref="Card"/> 不上网，只下发 DTO。摊牌前对手手牌为 null。</summary>
public static class BattleWire
{
    public static BattleStateDto ForViewer(
        BattleSnapshot snapshot,
        int viewerSeat,
        string roomId,
        IReadOnlyList<PlayerPublic> players)
    {
        var showAll = snapshot.Phase == BattlePhase.Showdown;
        var seats = new BattleSeatDto[BattleLimits.RoomSeats];
        for (var i = 0; i < seats.Length; i++)
        {
            var setup = snapshot.Seats[i];
            var player = players[i];
            var hand = snapshot.Hands[i];
            var reveal = showAll || i == viewerSeat;
            var score = snapshot.Scores[i];
            var scored = snapshot.Phase == BattlePhase.Showdown && HasOpenHand(hand);
            seats[i] = new BattleSeatDto
            {
                SeatId = setup.SeatId,
                UserId = FirstNonEmpty(setup.UserId, player.UserId),
                NickName = FirstNonEmpty(player.NickName, setup.NickName),
                IsHuman = setup.IsHuman,
                Alive = setup.Alive,
                Cards = reveal ? ToCards(hand) : null,
                HandType = scored ? score.Type.ToString() : null,
                Label = scored ? score.Label : null,
                Level = scored ? score.Level : (int?)null,
                Multiplier = scored ? score.Multiplier : (float?)null,
                Damage = snapshot.Phase == BattlePhase.Showdown ? snapshot.Damages[i] : (int?)null
            };
        }

        return new BattleStateDto
        {
            RoomId = roomId,
            Seed = snapshot.Seed,
            Mode = snapshot.Mode == BattleModeKind.Pve ? "pve" : "pvp",
            Phase = PhaseName(snapshot.Phase),
            ViewerSeat = viewerSeat,
            Seats = seats,
            Winners = snapshot.Winners
        };
    }

    public static CardDto ToDto(Card card)
        => new CardDto { Suit = (int)card.Suit, Rank = (int)card.Rank };

    private static IReadOnlyList<CardDto> ToCards(IReadOnlyList<Card> hand)
    {
        if (hand.Count == 0)
        {
            return Array.Empty<CardDto>();
        }

        var list = new List<CardDto>(hand.Count);
        for (var i = 0; i < hand.Count; i++)
        {
            if (hand[i].IsValid)
            {
                list.Add(ToDto(hand[i]));
            }
        }

        return list;
    }

    private static bool HasOpenHand(IReadOnlyList<Card> hand)
    {
        if (hand.Count < BattleLimits.OpenHandSize)
        {
            return false;
        }

        for (var i = 0; i < BattleLimits.OpenHandSize; i++)
        {
            if (!hand[i].IsValid)
            {
                return false;
            }
        }

        return true;
    }

    private static string PhaseName(BattlePhase phase)
    {
        switch (phase)
        {
            case BattlePhase.Dealt: return "dealt";
            case BattlePhase.Showdown: return "showdown";
            default: return "idle";
        }
    }

    private static string FirstNonEmpty(string a, string b)
    {
        if (!string.IsNullOrEmpty(a))
        {
            return a;
        }

        return b;
    }
}
