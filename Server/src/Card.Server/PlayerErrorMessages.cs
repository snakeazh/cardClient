using CardShare.Contracts;

namespace CardShare.Server;

/// <summary>将返回玩家的服务端错误统一转换为中文，错误码仍供客户端判断。</summary>
public static class PlayerErrorMessages
{
    public static string Translate(string code, string? message)
    {
        var translated = message switch
        {
            "Relic bag is full." => "圣物背包已满",
            "Relic slots are maxed." => "圣物槽位已全部解锁",
            "Relic is not on sale." or "Relic is not on the shelf." => "该圣物不在商店货架上",
            "Relic already owned." or "Already owned." => "已经拥有该圣物",
            "Relic not owned." => "未拥有该圣物",
            "Relic is not consumable." => "该圣物不能使用",
            "Relic has no pvp effect." => "该圣物在竞技模式中无法使用",
            "Not enough gold." or "Not enough run gold." => "金币不足",
            "Not enough energy." => "体力不足",
            "Not enough items." => "道具数量不足",
            "No rub left." => "搓牌次数不足",
            "No replace left." => "换牌次数不足",
            "No peek left." => "透视次数不足",
            "Skills disabled by binding." => "技能受到羁绊限制，暂时无法使用",
            "Shop is closed." => "商店已关闭",
            "Skill is not for sale." => "该技能暂不可购买",
            "Unknown skill." => "技能不存在",
            "Unknown relic." => "圣物不存在",
            "Unknown battle action." or "Unknown shop action." => "操作无效",
            "Match is not in a fight." or "Not in a duel." or "Not in a battle." => "当前不在战斗中",
            "Not waiting to accept." => "当前没有等待确认的对局",
            "Run not found." => "对局已失效",
            "Run already settled." or "Run already settled with different payload." => "对局已结算",
            "An active run already exists." => "已有未结束的闯关",
            "Level is locked." => "关卡未解锁",
            "Hero is locked." => "英雄未解锁",
            "Player not found." => "玩家信息不存在，请重新登录",
            "Invalid access token." or "Missing bearer token." or "Unauthorized." or "Auth first." => "登录已失效，请重新登录",
            "All talents are maxed." or "Talent is already maxed." => "天赋已满级",
            "Daily energy ad refill limit reached." or "Daily ad-shop stamina limit reached." or "Daily ad-shop gold limit reached." => "今日广告次数已用完",
            "Avatar image must be PNG or JPEG." => "头像图片格式需为 PNG 或 JPEG",
            _ => null
        };
        if (translated != null) return translated;
        if (!string.IsNullOrWhiteSpace(message) && message.Any(c => c >= '\u4e00' && c <= '\u9fff')) return message;

        return code switch
        {
            ErrorCodes.InsufficientGold => "金币不足",
            ErrorCodes.InsufficientEnergy => "体力不足",
            ErrorCodes.AdLimitReached => "今日次数已用完",
            ErrorCodes.TalentPoolEmpty => "天赋已全部满级",
            ErrorCodes.LevelLocked => "关卡未解锁",
            ErrorCodes.HeroLocked => "英雄未解锁",
            ErrorCodes.RunNotFound => "对局已失效",
            ErrorCodes.Unauthorized => "登录已失效，请重新登录",
            _ => "操作失败，请稍后重试"
        };
    }
}
