namespace KapeldinerService.Domain.Common.Errors;

public static class GeneralErrors
{
    public static DomainError ValueIsInvalid(string v) =>
        new("GeneralErrors.ValueIsInvalid", $"Значение недействительно: {v}", ErrorType.ValidationError);

    public static DomainError ValueIsRequired(string v) =>
        new("GeneralErrors.ValueIsRequired", $"Значение обязательно: {v}", ErrorType.RequiredError);
}