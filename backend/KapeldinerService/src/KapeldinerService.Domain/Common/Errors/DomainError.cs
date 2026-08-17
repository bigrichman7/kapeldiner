namespace KapeldinerService.Domain.Common.Errors;

public class DomainError
{
    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }

    public DomainError(string code, string message, ErrorType type)
    {
        Code = code;
        Message = message;
        Type = type;
    }
       
}

public enum ErrorType
{
    ValidationError,
    RequiredError,
}