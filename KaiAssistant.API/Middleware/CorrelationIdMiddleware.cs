using System.Diagnostics;
using KaiAssistant.Application.Interfaces;

namespace KaiAssistant.API.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly IFeatureFlagService _flags;
    private readonly IInstanceIdentity _instanceIdentity;

    public CorrelationIdMiddleware(RequestDelegate next, IFeatureFlagService flags, IInstanceIdentity instanceIdentity)
    {
        _next = next;
        _flags = flags;
        _instanceIdentity = instanceIdentity;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            ? incoming.ToString()
            : context.TraceIdentifier;

        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString(),
            ["SpanId"] = Activity.Current?.SpanId.ToString(),
            ["InstanceId"] = _instanceIdentity.InstanceId,
            ["ClientIp"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            ["EnableCache"] = _flags.EnableCache,
            ["EnableRateLimiting"] = _flags.EnableRateLimiting,
            ["EnableRabbitMqPublishing"] = _flags.EnableRabbitMqPublishing
        }))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}
