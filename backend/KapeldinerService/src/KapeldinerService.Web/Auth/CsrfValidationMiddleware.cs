using Microsoft.AspNetCore.Antiforgery;

namespace KapeldinerService.Web.Auth;

public sealed class CsrfValidationMiddleware(RequestDelegate next)
{
    private static readonly PathString LoginPath = new("/api/auth/login");

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        bool isUnsafeMethod = HttpMethods.IsPost(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method);
        bool isCookieAuthenticated = context.User.Identity?.IsAuthenticated == true;

        if (isUnsafeMethod && (context.Request.Path.Equals(LoginPath) || isCookieAuthenticated))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Failure(
                    "CSRF_VALIDATION_FAILED",
                    "Запрос отклонен: отсутствует или недействителен CSRF-токен."));
                return;
            }
        }

        await next(context);
    }
}
