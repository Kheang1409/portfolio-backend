using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace KaiAssistant.API.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;
    private readonly IReadOnlyList<IExceptionHandler> _handlers;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env,
        IEnumerable<IExceptionHandler> handlers)
    {
        _next = next;
        _logger = logger;
        _env = env;
        _handlers = handlers.OrderBy(h => h.Order).ToArray();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var includeDetails = _env.IsDevelopment();
        var problem = BuildProblemDetails(context, exception, includeDetails);

        context.Response.StatusCode = problem.Status ?? (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, options));
    }

    private ProblemDetails BuildProblemDetails(HttpContext context, Exception ex, bool includeDetails)
    {
        var handler = _handlers.FirstOrDefault(h => h.CanHandle(ex)) ?? new UnhandledExceptionHandler();
        var problem = handler.CreateProblemDetails(ex, context, includeDetails);

        problem.Instance ??= context.Request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        return problem;
    }
}

public interface IExceptionHandler
{
    bool CanHandle(Exception exception);
    ProblemDetails CreateProblemDetails(Exception exception, HttpContext context, bool includeDetails);
    int Order { get; }
}

public class ArgumentExceptionHandler : IExceptionHandler
{
    public bool CanHandle(Exception exception) => exception is ArgumentException;

    public ProblemDetails CreateProblemDetails(Exception exception, HttpContext context, bool includeDetails) =>
        new()
        {
            Status = (int)HttpStatusCode.BadRequest,
            Title = "Invalid argument",
            Detail = includeDetails ? exception.Message : null,
            Type = "https://httpstatuses.com/400"
        };

    public int Order => 1;
}

public class UnauthorizedAccessExceptionHandler : IExceptionHandler
{
    public bool CanHandle(Exception exception) => exception is UnauthorizedAccessException;

    public ProblemDetails CreateProblemDetails(Exception exception, HttpContext context, bool includeDetails) =>
        new()
        {
            Status = (int)HttpStatusCode.Unauthorized,
            Title = "Unauthorized access",
            Detail = includeDetails ? exception.Message : null,
            Type = "https://httpstatuses.com/401"
        };

    public int Order => 2;
}

public class InvalidOperationExceptionHandler : IExceptionHandler
{
    public bool CanHandle(Exception exception) => exception is InvalidOperationException;

    public ProblemDetails CreateProblemDetails(Exception exception, HttpContext context, bool includeDetails) =>
        new()
        {
            Status = (int)HttpStatusCode.BadRequest,
            Title = "Invalid operation",
            Detail = includeDetails ? exception.Message : null,
            Type = "https://httpstatuses.com/400"
        };

    public int Order => 3;
}

public class NotFoundExceptionHandler : IExceptionHandler
{
    public bool CanHandle(Exception exception) => exception is KeyNotFoundException;

    public ProblemDetails CreateProblemDetails(Exception exception, HttpContext context, bool includeDetails) =>
        new()
        {
            Status = (int)HttpStatusCode.NotFound,
            Title = "Resource not found",
            Detail = includeDetails ? exception.Message : null,
            Type = "https://httpstatuses.com/404"
        };

    public int Order => 4;
}

public class RequestTimeoutExceptionHandler : IExceptionHandler
{
    public bool CanHandle(Exception exception) => exception is TaskCanceledException;

    public ProblemDetails CreateProblemDetails(Exception exception, HttpContext context, bool includeDetails) =>
        new()
        {
            Status = (int)HttpStatusCode.RequestTimeout,
            Title = "Request cancelled or timed out",
            Detail = includeDetails ? exception.Message : null,
            Type = "https://httpstatuses.com/408"
        };

    public int Order => 5;
}

public class UnhandledExceptionHandler : IExceptionHandler
{
    public bool CanHandle(Exception exception) => true;

    public ProblemDetails CreateProblemDetails(Exception exception, HttpContext context, bool includeDetails)
    {
        var detail = includeDetails ? exception.ToString() : "An unexpected error occurred.";

        return new ProblemDetails
        {
            Status = (int)HttpStatusCode.InternalServerError,
            Title = "Unexpected error",
            Detail = detail,
            Type = "https://httpstatuses.com/500"
        };
    }

    public int Order => int.MaxValue;
}