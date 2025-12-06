using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace KaiAssistant.Infrastructure.Gateways;
public class GeminiGateway : IGeminiGateway
{
    private readonly IHttpClientFactory _factory;
    private readonly IOptions<GeminiSettings> _settings;
    private readonly ILogger<GeminiGateway> _logger;

    public GeminiGateway(IHttpClientFactory factory, IOptions<GeminiSettings> settings, ILogger<GeminiGateway> logger)
    {
        _factory = factory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<(string? Body, string? UsedModel)> SendGenerationRequestAsync(string payloadJson, IEnumerable<string> models, CancellationToken cancellationToken = default)
    {
        var client = _factory.CreateClient("Gemini");
        foreach (var model in models)
        {
            try
            {
                var url = ($"{_settings.Value.Endpoint}{model}");
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("x-goog-api-key", _settings.Value.ApiKey);
                var resp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return (body, model);
                }
                var bodyErr = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Gemini model {Model} returned {Status}: {Body}", model, resp.StatusCode, bodyErr);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Gemini request cancelled for model {Model}", model);
                throw;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Exception calling Gemini model {Model}", model);
            }
        }

        return (null, null);
    }
}