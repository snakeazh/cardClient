namespace CardShare.Contracts;

public sealed class ApiError
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
