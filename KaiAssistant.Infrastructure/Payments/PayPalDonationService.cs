using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using KaiAssistant.Application.Donations;
using KaiAssistant.Application.Interfaces;
using KaiAssistant.Application.Services;
using KaiAssistant.Domain.Entities;
using KaiAssistant.Domain.Entities.Donations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace KaiAssistant.Infrastructure.Payments;

public sealed class PayPalDonationService(
    IHttpClientFactory httpClientFactory,
    IMongoDatabase database,
    EmailSettings emailSettings,
    IOptions<PayPalOptions> options,
    ILogger<PayPalDonationService> logger) : IDonationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMongoCollection<Donation> _donations = database.GetCollection<Donation>("donations");
    private readonly IMongoCollection<DonationWebhookEvent> _events = database.GetCollection<DonationWebhookEvent>("donation_webhook_events");
    private readonly IMongoCollection<DonationReceiptOutbox> _receipts = database.GetCollection<DonationReceiptOutbox>("donation_receipt_outbox");
    private readonly PayPalOptions _options = options.Value;
    private string ApiBase => string.Equals(_options.Environment, "live", StringComparison.OrdinalIgnoreCase)
        ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";

    public async Task<CreateDonationResult> CreateOrderAsync(CreateDonationRequest request, CancellationToken cancellationToken)
    {
        ValidateAmount(request.Amount);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 64 || !Guid.TryParse(request.IdempotencyKey, out _))
            throw new ArgumentException("A valid idempotency key is required.", nameof(request));

        var amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero);
        var fingerprint = BuildRequestFingerprint(request, amount);
        Donation donation;
        Donation? existing = null;
        if (request.IdempotencyKey != Guid.Empty.ToString())
            existing = await _donations.Find(x => x.IdempotencyKey == request.IdempotencyKey).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.IsNullOrEmpty(existing.RequestFingerprint) && existing.RequestFingerprint != fingerprint)
                throw new InvalidOperationException("This donation request key was already used with different details.");
            if (!string.IsNullOrWhiteSpace(existing.PayPalOrderId)) return new CreateDonationResult(existing.PayPalOrderId);
            if (existing.LastOrderAttemptAtUtc > DateTimeOffset.UtcNow.AddMinutes(-2)) throw new InvalidOperationException("Donation order is still being created; retry shortly.");
            await _donations.UpdateOneAsync(x => x.Id == existing.Id && x.Status == "CREATING", Builders<Donation>.Update.Set(x => x.LastOrderAttemptAtUtc, DateTimeOffset.UtcNow), cancellationToken: cancellationToken).ConfigureAwait(false);
            existing = await _donations.Find(x => x.IdempotencyKey == request.IdempotencyKey).FirstAsync(cancellationToken).ConfigureAwait(false);
            donation = existing;
        }
        else
        {
            // Persist before calling PayPal. Stable provider request ID enables recovery after timeouts.
            donation = new Donation
            {
                IdempotencyKey = request.IdempotencyKey,
                RequestFingerprint = fingerprint,
                LastOrderAttemptAtUtc = DateTimeOffset.UtcNow,
                Amount = amount,
                Currency = _options.Currency,
                Status = "CREATING",
                DonorName = NormalizeName(request.Name),
                DonorEmailProvided = emailSettings.Enabled && !string.IsNullOrWhiteSpace(request.Email),
                DonorEmail = emailSettings.Enabled ? NormalizeEmail(request.Email) : null,
                Remark = request.ShowOnLeaderboard ? NormalizeRemark(request.Remark) : string.Empty,
                ShowOnLeaderboard = request.ShowOnLeaderboard
            };
            try { await _donations.InsertOneAsync(donation, cancellationToken: cancellationToken).ConfigureAwait(false); }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { throw new InvalidOperationException("Donation request is already being processed."); }
        }

        var order = await SendPayPalAsync<PayPalOrder>(HttpMethod.Post, "/v2/checkout/orders", new
        {
            intent = "CAPTURE",
            purchase_units = new[] { new { amount = new { currency_code = _options.Currency, value = amount.ToString("0.00", CultureInfo.InvariantCulture) }, description = "Voluntary portfolio support" } },
            application_context = new { brand_name = _options.BrandName, user_action = "PAY_NOW", shipping_preference = "NO_SHIPPING" }
        }, cancellationToken, "order-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.IdempotencyKey))).ToLowerInvariant()[..32]).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(order.Id)) throw new InvalidOperationException("PayPal returned an invalid order.");
        await _donations.UpdateOneAsync(x => x.Id == donation.Id && x.Status == "CREATING",
            Builders<Donation>.Update.Set(x => x.PayPalOrderId, order.Id).Set(x => x.Status, "PENDING"), cancellationToken: cancellationToken).ConfigureAwait(false);
        return new CreateDonationResult(order.Id);
    }

    public async Task<DonationResult> CaptureAsync(CaptureDonationRequest request, CancellationToken cancellationToken)
    {
        ValidateOrderId(request.OrderId);
        var donation = await _donations.Find(x => x.PayPalOrderId == request.OrderId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Donation order was not found.");
        if (donation.Status is "SUCCEEDED" or "COMPLETED") return new DonationResult("COMPLETED", donation.Amount, donation.Currency);
        if (donation.Status is "REFUNDED" or "REVERSED" or "FAILED" or "PROCESSING") throw new InvalidOperationException("This donation cannot be captured again while its state is terminal or processing.");
        var started = await _donations.UpdateOneAsync(x => x.Id == donation.Id && x.Status == "PENDING",
            Builders<Donation>.Update.Set(x => x.Status, "PROCESSING"), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (started.ModifiedCount != 1)
        {
            donation = await _donations.Find(x => x.Id == donation.Id).FirstAsync(cancellationToken).ConfigureAwait(false);
            if (donation.Status is "SUCCEEDED" or "COMPLETED") return new DonationResult("COMPLETED", donation.Amount, donation.Currency);
            throw new InvalidOperationException("Donation capture is already being processed.");
        }

        // PayPal-Request-Id is stable per order and server side; concurrent retries converge on one capture.
        PayPalOrder capture;
        try
        {
            capture = await SendPayPalAsync<PayPalOrder>(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(request.OrderId)}/capture", new { }, cancellationToken,
                "capture-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OrderId))).ToLowerInvariant()[..32]).ConfigureAwait(false);
        }
        catch
        {
            await _donations.UpdateOneAsync(x => x.Id == donation.Id && x.Status == "PROCESSING", Builders<Donation>.Update.Set(x => x.Status, "PENDING"), cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        var captureUnit = capture.PurchaseUnits?.FirstOrDefault();
        var captureInfo = captureUnit?.Payments?.Captures?.FirstOrDefault(x => x.Status == "COMPLETED");
        if (capture.Status != "COMPLETED" || captureInfo is null || string.IsNullOrWhiteSpace(captureInfo.Id) ||
            captureInfo.Amount?.CurrencyCode != donation.Currency ||
            !decimal.TryParse(captureInfo.Amount.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var capturedAmount) || capturedAmount != donation.Amount ||
            (captureUnit?.Payee?.MerchantId is { Length: > 0 } merchantId && !string.IsNullOrWhiteSpace(_options.MerchantId) && merchantId != _options.MerchantId))
        {
            await _donations.UpdateOneAsync(x => x.Id == donation.Id && x.Status == "PROCESSING", Builders<Donation>.Update.Set(x => x.Status, "FAILED"), cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("PayPal did not confirm the expected completed donation.");
        }

        var now = DateTimeOffset.UtcNow;
        await MarkSucceededAsync(donation, captureInfo.Id, now, cancellationToken).ConfigureAwait(false);
        return new DonationResult("COMPLETED", donation.Amount, donation.Currency);
    }

    public async Task<bool> HandleWebhookAsync(PayPalWebhookHeaders headers, string rawBody, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookId) || string.IsNullOrWhiteSpace(rawBody) || rawBody.Length > 250_000) return false;
        using var eventJson = JsonDocument.Parse(rawBody);
        var root = eventJson.RootElement;
        var eventId = StringProperty(root, "id");
        var eventType = StringProperty(root, "event_type");
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 128 || eventType is not ("PAYMENT.CAPTURE.COMPLETED" or "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.REFUNDED" or "PAYMENT.CAPTURE.REVERSED")) return false;

        var verified = await VerifyWebhookAsync(headers, root, cancellationToken).ConfigureAwait(false);
        if (!verified) return false;
        try { await _events.InsertOneAsync(new DonationWebhookEvent { EventId = eventId, EventType = eventType, Status = "Processing" }, cancellationToken: cancellationToken).ConfigureAwait(false); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            var prior = await _events.Find(x => x.EventId == eventId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (prior?.Status == "Processed") return true;
            var retry = await _events.UpdateOneAsync(x => x.EventId == eventId && x.Status == "Failed",
                Builders<DonationWebhookEvent>.Update.Set(x => x.Status, "Processing").Inc(x => x.Attempts, 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (retry.ModifiedCount == 0)
            {
                var staleBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
                retry = await _events.UpdateOneAsync(x => x.EventId == eventId && x.Status == "Processing" && x.ReceivedAtUtc <= staleBefore,
                    Builders<DonationWebhookEvent>.Update.Inc(x => x.Attempts, 1), cancellationToken: cancellationToken).ConfigureAwait(false);
                if (retry.ModifiedCount == 0) return true;
            }
        }

        try
        {
            var resource = root.GetProperty("resource");
            var captureId = StringProperty(resource, "id");
            var orderId = resource.TryGetProperty("supplementary_data", out var supplementary) && supplementary.TryGetProperty("related_ids", out var related) ? StringProperty(related, "order_id") : string.Empty;
            var donation = !string.IsNullOrWhiteSpace(orderId)
                ? await _donations.Find(x => x.PayPalOrderId == orderId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                : await _donations.Find(x => x.PayPalCaptureId == captureId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (donation is null) throw new InvalidOperationException("Webhook does not reference a known donation.");

            var eventTime = ParseDate(StringProperty(root, "create_time")) ?? DateTimeOffset.UtcNow;
            if (donation.LastPaymentEventAtUtc is null || eventTime >= donation.LastPaymentEventAtUtc)
            {
                if (resource.TryGetProperty("payee", out var webhookPayee) && webhookPayee.TryGetProperty("merchant_id", out var webhookMerchant) && !string.IsNullOrWhiteSpace(_options.MerchantId) && webhookMerchant.GetString() != _options.MerchantId)
                    throw new InvalidOperationException("Webhook merchant did not match the configured merchant.");
                if (eventType == "PAYMENT.CAPTURE.COMPLETED")
                {
                    var amount = resource.GetProperty("amount");
                    if (StringProperty(amount, "currency_code") != donation.Currency || !decimal.TryParse(StringProperty(amount, "value"), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value != donation.Amount || StringProperty(resource, "status") != "COMPLETED")
                        throw new InvalidOperationException("Webhook payment amount or state did not match the donation.");
                    await MarkSucceededAsync(donation, captureId, eventTime, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var next = eventType switch { "PAYMENT.CAPTURE.DENIED" => "FAILED", "PAYMENT.CAPTURE.REFUNDED" => "REFUNDED", _ => "REVERSED" };
                    var update = Builders<Donation>.Update.Set(x => x.Status, next).Set(x => x.LastPaymentEventAtUtc, eventTime);
                    await _donations.UpdateOneAsync(x => x.Id == donation.Id && (x.LastPaymentEventAtUtc == null || x.LastPaymentEventAtUtc <= eventTime), update, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
            await _events.UpdateOneAsync(x => x.EventId == eventId, Builders<DonationWebhookEvent>.Update.Set(x => x.Status, "Processed").Set(x => x.ProcessedAtUtc, DateTimeOffset.UtcNow), cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            await _events.UpdateOneAsync(x => x.EventId == eventId, Builders<DonationWebhookEvent>.Update.Set(x => x.Status, "Failed").Inc(x => x.Attempts, 1), cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<DonorLeaderboardResult> GetLeaderboardAsync(CancellationToken cancellationToken)
    {
        var entries = await _donations.Find(x => (x.Status == "SUCCEEDED" || x.Status == "COMPLETED") && x.ShowOnLeaderboard)
            .SortByDescending(x => x.Amount).ThenBy(x => x.PaidAtUtc).Limit(50)
            .Project(x => new { x.DonorName, x.Amount, x.Remark, PaidAtUtc = x.PaidAtUtc }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var totals = await _donations.Aggregate().Match(x => x.Status == "SUCCEEDED" || x.Status == "COMPLETED")
            .Group(_ => 1, group => new DonationTotals(group.Count(), group.Sum(x => x.Amount))).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return new DonorLeaderboardResult(entries.Select((x, i) => new DonorLeaderboardEntry(NormalizeName(x.DonorName), x.Amount, NormalizeRemark(x.Remark), x.PaidAtUtc ?? DateTimeOffset.MinValue, i + 1)).ToArray(), totals?.Count ?? 0, totals?.Amount ?? 0m);
    }

    private async Task MarkSucceededAsync(Donation donation, string captureId, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        var update = await _donations.UpdateOneAsync(x => x.Id == donation.Id && x.Status != "REFUNDED" && x.Status != "REVERSED" && x.Status != "FAILED" && (x.LastPaymentEventAtUtc == null || x.LastPaymentEventAtUtc <= atUtc),
            Builders<Donation>.Update.Set(x => x.Status, "SUCCEEDED").Set(x => x.PayPalCaptureId, captureId).Set(x => x.PaidAtUtc, atUtc).Set(x => x.LastPaymentEventAtUtc, atUtc), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (update.ModifiedCount == 0 && donation.Status is "REFUNDED" or "REVERSED" or "FAILED") return;
        if (update.ModifiedCount == 0)
        {
            var current = await _donations.Find(x => x.Id == donation.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null && (current.Status is "REFUNDED" or "REVERSED" or "FAILED" || current.LastPaymentEventAtUtc > atUtc)) return;
        }
        if (string.IsNullOrWhiteSpace(donation.DonorEmail) || string.IsNullOrWhiteSpace(donation.Id)) return;
        var email = new DonationReceiptOutbox { DonationId = donation.Id, Name = donation.DonorName, Email = donation.DonorEmail, Amount = donation.Amount, Currency = donation.Currency, RetainUntilUtc = DateTimeOffset.UtcNow.AddDays(Math.Clamp(_options.EmailRetentionDays, 1, 3650)) };
        try { await _receipts.InsertOneAsync(email, cancellationToken: cancellationToken).ConfigureAwait(false); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
    }

    private async Task<bool> VerifyWebhookAsync(PayPalWebhookHeaders headers, JsonElement webhookEvent, CancellationToken cancellationToken)
    {
        if (new[] { headers.TransmissionId, headers.TransmissionTime, headers.CertificateUrl, headers.AuthAlgorithm, headers.TransmissionSignature }.Any(string.IsNullOrWhiteSpace)) return false;
        if (!Uri.TryCreate(headers.CertificateUrl, UriKind.Absolute, out var certUrl) || certUrl.Scheme != Uri.UriSchemeHttps || !certUrl.Host.EndsWith("paypal.com", StringComparison.OrdinalIgnoreCase)) return false;
        using var response = await SendPayPalRawAsync(HttpMethod.Post, "/v1/notifications/verify-webhook-signature", new
        {
            auth_algo = headers.AuthAlgorithm, cert_url = headers.CertificateUrl, transmission_id = headers.TransmissionId,
            transmission_sig = headers.TransmissionSignature, transmission_time = headers.TransmissionTime,
            webhook_id = _options.WebhookId, webhook_event = webhookEvent
        }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return false;
        using var result = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
        return StringProperty(result.RootElement, "verification_status") == "SUCCESS";
    }

    private async Task<T> SendPayPalAsync<T>(HttpMethod method, string path, object body, CancellationToken cancellationToken, string? requestId = null)
    {
        using var response = await SendPayPalRawAsync(method, path, body, cancellationToken, requestId).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("PayPal API request {Path} failed with status {StatusCode}.", path, (int)response.StatusCode);
            throw new InvalidOperationException("PayPal could not process the donation request.");
        }
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PayPal returned an invalid response.");
    }

    private async Task<HttpResponseMessage> SendPayPalRawAsync(HttpMethod method, string path, object body, CancellationToken cancellationToken, string? requestId = null)
    {
        var client = httpClientFactory.CreateClient("PayPal");
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v1/oauth2/token");
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" });
        using var tokenResponse = await client.SendAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
        if (!tokenResponse.IsSuccessStatusCode) throw new InvalidOperationException("PayPal authentication failed.");
        var token = await tokenResponse.Content.ReadFromJsonAsync<PayPalToken>(JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("PayPal authentication response was invalid.");
        var request = new HttpRequestMessage(method, $"{ApiBase}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        if (requestId is not null) request.Headers.Add("PayPal-Request-Id", requestId);
        request.Content = JsonContent.Create(body);
        try { return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false); }
        finally { request.Dispose(); }
    }

    private void ValidateAmount(decimal amount)
    {
        if (amount < _options.MinimumDonation || amount > _options.MaximumDonation || decimal.Round(amount, 2) != amount)
            throw new ArgumentOutOfRangeException(nameof(amount), "Donation amount is outside the allowed range or precision.");
        if (_options.Currency.Length != 3 || !_options.Currency.All(char.IsAsciiLetter))
            throw new InvalidOperationException("Donation currency is not configured correctly.");
    }
    private string BuildRequestFingerprint(CreateDonationRequest request, decimal amount)
    {
        var normalized = string.Join("|", amount.ToString("0.00", CultureInfo.InvariantCulture), _options.Currency, NormalizeName(request.Name), NormalizeEmail(request.Email), NormalizeRemark(request.ShowOnLeaderboard ? request.Remark : null), request.ShowOnLeaderboard);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
    private static void ValidateOrderId(string? id) { if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid donation order.", nameof(id)); }
    private static string StringProperty(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : string.Empty;
    private static DateTimeOffset? ParseDate(string value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
    private static string NormalizeName(string? value) => SanitizeDisplayText(string.IsNullOrWhiteSpace(value) ? "Anonymous" : value.Trim(), 80);
    private static string NormalizeEmail(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, 254)];
    private static string NormalizeRemark(string? value) => SanitizeDisplayText(value?.Trim() ?? string.Empty, 240);
    private static string SanitizeDisplayText(string value, int maxLength)
    {
        var filtered = new string(value.Where(c => !char.IsControl(c) && c is not '<' and not '>' and not '\\' and not '`').ToArray());
        return filtered[..Math.Min(filtered.Length, maxLength)];
    }
    private sealed record DonationTotals(int Count, decimal Amount);
    private sealed record PayPalToken([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record PayPalOrder(string Id, string Status, [property: JsonPropertyName("purchase_units")] List<PayPalPurchaseUnit>? PurchaseUnits);
    private sealed record PayPalPurchaseUnit(PayPalAmount? Amount, PayPalPayments? Payments, PayPalPayee? Payee);
    private sealed record PayPalPayee([property: JsonPropertyName("merchant_id")] string? MerchantId);
    private sealed record PayPalAmount([property: JsonPropertyName("currency_code")] string CurrencyCode, string Value);
    private sealed record PayPalPayments(List<PayPalCapture>? Captures);
    private sealed record PayPalCapture(string? Id, string? Status, PayPalAmount? Amount);
}
