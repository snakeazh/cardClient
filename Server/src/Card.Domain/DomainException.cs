using CardShare.Contracts;

namespace CardShare.Domain;

public sealed class DomainException : Exception
{
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    public static DomainException Unauthorized(string message = "Unauthorized.")
        => new DomainException(ErrorCodes.Unauthorized, message);

    public static DomainException Invalid(string message)
        => new DomainException(ErrorCodes.InvalidRequest, message);
}
