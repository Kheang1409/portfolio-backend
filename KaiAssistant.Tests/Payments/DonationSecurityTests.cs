using System;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KaiAssistant.Application.Donations;
using KaiAssistant.Application.Services;
using KaiAssistant.Domain.Entities;
using KaiAssistant.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using Xunit;

namespace KaiAssistant.Tests.Payments;

public sealed class DonationSecurityTests
{
    [Fact]
    public async Task CreateOrder_RejectsAmountWithUnsupportedPrecisionBeforePayPal()
    {
        using var runner = Integration.MongoTestRunner.Start();
        var db = new MongoClient(runner.ConnectionString).GetDatabase("donation-tests");
        var handler = new StubHandler(_ => throw new Xunit.Sdk.XunitException("PayPal must not be called for invalid amount."));
        var service = Create(db, handler);

        var act = () => service.CreateOrderAsync(new CreateDonationRequest(10.999m, "A", null, null, false, Guid.NewGuid().ToString()), default);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Capture_RejectsWhenCaptureAmountDiffersFromServerOrder()
    {
        using var runner = Integration.MongoTestRunner.Start();
        var db = new MongoClient(runner.ConnectionString).GetDatabase("donation-tests");
        var donations = db.GetCollection<KaiAssistant.Domain.Entities.Donations.Donation>("donations");
        await donations.InsertOneAsync(new KaiAssistant.Domain.Entities.Donations.Donation { PayPalOrderId = "ORDER123", Amount = 10m, Currency = "USD", Status = "PENDING" });
        var handler = new StubHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal)) return Json("{\"access_token\":\"test-token\"}");
            return Json("{\"id\":\"ORDER123\",\"status\":\"COMPLETED\",\"purchase_units\":[{\"payments\":{\"captures\":[{\"id\":\"CAPTURE123\",\"status\":\"COMPLETED\",\"amount\":{\"currency_code\":\"USD\",\"value\":\"9.00\"}}]}}]}");
        });
        var service = Create(db, handler);

        var act = () => service.CaptureAsync(new CaptureDonationRequest("ORDER123"), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Webhook_RejectsInvalidSignatureWithoutPersistingEvent()
    {
        using var runner = Integration.MongoTestRunner.Start();
        var db = new MongoClient(runner.ConnectionString).GetDatabase("donation-tests");
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/v1/oauth2/token", StringComparison.Ordinal)
            ? Json("{\"access_token\":\"test-token\"}") : Json("{\"verification_status\":\"FAILURE\"}"));
        var service = Create(db, handler, webhookId: "webhook-id");
        using var document = System.Text.Json.JsonDocument.Parse("{\"id\":\"EVENT1\",\"event_type\":\"PAYMENT.CAPTURE.COMPLETED\"}");

        var result = await service.HandleWebhookAsync(new PayPalWebhookHeaders("tx", "time", "https://api.paypal.com/cert", "SHA256withRSA", "sig"), document.RootElement.GetRawText(), default);

        result.Should().BeFalse();
        (await db.GetCollection<KaiAssistant.Domain.Entities.Donations.DonationWebhookEvent>("donation_webhook_events").CountDocumentsAsync(FilterDefinition<KaiAssistant.Domain.Entities.Donations.DonationWebhookEvent>.Empty)).Should().Be(0);
    }

    [Fact]
    public async Task Leaderboard_ExcludesUnconfirmedAndPrivateDonations()
    {
        using var runner = Integration.MongoTestRunner.Start();
        var db = new MongoClient(runner.ConnectionString).GetDatabase("donation-tests");
        await db.GetCollection<KaiAssistant.Domain.Entities.Donations.Donation>("donations").InsertManyAsync([
            new() { PayPalOrderId = "A", Amount = 8, Status = "SUCCEEDED", ShowOnLeaderboard = true, DonorName = "<script>x</script>", Remark = "<img src=x onerror=alert(1)>" },
            new() { PayPalOrderId = "B", Amount = 90, Status = "PENDING", ShowOnLeaderboard = true, DonorName = "Pending" },
            new() { PayPalOrderId = "C", Amount = 70, Status = "SUCCEEDED", ShowOnLeaderboard = false, DonorEmail = "private@example.test" }
        ]);
        var service = Create(db, new StubHandler(_ => Json("{}")));

        var result = await service.GetLeaderboardAsync(default);

        result.Entries.Should().ContainSingle();
        result.Entries[0].Name.Should().Be("scriptx/script");
        result.Entries[0].Remark.Should().Be("img src=x onerror=alert(1)");
        result.TotalDonations.Should().Be(2);
        result.TotalAmount.Should().Be(78);
    }

    private static PayPalDonationService Create(IMongoDatabase db, StubHandler handler, string webhookId = "")
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("PayPal")).Returns(() => new HttpClient(handler, disposeHandler: false));
        return new PayPalDonationService(factory.Object, db, new EmailSettings("", 25, "", "", "", false), Options.Create(new PayPalOptions
        {
            ClientId = "client", ClientSecret = "secret", Environment = "sandbox", Currency = "USD", MinimumDonation = 1, MaximumDonation = 1000, WebhookId = webhookId
        }), NullLogger<PayPalDonationService>.Instance);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
