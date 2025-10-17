using Microsoft.AspNetCore.Mvc;
using MediatR;
using KaiAssistant.Application.AskAssistants.Commands;
using KaiAssistant.Application.DTOs;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;

namespace KaiAssistant.API.Controller;

[ApiController]
[Route("/api/assistants")]
public class AssistantController : ControllerBase
{
    private readonly IMediator _mediator;
    private const string ClientIdHeader = "X-Client-Id";
    private const string SessionIdHeader = "X-Session-Id";
    private const string ForwardedForHeader = "X-Forwarded-For";
    private const string ClientIdCookie = "ka-cid";

    public AssistantController(IMediator mediator)
    {
        _mediator = mediator;
    }
    [HttpPost("ask")]
    public async Task<IActionResult> Applied([FromBody] TextDto dto)
    {
        var userId = GetOrCreateClientId(HttpContext);

        var command = new AskAssistantCommand(dto.Message, userId);
        var response = await _mediator.Send(command);
        return Ok(response);
    }

    private string GetOrCreateClientId(HttpContext context)
    {
        // 1) Prefer explicit browser/machine ID provided by client
        if (context.Request.Headers.TryGetValue(ClientIdHeader, out var clientIdHeader))
        {
            var clientId = clientIdHeader.FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                EnsureClientIdCookie(context, clientId);
                return $"cid:{clientId}";
            }
        }

        // 2) Fallback to a session header, if present
        if (context.Request.Headers.TryGetValue(SessionIdHeader, out var sessionIdHeader))
        {
            var sessionId = sessionIdHeader.FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                EnsureClientIdCookie(context, sessionId);
                return $"sid:{sessionId}";
            }
        }

        // 3) Reuse a stable cookie-based ID if available
        if (context.Request.Cookies.TryGetValue(ClientIdCookie, out var cookieId) && !string.IsNullOrWhiteSpace(cookieId))
        {
            return $"cid:{cookieId}";
        }

        // 4) Derive from forwarded IP or remote IP + user agent hash (more stable behind NAT)
        string? ip = null;
        if (context.Request.Headers.TryGetValue(ForwardedForHeader, out var forwarded))
        {
            ip = forwarded.FirstOrDefault()?.Split(',').FirstOrDefault()?.Trim();
        }
        ip ??= context.Connection.RemoteIpAddress?.ToString();

        var ua = context.Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;
        var derived = DeriveStableId(ip ?? "0.0.0.0", ua);
        EnsureClientIdCookie(context, derived);
        return $"cid:{derived}";
    }

    private static string DeriveStableId(string ip, string userAgent)
    {
        // Hash(ip + UA) to avoid exposing raw identifiers and to make it stable per browser/machine
        var input = $"{ip}|{userAgent}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32]; // 128-bit hex
    }

    private void EnsureClientIdCookie(HttpContext context, string clientId)
    {
        if (context.Request.Cookies.ContainsKey(ClientIdCookie)) return;
        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddYears(1)
        };
        context.Response.Cookies.Append(ClientIdCookie, clientId, opts);
    }

}