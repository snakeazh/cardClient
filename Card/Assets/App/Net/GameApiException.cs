using System;

namespace App.Net
{
    public sealed class GameApiException : Exception
    {
        public GameApiException(string code, string message, int status = 0)
            : base(string.IsNullOrEmpty(message) ? code : message)
        {
            Code = code ?? string.Empty;
            Status = status;
        }

        public string Code { get; }

        public int Status { get; }
    }
}
