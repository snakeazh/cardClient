namespace CardShare.Domain;

public sealed class CodeSession
{
    public CodeSession(string openId)
    {
        OpenId = openId;
    }

    public string OpenId { get; }
}

public interface ICodeSessionClient
{
    string Provider { get; }

    Task<CodeSession> ExchangeAsync(string code, CancellationToken cancellationToken);
}
