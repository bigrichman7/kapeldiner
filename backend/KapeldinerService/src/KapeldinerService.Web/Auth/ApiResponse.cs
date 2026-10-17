namespace KapeldinerService.Web.Auth;

public sealed record ApiEnvelope<T>(bool Success, T? Data, ApiError? Error);

public static class ApiResponse
{
    public static ApiEnvelope<T> Ok<T>(T data) => new(true, data, null);

    public static ApiEnvelope<T> Failure<T>(string code, string message) =>
        new(false, default, new ApiError(code, message, null));
}

public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Fields);
