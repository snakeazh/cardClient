namespace CardShare.Contracts;

public sealed class PlayerPublic
{
    public string UserId { get; set; } = string.Empty;

    public string NickName { get; set; } = string.Empty;

    public string AvatarUrl { get; set; } = string.Empty;
}
