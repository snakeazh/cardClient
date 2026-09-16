namespace CardShare.Domain;

public interface IAuthBindingRepository
{
    Task<Guid?> FindUserIdAsync(string provider, string openId, CancellationToken cancellationToken);

    Task BindAsync(string provider, string openId, Guid userId, CancellationToken cancellationToken);
}
