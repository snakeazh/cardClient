namespace CardShare.Domain;

/// <summary>头像图片存储。PutAsync 存图并返回可公网访问的 URL。</summary>
public interface IAvatarStore
{
    Task<string> PutAsync(Guid userId, byte[] data, string extension, CancellationToken cancellationToken);
}
