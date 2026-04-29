namespace SatelliteData.Api.Controllers;

public sealed record ApiResponse<T>(
    bool Success,
    string Code,
    string Message,
    T? Data,
    string TraceId)
{
    public static ApiResponse<T> Ok(T data, HttpContext context) =>
        new(true, "OK", "", data, context.TraceIdentifier);

    public static ApiResponse<T> Fail(string code, string message, HttpContext context) =>
        new(false, code, message, default, context.TraceIdentifier);
}
