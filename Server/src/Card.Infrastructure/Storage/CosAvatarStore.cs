using CardShare.Contracts;
using CardShare.Domain;
using COSXML;
using COSXML.Auth;
using COSXML.Model.Object;
using Microsoft.Extensions.Options;

namespace CardShare.Infrastructure.Storage;

public sealed class CosAvatarOptions
{
    public string SecretId { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    /// <summary>对象键前缀，如 avatars/。</summary>
    public string KeyPrefix { get; set; } = "avatars/";

    public string PublicBaseUrl => $"https://{Bucket}.cos.{Region}.myqcloud.com";
}

/// <summary>头像存腾讯云 COS（桶需公有读，URL 直接可访问）。</summary>
public sealed class CosAvatarStore : IAvatarStore
{
    private readonly CosAvatarOptions _options;
    private readonly CosXml _cos;

    public CosAvatarStore(IOptions<CosAvatarOptions> options)
    {
        _options = options.Value;
        var config = new CosXmlConfig.Builder()
            .IsHttps(true)
            .SetRegion(_options.Region)
            .Build();
        var credential = new DefaultQCloudCredentialProvider(_options.SecretId, _options.SecretKey, 600);
        _cos = new CosXmlServer(config, credential);
    }

    public Task<string> PutAsync(Guid userId, byte[] data, string extension, CancellationToken cancellationToken)
    {
        var key = $"{_options.KeyPrefix}{userId:N}/{Guid.NewGuid():N}.{extension}";
        var request = new PutObjectRequest(_options.Bucket, key, data);
        request.SetRequestHeader("Content-Type", extension == "png" ? "image/png" : "image/jpeg");
        return Task.Run(() =>
        {
            _cos.PutObject(request);
            return $"{_options.PublicBaseUrl}/{key}";
        }, cancellationToken);
    }
}

/// <summary>未配置 COS 时的显式兜底：报 501，不静默降级。</summary>
public sealed class DisabledAvatarStore : IAvatarStore
{
    public Task<string> PutAsync(Guid userId, byte[] data, string extension, CancellationToken cancellationToken)
        => throw new DomainException(ErrorCodes.ProviderNotConfigured, "Avatar storage (Cos) is not configured.");
}
