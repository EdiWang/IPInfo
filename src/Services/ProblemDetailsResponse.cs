using System.Text.Json;

namespace IPInfo.Services;

public static class ProblemDetailsResponse
{
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string type,
        string title,
        string detail,
        CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                type,
                title,
                status = statusCode,
                detail
            },
            cancellationToken: cancellationToken == default ? context.RequestAborted : cancellationToken);
    }
}
