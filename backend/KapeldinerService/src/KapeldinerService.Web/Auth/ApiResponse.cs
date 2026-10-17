namespace KapeldinerService.Web.Auth;

public sealed record ApiResponse<T>(bool Success, T? Data, ApiError? Error)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null);

    public static ApiResponse<T> Failure(string code, string message) =>
        new(false, default, new ApiError(code, message, null));
}

public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Fields);
