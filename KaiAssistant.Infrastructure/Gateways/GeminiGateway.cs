using KaiAssistant.Application.Interfaces;
using KaiAssistant.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;

namespace KaiAssistant.Infrastructure.Gateways;
public class GeminiGateway : IGeminiGateway
{
    private readonly IHttpClientFactory _factory;
    private readonly IOptions<GeminiSettings> _settings;
    private readonly ILogger<GeminiGateway> _logger;
    private readonly object _cacheLock = new();
    private string? _lastSuccessfulModel;
    private DateTime _lastSuccessfulAt = DateTime.MinValue;
    private readonly ConcurrentDictionary<string,int> _model429Counts = new();

    public GeminiGateway(IHttpClientFactory factory, IOptions<GeminiSettings> settings, ILogger<GeminiGateway> logger)
    {
        _factory = factory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<(string? Body, string? UsedModel)> SendGenerationRequestAsync(string payloadJson, IEnumerable<string> models, CancellationToken cancellationToken = default)
    {
        var client = _factory.CreateClient("Gemini");
        // Build prioritized model list: prefer last successful model if still fresh
        var modelList = models.ToList();
        var deprioritized = new List<string>();
        string? preferred = null;
        lock (_cacheLock)
        {
            if (!string.IsNullOrWhiteSpace(_lastSuccessfulModel))
            {
                var ttl = TimeSpan.FromSeconds(_settings.Value.LastSuccessfulModelCacheTtlSeconds);
                if (DateTime.UtcNow - _lastSuccessfulAt <= ttl && modelList.Contains(_lastSuccessfulModel))
                {
                    preferred = _lastSuccessfulModel;
                    // Move preferred to front
                    modelList.Remove(_lastSuccessfulModel);
                    modelList.Insert(0, _lastSuccessfulModel);
                    _logger.LogDebug("Preferring last successful model {Model}", _lastSuccessfulModel);
                }
            }
        }

        // Auto-deprioritize or skip models that have excessive 429 counts
        var threshold = _settings.Value.DeprioritizeOn429Count;
        var skipMode = _settings.Value.DeprioritizeSkip;
        var filtered = new List<string>();
        foreach (var m in modelList)
        {
            _model429Counts.TryGetValue(m, out var cnt);
            if (cnt >= threshold)
            {
                if (skipMode)
                {
                    _logger.LogInformation("Skipping model {Model} because 429 count {Count} >= threshold {Threshold}", m, cnt, threshold);
                    continue; // omit this model entirely
                }
                _logger.LogInformation("Deprioritizing model {Model} because 429 count {Count} >= threshold {Threshold}", m, cnt, threshold);
                deprioritized.Add(m);
                continue;
            }
            filtered.Add(m);
        }

        // final candidate order: filtered (good) first, then deprioritized
        modelList = filtered.Concat(deprioritized).ToList();

        foreach (var model in modelList)
        {
            var attempt = 0;
            var maxAttempts = 3;
            var backoffMs = 1000;
            while (attempt < maxAttempts)
            {
                attempt++;
                try
                {
                    var url = $"{_settings.Value.Endpoint}{model}";
                    using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                    };
                    req.Headers.TryAddWithoutValidation("x-goog-api-key", _settings.Value.ApiKey);
                    var resp = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        // Cache successful model
                        lock (_cacheLock)
                        {
                            _lastSuccessfulModel = model;
                            _lastSuccessfulAt = DateTime.UtcNow;
                        }
                        // reset 429 counter on success
                        _model429Counts.AddOrUpdate(model, 0, (_, __) => 0);
                        return (body, model);
                    }

                    var bodyErr = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning("Gemini model {Model} returned {Status} (attempt {Attempt}/{Max}): {Body}", model, resp.StatusCode, attempt, maxAttempts, bodyErr);

                    // Increment 429 counter for this model
                    if (!_model429Counts.ContainsKey(model)) _model429Counts[model] = 0;
                    _model429Counts.AddOrUpdate(model, 1, (_, old) => old + 1);

                    // If rate limited, attempt to respect Retry-After header or embedded RetryInfo
                    if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)resp.StatusCode == 429)
                    {
                        // Try Retry-After header first
                        TimeSpan wait = TimeSpan.FromMilliseconds(backoffMs);
                        if (resp.Headers.RetryAfter != null)
                        {
                            if (resp.Headers.RetryAfter.Delta.HasValue)
                                wait = resp.Headers.RetryAfter.Delta.Value;
                            else if (resp.Headers.RetryAfter.Date.HasValue)
                                wait = resp.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        }
                        else
                        {
                            // Try parse retryDelay from body JSON (google.rpc.RetryInfo)
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(bodyErr);
                                if (doc.RootElement.TryGetProperty("details", out var details) && details.ValueKind == System.Text.Json.JsonValueKind.Array)
                                {
                                    foreach (var d in details.EnumerateArray())
                                    {
                                        if (d.TryGetProperty("@type", out var t) && t.GetString()?.Contains("RetryInfo") == true)
                                        {
                                            if (d.TryGetProperty("retryDelay", out var rd) && rd.ValueKind == System.Text.Json.JsonValueKind.String)
                                            {
                                                if (System.TimeSpan.TryParse(rd.GetString(), out var parsed))
                                                {
                                                    wait = parsed;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // ignore parsing errors
                            }
                        }

                        // If retryDelay is large, skip waiting for this model and try next model immediately
                        var skipThreshold = TimeSpan.FromSeconds(_settings.Value.SkipRetryDelayThresholdSeconds);
                        if (wait > skipThreshold)
                        {
                            _logger.LogInformation("Skipping model {Model} due to large retryDelay {RetryDelay}", model, wait);
                            break; // break retry loop for this model and try next model
                        }

                        if (attempt >= maxAttempts)
                        {
                            _logger.LogWarning("Exceeded retry attempts for model {Model} after receiving 429.", model);
                            break;
                        }
                        _logger.LogInformation("Waiting {Wait} before retrying model {Model} (attempt {Attempt}/{Max})", wait, model, attempt + 1, maxAttempts);
                        try { await Task.Delay(wait, cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { throw; }
                        backoffMs *= 2;
                        continue;
                    }

                    // For other non-success codes, break retry loop for this model
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Gemini request cancelled for model {Model}", model);
                    throw;
                }
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "Exception calling Gemini model {Model} (attempt {Attempt}/{Max})", model, attempt, maxAttempts);
                    if (attempt >= maxAttempts) break;
                    try { await Task.Delay(backoffMs, cancellationToken).ConfigureAwait(false); } catch (OperationCanceledException) { throw; }
                    backoffMs *= 2;
                }
            }
        }

        return (null, null);
    }

    // Note: model 429 counters remain internal to the gateway; no public snapshot API.
}