using CardShare.Domain;

namespace CardShare.Server;

public static class HttpContextUser
{
    public const string ItemKey = "card.userId";

    public static Guid RequireUserId(this HttpContext context)
    {
        if (context.Items.TryGetValue(ItemKey, out var value) && value is Guid userId)
        {
            return userId;
        }

        throw DomainException.Unauthorized();
    }
}
